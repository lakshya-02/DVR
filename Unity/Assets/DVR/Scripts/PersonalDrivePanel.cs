using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DVR
{
    public sealed class PersonalDrivePanel : MonoBehaviour
    {
        private const string WiredOrigin = "http://127.0.0.1:48180";
        private enum View { Unconfigured, Connect, Pair, Browse, Account, ConfirmDisconnect, Revoking }

        [Serializable] private sealed class Settings { public string origin = null; }
        [Serializable] private sealed class Pairing
        {
            public string bearer = null;
            public string entryUrl = null;
            public string code = null;
            public string qr = null;
        }
        [Serializable] private sealed class Session { public string state = null; public Pairing pairing = null; }
        [Serializable] private sealed class Page { public Video[] files = null; public string nextCursor = null; }
        [Serializable] private sealed class Video { public string id = null; public string name = null; public string resourceKey = null; }
        [Serializable] private sealed class Failure { public string error = null; }

        private static readonly Regex BearerPattern = new Regex("^[A-Za-z0-9_-]{43}$");
        private static readonly Regex ItemPattern = new Regex("^[A-Za-z0-9_-]{1,256}$");
        private readonly List<string> cursors = new List<string>();
        private Television television;
        private Text title;
        private Text status;
        private Text[] labels;
        private Button[] buttons;
        private Button previousButton;
        private Button nextButton;
        private Button searchButton;
        private Button settingsButton;
        private RawImage qr;
        private Texture2D qrTexture;
        private DriveDeviceCredential credential;
        private UnityWebRequest request;
        private Coroutine fetch;
        private TouchScreenKeyboard keyboard;
        private string origin;
        private string bearer;
        private string query = "";
        private string nextCursor;
        private string storageNote;
        private int pageIndex;
        private float nextPoll;
        private Pairing pairing;
        private Video[] videos = Array.Empty<Video>();
        private View view;
        private bool visible;
        private bool wiredPilot = false;

        public void Initialize(Television screen, Text heading, Text feedback, Text[] rows, RawImage pairingImage = null)
        {
            television = screen;
            title = heading;
            status = feedback;
            labels = rows;
            buttons = new Button[rows.Length];
            for (var i = 0; i < rows.Length; i++) buttons[i] = rows[i].GetComponentInParent<Button>(true);
            qr = pairingImage;
            if (qr) qr.gameObject.SetActive(false);

            var asset = Resources.Load<TextAsset>("DvrDriveSettings");
            var settings = asset ? Read<Settings>(asset.text) : null;
            var configuredOrigin = settings?.origin;
#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (!asset) configuredOrigin = WiredOrigin;
            wiredPilot = configuredOrigin == WiredOrigin;
#endif
            if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var service)
                || (!wiredPilot && service.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(service.UserInfo)
                || service.AbsolutePath != "/" || !string.IsNullOrEmpty(service.Query) || !string.IsNullOrEmpty(service.Fragment)) return;

            origin = service.GetLeftPart(UriPartial.Authority);
            credential = new DriveDeviceCredential(origin);
            bearer = credential.Load();
            if (bearer != null && !BearerPattern.IsMatch(bearer)) ForgetConnection();
        }

        public void BindNavigation(Button previous, Button next, Button search, Button settings)
        {
            previousButton = previous;
            nextButton = next;
            searchButton = search;
            settingsButton = settings;
        }

        public void Show()
        {
            if (visible || labels == null) return;
            visible = true;
            if (origin == null)
            {
                Display(View.Unconfigured, "Google Drive is unavailable in this build.");
                return;
            }
            if (bearer == null)
            {
                Display(View.Connect, wiredPilot ? "Start the DVR companion on your PC. Leave Quest connected by USB."
                    : "Pair your headset with Google Drive.", "Connect Google Drive");
                return;
            }
            if (pairing != null) DisplayPairing();
            else Display(View.Browse, "Checking your Google Drive connection…");
            CheckSession();
        }

        public void Hide()
        {
            if (!visible) return;
            visible = false;
            if (fetch != null) StopCoroutine(fetch);
            fetch = null;
            request?.Abort();
            request?.Dispose();
            request = null;
            if (keyboard != null) keyboard.active = false;
            keyboard = null;
            if (qr) qr.gameObject.SetActive(false);
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (qrTexture) Destroy(qrTexture);
        }

        public void Select(int index)
        {
            if (!visible || request != null || index < 0 || index >= labels.Length) return;
            switch (view)
            {
                case View.Connect when index == 0:
                    StartPairing();
                    break;
                case View.Pair when index == 0:
                case View.ConfirmDisconnect when index == 0:
                case View.Revoking when index == 0:
                    Disconnect();
                    break;
                case View.Browse when index < videos.Length:
                    Play(videos[index]);
                    break;
                case View.Account when index == 0:
                case View.ConfirmDisconnect when index == 1:
                    ShowVideos();
                    break;
                case View.Account when index == 1:
                    Display(View.ConfirmDisconnect, "Disconnect and revoke DVR’s Google Drive access?", "Disconnect", "Keep connection");
                    break;
            }
        }

        public void Previous()
        {
            if (visible && view == View.Browse && pageIndex > 0) LoadPage(cursors[pageIndex - 1], pageIndex - 1, query);
        }

        public void Next()
        {
            if (visible && view == View.Browse && !string.IsNullOrEmpty(nextCursor)) LoadPage(nextCursor, pageIndex + 1, query);
        }

        public void Search()
        {
            if (!visible || view != View.Browse || keyboard != null || request != null) return;
            if (!TouchScreenKeyboard.isSupported)
            {
                status.text = "The search keyboard is unavailable on this device.";
                return;
            }
            keyboard = TouchScreenKeyboard.Open(query, TouchScreenKeyboardType.Default, false, false, false, false, "Video name", 120);
        }

        public void Back()
        {
            if (!visible || request != null) return;
            switch (view)
            {
                case View.Browse:
                    Display(View.Account, "Google Drive is connected.", "Browse videos", "Disconnect Google Drive");
                    break;
                case View.Account:
                case View.ConfirmDisconnect:
                    ShowVideos();
                    break;
            }
        }

        private void Update()
        {
            if (!visible) return;
            if (view == View.Pair && request == null && Time.unscaledTime >= nextPoll) CheckSession();
            if (keyboard == null || keyboard.status == TouchScreenKeyboard.Status.Visible) return;
            var completed = keyboard.status == TouchScreenKeyboard.Status.Done;
            var term = keyboard.text.Trim();
            keyboard = null;
            if (completed) LoadPage("", 0, term);
        }

        private void StartPairing()
        {
            Display(View.Connect, "Getting a pairing code…", "Connect Google Drive");
            Send("POST", "/v1/pairings", (code, body) =>
            {
                var created = Read<Pairing>(body);
                if (code != 201 || !ValidPairing(created) || created.bearer == null || !BearerPattern.IsMatch(created.bearer))
                {
                    Display(View.Connect, Error(body, DriveUnavailable("Could not get a pairing code. Try again.")), "Connect Google Drive");
                    return;
                }
                bearer = created.bearer;
                storageNote = credential.Save(bearer) ? null : "Pair again after restarting DVR.";
                pairing = created;
                ResetQr();
                DisplayPairing();
                nextPoll = Time.unscaledTime + 2f;
            });
        }

        private void DisplayPairing()
        {
            var instructions = wiredPilot ? "On your USB-connected PC, open" : "Scan the QR or visit";
            Display(View.Pair, $"{instructions}\n{pairing.entryUrl}\nCode  {pairing.code}", "Cancel pairing");
        }

        private void CheckSession()
        {
            var path = pairing == null ? "/v1/session?details=1" : "/v1/session";
            Send("GET", path, (code, body) =>
            {
                if (code == 401 || code == 410)
                {
                    ForgetConnection();
                    Display(View.Connect, Error(body, "Pair again to continue."), "Connect Google Drive");
                    return;
                }
                var session = code == 200 ? Read<Session>(body) : null;
                switch (session?.state)
                {
                    case "pending" when pairing != null || ValidPairing(session.pairing):
                        if (pairing == null)
                        {
                            pairing = session.pairing;
                            ResetQr();
                            DisplayPairing();
                        }
                        nextPoll = Time.unscaledTime + 2f;
                        break;
                    case "disconnecting":
                        Display(View.Revoking, "Google access still needs to be revoked.", "Retry disconnect");
                        break;
                    case "connected":
                        if (pairing != null)
                        {
                            pairing = null;
                            ResetQr();
                        }
                        nextCursor = null;
                        videos = Array.Empty<Video>();
                        query = "";
                        pageIndex = 0;
                        cursors.Clear();
                        LoadPage("", 0, "");
                        break;
                    default:
                        if (view == View.Pair) nextPoll = Time.unscaledTime + 5f;
                        else Display(View.Account, DriveUnavailable("Could not check Google Drive. Try again."), "Try again", "Disconnect Google Drive");
                        break;
                }
            });
        }

        private void LoadPage(string cursor, int index, string term)
        {
            if (request != null) return;
            var path = $"/v1/videos?limit={labels.Length}&q={Uri.EscapeDataString(term)}";
            if (!string.IsNullOrEmpty(cursor)) path += "&cursor=" + Uri.EscapeDataString(cursor);
            Display(View.Browse, "Loading videos…");
            Send("GET", path, (code, body) =>
            {
                var page = code == 200 ? Read<Page>(body) : null;
                if (code == 401)
                {
                    ForgetConnection();
                    Display(View.Connect, Error(body, "Pair again to continue."), "Connect Google Drive");
                }
                else if (code == 409)
                {
                    CheckSession();
                }
                else if (page?.files == null)
                {
                    ShowVideos(Error(body, DriveUnavailable("Could not load videos. Try again.")));
                }
                else
                {
                    if (index == 0) cursors.Clear();
                    if (cursors.Count <= index) cursors.Add(cursor);
                    else if (cursors[index] != cursor)
                    {
                        cursors.RemoveRange(index, cursors.Count - index);
                        cursors.Add(cursor);
                    }
                    pageIndex = index;
                    query = term;
                    nextCursor = page.nextCursor;
                    videos = page.files;
                    ShowVideos();
                }
            });
        }

        private void ShowVideos(string message = null)
        {
            if (cursors.Count == 0 && message == null)
            {
                LoadPage("", 0, query);
                return;
            }
            if (message == null)
            {
                message = videos.Length > 0 ? $"Page {pageIndex + 1} · Choose a video."
                    : string.IsNullOrEmpty(nextCursor) ? "No videos found." : "No videos on this page. Choose Next.";
                if (query.Length > 0) message = $"Search  {query}\n{message}";
                if (storageNote != null) message += "\n" + storageNote;
            }
            var names = Array.ConvertAll(videos, video => FileName(video?.name));
            Display(View.Browse, message, names);
        }

        private bool ValidPairing(Pairing candidate) => candidate?.entryUrl == origin + "/pair" && !string.IsNullOrEmpty(candidate.code);

        private void Play(Video video)
        {
            if (video?.id == null || !ItemPattern.IsMatch(video.id)) return;
            var url = origin + "/v1/media/" + video.id;
            if (!string.IsNullOrEmpty(video.resourceKey) && ItemPattern.IsMatch(video.resourceKey)) url += "?key=" + video.resourceKey;
            television.Play(url, message =>
            {
                if (visible && view == View.Browse) status.text = FileName(video.name) + "\n" + message;
            }, movie: true, bearer: bearer);
        }

        private void Disconnect()
        {
            var cancel = view == View.Pair;
            if (!cancel)
            {
                television.Stop();
                Display(View.Revoking, "Revoking Google Drive access…");
            }
            Send("DELETE", "/v1/session", (code, body) =>
            {
                if (code == 204 || code == 401)
                {
                    ForgetConnection();
                    Display(View.Connect, cancel ? "Pairing cancelled." : "Google Drive disconnected.", "Connect Google Drive");
                }
                else if (cancel)
                {
                    DisplayPairing();
                    status.text += "\nCould not cancel. Try again.";
                }
                else Display(View.Revoking, Error(body, "Could not finish revoking Google access."), "Retry disconnect");
            });
        }

        private void ForgetConnection()
        {
            credential?.Delete();
            bearer = null;
            pairing = null;
            storageNote = null;
            ResetQr();
        }

        private void ResetQr()
        {
            if (qrTexture) Destroy(qrTexture);
            qrTexture = null;
            if (!qr) return;
            qr.texture = null;
            qr.gameObject.SetActive(false);
            if (string.IsNullOrEmpty(pairing?.qr)) return;
            try
            {
                var image = Convert.FromBase64String(pairing.qr);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (texture.LoadImage(image, true))
                {
                    qrTexture = texture;
                    qr.texture = texture;
                }
                else Destroy(texture);
            }
            catch (FormatException) { }
        }

        private void Display(View current, string message, params string[] choices)
        {
            view = current;
            title.text = current == View.Pair ? "Connect Google Drive" : "Personal · Google Drive";
            title.supportRichText = false;
            status.text = message;
            status.supportRichText = false;
            for (var i = 0; i < labels.Length; i++)
            {
                var shown = i < choices.Length;
                labels[i].text = shown ? choices[i] : "";
                labels[i].supportRichText = false;
                if (buttons[i]) buttons[i].gameObject.SetActive(shown);
            }
            if (qr) qr.gameObject.SetActive(current == View.Pair && qrTexture);
            UpdateNavigation();
        }

        private void UpdateNavigation()
        {
            var browse = view == View.Browse && request == null;
            if (previousButton) previousButton.interactable = browse && pageIndex > 0;
            if (nextButton) nextButton.interactable = browse && !string.IsNullOrEmpty(nextCursor);
            if (searchButton) searchButton.interactable = browse;
            if (settingsButton) settingsButton.interactable = request == null
                && (browse || view == View.Account || view == View.ConfirmDisconnect);
        }

        private void Send(string method, string path, Action<long, string> done)
        {
            if (!visible || request != null) return;
            request = new UnityWebRequest(origin + path, method, new DownloadHandlerBuffer(), null)
            {
                timeout = 20,
                redirectLimit = 0,
            };
            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
            UpdateNavigation();
            fetch = StartCoroutine(Receive(request, done));
        }

        private IEnumerator Receive(UnityWebRequest active, Action<long, string> done)
        {
            yield return active.SendWebRequest();
            var code = active.responseCode;
            var body = active.downloadHandler.text;
            request = null;
            fetch = null;
            active.Dispose();
            if (visible)
            {
                done(code, body);
                UpdateNavigation();
            }
        }

        private static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }

        private static string Error(string body, string fallback) => Read<Failure>(body)?.error ?? fallback;

        private string DriveUnavailable(string message) => wiredPilot
            ? "Start the DVR companion on your PC and reconnect Quest by USB." : message;

        private static string FileName(string name) => string.IsNullOrWhiteSpace(name)
            ? "Untitled video" : name.Replace('\r', ' ').Replace('\n', ' ');
    }
}
