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
        private enum View { Unconfigured, Connect, Pair, Checking, Browse, FolderSources, ConnectionError, ListError, Account, ConfirmDisconnect, Revoking }
        private enum Source { Library, Sources, Folder, Shared, Drives, Search }

        [Serializable] private sealed class Settings { public string origin = null; }
        [Serializable] private sealed class Pairing
        {
            public string bearer = null;
            public string entryUrl = null;
            public string code = null;
            public string qr = null;
        }
        [Serializable] private sealed class Session { public string state = null; public Pairing pairing = null; }
        [Serializable] private sealed class Page { public Entry[] files = null; public string nextCursor = null; }
        [Serializable] private sealed class Identity { public string displayName = null; public string emailAddress = null; }
        [Serializable] private sealed class Entry
        {
            public string id = null;
            public string name = null;
            public string resourceKey = null;
            public string driveId = null;
            public string mimeType = null;
            public string kind = null;
        }
        [Serializable] private sealed class Failure { public string error = null; }

        private sealed class Location
        {
            internal readonly Source Source;
            internal readonly string Name;
            internal readonly string Id;
            internal readonly string Key;
            internal readonly string Drive;

            internal Location(Source source, string name, string id = null, string key = null, string drive = null)
            {
                Source = source;
                Name = name;
                Id = id;
                Key = key;
                Drive = drive;
            }
        }

        private static readonly Regex BearerPattern = new Regex("^[A-Za-z0-9_-]{43}$");
        private static readonly Regex ItemPattern = new Regex("^[A-Za-z0-9_-]{1,256}$");
        private static readonly Location Library = new Location(Source.Library, "Library");
        private static readonly Location Folders = new Location(Source.Sources, "Folders");
        private readonly List<Location> ancestors = new List<Location>();
        private readonly List<string> cursors = new List<string>();
        private Television television;
        private Text title;
        private Text status;
        private Text accountLabel;
        private Text[] labels;
        private Button[] buttons;
        private Button previousButton;
        private Button nextButton;
        private Button searchButton;
        private Button settingsButton;
        private Button libraryButton;
        private Text settingsLabel;
        private Text libraryLabel;
        private RawImage qr;
        private Texture2D qrTexture;
        private DriveDeviceCredential credential;
        private UnityWebRequest request;
        private Coroutine fetch;
        private TouchScreenKeyboard keyboard;
        private string origin;
        private string bearer;
        private Location location = Folders;
        private Identity identity;
        private string nextCursor;
        private string retryCursor;
        private int retryIndex;
        private string storageNote;
        private int pageIndex;
        private float nextPoll;
        private Pairing pairing;
        private Entry[] entries = Array.Empty<Entry>();
        private View view;
        private bool visible;
        private bool wiredPilot = false;

        public void Initialize(Television screen, Text heading, Text feedback, Text[] rows, RawImage pairingImage = null, Text account = null)
        {
            television = screen;
            title = heading;
            status = feedback;
            accountLabel = account;
            if (accountLabel) accountLabel.supportRichText = false;
            labels = rows;
            buttons = new Button[rows.Length];
            for (var i = 0; i < rows.Length; i++) buttons[i] = rows[i].GetComponentInParent<Button>(true);
            qr = pairingImage;
            if (qr) qr.gameObject.SetActive(false);

            var asset = Resources.Load<TextAsset>("DvrDriveSettings");
            var settings = asset ? Read<Settings>(asset.text) : null;
            var configuredOrigin = settings?.origin;
#if UNITY_ANDROID && DEVELOPMENT_BUILD && DVR_WIRED_PILOT && !UNITY_EDITOR
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
            settingsLabel = settings ? settings.GetComponentInChildren<Text>(true) : null;
        }

        public void BindLibraryButton(Button library)
        {
            libraryButton = library;
            libraryLabel = library ? library.GetComponentInChildren<Text>(true) : null;
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
            else Display(View.Checking, "Checking your Google Drive connection…");
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
                case View.FolderSources when index == 0:
                    Navigate(new Location(Source.Folder, "My Drive", "root"));
                    break;
                case View.FolderSources when index == 1:
                    Navigate(new Location(Source.Shared, "Shared with me"));
                    break;
                case View.FolderSources when index == 2:
                    Navigate(new Location(Source.Drives, "Shared drives"));
                    break;
                case View.Browse when index < entries.Length:
                    Open(entries[index]);
                    break;
                case View.ListError when index == 0:
                    LoadPage(retryCursor, retryIndex);
                    break;
                case View.ConnectionError when index == 0:
                    CheckSession();
                    break;
                case View.Account when index == 0:
                case View.ConfirmDisconnect when index == 1:
                    ShowLocation();
                    break;
                case View.Account when index == 1:
                    Display(View.ConfirmDisconnect, "Disconnect and revoke DVR’s Google Drive access?", "Disconnect", "Keep connection");
                    break;
            }
        }

        public void Previous()
        {
            if (visible && view == View.Browse && pageIndex > 0) LoadPage(cursors[pageIndex - 1], pageIndex - 1);
        }

        public void Next()
        {
            if (visible && view == View.Browse && !string.IsNullOrEmpty(nextCursor)) LoadPage(nextCursor, pageIndex + 1);
        }

        public void ToggleLibrary()
        {
            if (!CanNavigate) return;
            Navigate(location.Source == Source.Library || location.Source == Source.Search ? Folders : Library, false);
        }

        public void Search()
        {
            if (!CanNavigate || keyboard != null) return;
            if (!TouchScreenKeyboard.isSupported)
            {
                status.text = "The search keyboard is unavailable on this device.";
                return;
            }
            var initial = location.Source == Source.Search ? location.Name : "";
            keyboard = TouchScreenKeyboard.Open(initial, TouchScreenKeyboardType.Default, false, false, false, false, "Folder or video name", 120);
        }

        public void Back()
        {
            if (!visible || request != null) return;
            switch (view)
            {
                case View.Browse:
                case View.FolderSources:
                case View.ListError:
                    if (!GoUp()) Display(View.Account, "Google Drive settings", "Return to Drive", "Disconnect Google Drive");
                    break;
                case View.Account:
                case View.ConfirmDisconnect:
                    ShowLocation();
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
            if (completed) SearchFor(term);
        }

        private bool CanNavigate => visible && request == null
            && (view == View.Browse || view == View.FolderSources || view == View.ListError);

        private void SearchFor(string term)
        {
            if (!CanNavigate) return;
            if (string.IsNullOrEmpty(term))
            {
                if (location.Source == Source.Search) GoUp();
                return;
            }
            if (location.Source == Source.Search)
            {
                location = new Location(Source.Search, term);
                ResetPage();
                ShowLocation();
            }
            else Navigate(new Location(Source.Search, term));
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
            if (view != View.Pair) Display(View.Checking, "Checking your Google Drive connection…");
            var path = pairing == null ? "/v1/session?details=1" : "/v1/session";
            Send("GET", path, (code, body) =>
            {
                if (code == 401 || code == 410)
                {
                    ShowSignedOut(body);
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
                        identity = null;
                        Display(View.Revoking, "Google access still needs to be revoked.", "Retry disconnect");
                        break;
                    case "connected":
                        if (pairing != null)
                        {
                            pairing = null;
                            ResetQr();
                        }
                        LoadIdentity();
                        break;
                    default:
                        if (view == View.Pair) nextPoll = Time.unscaledTime + 5f;
                        else Display(View.ConnectionError, DriveUnavailable("Could not check Google Drive. Try again."), "Retry connection");
                        break;
                }
            });
        }

        private void LoadIdentity()
        {
            Display(View.Checking, "Loading your Google account…");
            Send("GET", "/v1/account", (code, body) =>
            {
                if (code == 401) ShowSignedOut(body);
                else if (code == 409) CheckSession();
                else
                {
                    identity = code == 200 ? Read<Identity>(body) : null;
                    ResetPage();
                    ShowLocation();
                }
            });
        }

        private void ShowSignedOut(string body)
        {
            television.Stop();
            ForgetConnection();
            Display(View.Connect, Error(body, "Pair again to continue."), "Connect Google Drive");
        }

        private void Navigate(Location next, bool remember = true)
        {
            if (remember) ancestors.Add(location);
            else ancestors.Clear();
            location = next;
            ResetPage();
            ShowLocation();
        }

        private bool GoUp()
        {
            if (ancestors.Count == 0) return false;
            location = ancestors[ancestors.Count - 1];
            ancestors.RemoveAt(ancestors.Count - 1);
            ResetPage();
            ShowLocation();
            return true;
        }

        private void ResetPage()
        {
            entries = Array.Empty<Entry>();
            nextCursor = null;
            pageIndex = 0;
            cursors.Clear();
        }

        private void ShowLocation()
        {
            if (location.Source == Source.Sources)
            {
                Display(View.FolderSources, "Choose where to browse. Search finds folders and videos across Drive.",
                    "My Drive", "Shared with me", "Shared drives");
            }
            else if (cursors.Count == 0) LoadPage("", 0);
            else ShowEntries();
        }

        private void LoadPage(string cursor, int index)
        {
            if (request != null) return;
            var path = location.Source == Source.Library ? $"/v1/videos?limit={labels.Length}" : $"/v1/items?limit={labels.Length}";
            switch (location.Source)
            {
                case Source.Folder:
                    path += "&source=folder&parent=" + location.Id;
                    if (!string.IsNullOrEmpty(location.Key)) path += "&key=" + location.Key;
                    if (!string.IsNullOrEmpty(location.Drive)) path += "&drive=" + location.Drive;
                    break;
                case Source.Shared:
                    path += "&source=shared";
                    break;
                case Source.Drives:
                    path += "&source=drives";
                    break;
                case Source.Search:
                    path += "&q=" + Uri.EscapeDataString(location.Name);
                    break;
            }
            if (!string.IsNullOrEmpty(cursor)) path += "&cursor=" + Uri.EscapeDataString(cursor);
            Display(View.Checking, Breadcrumb() + "\nLoading…");
            Send("GET", path, (code, body) =>
            {
                var page = code == 200 ? Read<Page>(body) : null;
                if (code == 401) ShowSignedOut(body);
                else if (code == 409) CheckSession();
                else if (page?.files == null)
                {
                    retryCursor = code == 400 ? "" : cursor;
                    retryIndex = code == 400 ? 0 : index;
                    Display(View.ListError, Breadcrumb() + "\n" + Error(body, DriveUnavailable("Could not load this list.")), "Reload list");
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
                    nextCursor = page.nextCursor;
                    entries = page.files;
                    ShowEntries();
                }
            });
        }

        private void ShowEntries()
        {
            var message = entries.Length > 0 ? $"Page {pageIndex + 1}"
                : string.IsNullOrEmpty(nextCursor) ? "No folders or videos here." : "No items on this page. Choose Next.";
            if (storageNote != null) message += "\n" + storageNote;
            Display(View.Browse, Breadcrumb() + "\n" + message, Array.ConvertAll(entries, EntryName));
        }

        private string Breadcrumb()
        {
            if (location.Source == Source.Search) return "Search · " + SafeLabel(location.Name, "");
            if (ancestors.Count == 0 || ancestors[ancestors.Count - 1].Source == Source.Sources) return location.Name;
            var parent = ancestors[ancestors.Count - 1];
            return (ancestors.Count > 2 ? "… › " : "") + parent.Name + " › " + location.Name;
        }

        private static string EntryName(Entry entry)
        {
            var folder = entry?.kind == "folder" || entry?.kind == "drive";
            return SafeLabel(entry?.name, folder ? "Untitled folder" : "Untitled video") + (folder ? "  ›" : "");
        }

        private void Open(Entry entry)
        {
            if (!ValidId(entry?.id)) return;
            if (entry.kind == "folder" || entry.kind == "drive")
            {
                var drive = entry.kind == "drive" ? entry.id : ValidId(entry.driveId) ? entry.driveId : null;
                Navigate(new Location(Source.Folder, SafeLabel(entry.name, "Untitled folder"), entry.id,
                    ValidId(entry.resourceKey) ? entry.resourceKey : null, drive));
            }
            else Play(entry);
        }

        private static bool ValidId(string value) => !string.IsNullOrEmpty(value) && ItemPattern.IsMatch(value);

        private bool ValidPairing(Pairing candidate) => candidate?.entryUrl == origin + "/pair" && !string.IsNullOrEmpty(candidate.code);

        private void Play(Entry video)
        {
            var url = origin + "/v1/media/" + video.id;
            if (ValidId(video.resourceKey)) url += "?key=" + video.resourceKey;
            var playingFrom = location;
            television.Play(url, message =>
            {
                if (visible && view == View.Browse && location == playingFrom) status.text = EntryName(video) + "\n" + message;
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
            identity = null;
            location = Folders;
            ancestors.Clear();
            ResetPage();
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
            if (accountLabel) accountLabel.text = AccountLabel(current);
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
            var browse = CanNavigate && view == View.Browse;
            if (previousButton) previousButton.interactable = browse && pageIndex > 0;
            if (nextButton) nextButton.interactable = browse && !string.IsNullOrEmpty(nextCursor);
            if (searchButton) searchButton.interactable = CanNavigate;
            if (libraryButton) libraryButton.interactable = CanNavigate;
            if (libraryLabel) libraryLabel.text = location.Source == Source.Library || location.Source == Source.Search ? "Folders" : "Library";
            if (settingsButton) settingsButton.interactable = request == null
                && (CanNavigate || view == View.Account || view == View.ConfirmDisconnect);
            if (settingsLabel) settingsLabel.text = ancestors.Count > 0 || view == View.Account || view == View.ConfirmDisconnect ? "Back" : "Settings";
        }

        private string AccountLabel(View current)
        {
            if (current == View.Pair) return "Waiting for Google approval";
            if (current == View.Revoking) return "Disconnecting Google Drive";
            if (current == View.Connect || current == View.Unconfigured) return "Signed out";
            if (current == View.ConnectionError) return "Connection unavailable";
            var email = SafeLabel(identity?.emailAddress, "");
            var name = SafeLabel(identity?.displayName, "");
            if (email.Length == 0 && name.Length == 0) return current == View.Checking ? "Checking account…" : "Account details unavailable";
            if (email.Length == 0 || name.Length == 0 || name == email) return "Signed in as " + (name.Length == 0 ? email : name);
            return "Signed in as " + name + "\n" + email;
        }

        private void Send(string method, string path, Action<long, string> done)
        {
            if (!visible || request != null) return;
            request = new UnityWebRequest(origin + path, method, new DownloadHandlerBuffer(), null)
            {
                timeout = 50,
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

        private static string SafeLabel(string name, string fallback) => string.IsNullOrWhiteSpace(name)
            ? fallback : Regex.Replace(name, @"[\p{Cc}\u202A-\u202E\u2066-\u2069]", " ");

#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
        public IEnumerator VerifyLibrary(string folderName)
        {
            yield return WaitForDriveResponse();
            if (!CanNavigate || string.IsNullOrEmpty(identity?.emailAddress))
            {
                DriveProbe(false, view == View.Connect || view == View.Pair ? "Google account is not connected" : "Google account identity unavailable");
                yield break;
            }
            DriveProbe(true, "Google account identity present");
            if (!DriveProbe(view == View.FolderSources, "folders view opened")) yield break;
            Select(0);
            yield return WaitForDriveResponse();
            if (!DriveProbe(view == View.Browse && location.Id == "root", "My Drive root opened")) yield break;

            var requested = folderName?.Trim();
            if (!DriveProbe(!string.IsNullOrEmpty(requested), "folder query available")) yield break;
            SearchFor(requested);
            yield return WaitForDriveResponse();
            var folderIndex = -1;
            for (var pages = 0; view == View.Browse && location.Source == Source.Search && pages < 40; pages++)
            {
                folderIndex = Array.FindIndex(entries, entry => entry?.kind == "folder" && ValidId(entry.id)
                    && string.Equals(entry.name, requested, StringComparison.OrdinalIgnoreCase));
                if (folderIndex >= 0 || string.IsNullOrEmpty(nextCursor)) break;
                Next();
                yield return WaitForDriveResponse();
            }
            if (!DriveProbe(view == View.Browse && location.Source == Source.Search, "Drive-wide folder search loaded")) yield break;
            if (!DriveProbe(folderIndex >= 0, string.IsNullOrEmpty(nextCursor) ? "exact folder found" : "exact folder found within probe page limit")) yield break;
            Select(folderIndex);
            yield return WaitForDriveResponse();
            if (!DriveProbe(view == View.Browse && location.Source == Source.Folder, "matched folder opened")) yield break;

            var videoIndex = -1;
            for (var pages = 0; view == View.Browse && location.Source == Source.Folder && pages < 40; pages++)
            {
                videoIndex = Array.FindIndex(entries, entry => entry?.kind == "video" && ValidId(entry.id)
                    && (entry.mimeType == "video/mp4" || entry.mimeType == "video/webm"));
                if (videoIndex >= 0 || string.IsNullOrEmpty(nextCursor)) break;
                Next();
                yield return WaitForDriveResponse();
            }
            if (!DriveProbe(view == View.Browse && location.Source == Source.Folder, "matched folder contents loaded")) yield break;
            if (videoIndex < 0)
            {
                Debug.Log("[DVR Drive check] SKIP playback: no MP4 or WebM video in the matched folder");
                yield break;
            }
            Select(videoIndex);
            var deadline = Time.unscaledTime + 25f;
            while (visible && !television.HasPicture && Time.unscaledTime < deadline) yield return null;
            if (!DriveProbe(visible && television.HasPicture, "private video first frame")) yield break;
            var frames = television.DecodedFrames;
            yield return new WaitForSecondsRealtime(1.25f);
            if (!DriveProbe(television.HasPicture && television.DecodedFrames > frames, "private video frames advancing")) yield break;

            deadline = Time.unscaledTime + 6f;
            while (visible && television.HasPicture && television.PlaybackSeconds < 3 && Time.unscaledTime < deadline) yield return null;
            var before = television.PlaybackSeconds;
            if (!DriveProbe(television.HasPicture && before >= 2.5, "player reached seek position")) yield break;
            television.SeekRelative(-10);
            deadline = Time.unscaledTime + 5f;
            while (visible && television.HasPicture && television.PlaybackSeconds > before - 1 && Time.unscaledTime < deadline) yield return null;
            DriveProbe(television.HasPicture && television.PlaybackSeconds <= before - 1, "private video seek moved playback position");
        }

        private IEnumerator WaitForDriveResponse()
        {
            var deadline = Time.unscaledTime + 55f;
            while (visible && request != null && Time.unscaledTime < deadline) yield return null;
        }

        private static bool DriveProbe(bool passed, string step)
        {
            Debug.Log($"[DVR Drive check] {(passed ? "PASS" : "FAIL")} {step}");
            return passed;
        }
#endif
    }
}
