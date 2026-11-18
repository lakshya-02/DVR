using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace DVR
{
    public sealed partial class DiscDeck : MonoBehaviour
    {
        [Serializable]
        public sealed class DiscObject
        {
            public string catalogId;
            public Transform home;
            public Rigidbody body;
            public Grabbable grabbable;
            public GameObject[] interactions;

            internal readonly HashSet<int> pointers = new();
            internal Action<PointerEvent> listener;
            internal ChannelCatalog.Disc media;
            internal bool released;
            internal XRNode hand;
        }

        [Serializable]
        public sealed class SceneLinks
        {
            public Transform head, leftController, rightController, station, slot, screen;
            public GameObject sourcePanel;
            public DiscObject[] discs;
            public Button[] rows;
            public Button previous, next, search, back, random, eject, panelEject;
            public Button room, recenter, move, hide, reveal, netflix, prime;
            public Button pause, rewind, forward, quieter, louder;
            public Text title, status, footer, screenStatus, volumeLabel;
            public RawImage qr;
            public Image readyLight;
            public Television television;
            public PersonalDrivePanel personal;
            public AudioSource mechanicalAudio;
        }

        [SerializeField] private SceneLinks scene;

        private readonly System.Random random = new();
        private DiscObject loaded;
        private int page;
        private int selected = -1;
        private float volume = 1;
        private bool placed;
        private bool theaterMode;
        private bool controlsVisible = true;
        private bool movingScreen;
        private bool hadPicture;
        private Vector2 screenOffset;
        private uint handoffRequest;

        private Television Television => scene.television;
        private bool IsPersonal => loaded?.media.Kind == ChannelCatalog.DiscKind.Personal;
        private bool IsFilm => loaded?.media.Kind is ChannelCatalog.DiscKind.OfflineFilm or ChannelCatalog.DiscKind.OnlineFilm;
        private bool IsLive => loaded != null && !IsFilm && !IsPersonal;

        public void Configure(SceneLinks links) => scene = links;

        private void Start()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var disc in scene.discs)
            {
                foreach (var media in ChannelCatalog.Discs)
                {
                    if (media.Id != disc.catalogId) continue;
                    disc.media = media;
                    break;
                }

                if (disc.media.Id == null || !ids.Add(disc.catalogId))
                    throw new InvalidOperationException($"Disc has unknown or duplicate catalog ID {disc.catalogId}.");
                disc.listener = interaction => OnDiscEvent(disc, interaction);
                disc.grabbable.WhenPointerEventRaised += disc.listener;
            }

            var rowLabels = new Text[scene.rows.Length];
            for (var i = 0; i < scene.rows.Length; i++)
            {
                var index = i;
                rowLabels[i] = Label(scene.rows[i]);
                scene.rows[i].onClick.AddListener(() => SelectRow(index));
            }

            scene.personal.Initialize(Television, scene.title, scene.status, rowLabels, scene.qr);
            scene.personal.BindNavigation(scene.previous, scene.next, scene.search, scene.back);
            scene.previous.onClick.AddListener(() => StepPage(-1));
            scene.next.onClick.AddListener(() => StepPage(1));
            scene.search.onClick.AddListener(() => scene.personal.Search());
            scene.back.onClick.AddListener(() => scene.personal.Back());
            scene.random.onClick.AddListener(RandomChannel);
            scene.eject.onClick.AddListener(Eject);
            scene.panelEject.onClick.AddListener(Eject);
            scene.recenter.onClick.AddListener(Recenter);
            scene.room.onClick.AddListener(ToggleTheater);
            scene.move.onClick.AddListener(ToggleMove);
            scene.hide.onClick.AddListener(ToggleControls);
            scene.reveal.onClick.AddListener(ToggleControls);
            scene.netflix.onClick.AddListener(() => OpenService("Netflix", "https://www.netflix.com/"));
            scene.prime.onClick.AddListener(() => OpenService("Prime Video", "https://www.primevideo.com/"));
            scene.pause.onClick.AddListener(() => Television.TogglePause());
            scene.rewind.onClick.AddListener(() => Television.SeekRelative(-10));
            scene.forward.onClick.AddListener(() => Television.SeekRelative(10));
            scene.quieter.onClick.AddListener(() => AdjustVolume(-0.1f));
            scene.louder.onClick.AddListener(() => AdjustVolume(0.1f));
            SetPlaybackControls();
            RefreshPanel();
            OVRManager.InputFocusLost += ReleaseHeldDiscs;
            scene.station.gameObject.SetActive(false);
#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
            RunRequestedDeviceProbe();
#endif
        }

        private void Update()
        {
            if (!placed) return;
            if (OVRInput.GetDown(OVRInput.RawButton.B | OVRInput.RawButton.Y, OVRInput.Controller.Touch))
                ToggleControls();
            if (OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch) && HeadReady())
                Recenter();
            if (movingScreen)
            {
                var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
                if (stick.sqrMagnitude > 0.04f)
                {
                    screenOffset += stick * (Time.unscaledDeltaTime * 0.40f);
                    screenOffset.x = Mathf.Clamp(screenOffset.x, -0.43f, 0.28f);
                    screenOffset.y = Mathf.Clamp(screenOffset.y, -0.32f, 0.32f);
                    PositionScreen();
                }
            }

            var playing = Television.HasPicture;
            if (playing != hadPicture && controlsVisible == playing) ToggleControls();
            hadPicture = playing;
            var showMessage = !playing && controlsVisible;
            if (scene.screenStatus.enabled != showMessage) scene.screenStatus.enabled = showMessage;
            scene.readyLight.color = loaded == null
                ? new Color32(217, 149, 80, 255)
                : playing ? new Color32(150, 225, 181, 255)
                : new Color(0.95f, 0.65f, 0.32f, 0.45f + 0.5f * Mathf.PingPong(Time.unscaledTime * 1.8f, 1));
            SetPlaybackControls();
        }

        private void LateUpdate()
        {
            if (!placed && HeadReady()) Recenter();

            foreach (var disc in scene.discs)
            {
                if (!disc.released) continue;
                disc.released = false;
                if (disc.pointers.Count != 0 || loaded == disc) continue;

                // The grab transform is current even before the next physics step.
                var offset = scene.slot.InverseTransformPoint(disc.body.transform.position);
                var onTray = Mathf.Abs(offset.x) <= 0.10f && Mathf.Abs(offset.y) <= 0.065f
                    && Mathf.Abs(offset.z) <= 0.095f;
                if (onTray && loaded == null) Insert(disc);
                else
                {
                    Park(disc, disc.home);
                    if (onTray) scene.footer.text = "Eject the loaded disc first.";
                }
            }
        }

        private bool HeadReady()
        {
#if UNITY_EDITOR
            if (!XRSettings.isDeviceActive) return true;
#endif
            return OVRManager.hasInputFocus && (OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter)
                || scene.head.localPosition.sqrMagnitude > 0.1f);
        }

        private void OnDiscEvent(DiscObject disc, PointerEvent interaction)
        {
            switch (interaction.Type)
            {
                case PointerEventType.Select:
                    disc.pointers.Add(interaction.Identifier);
                    disc.released = false;
                    var point = interaction.Pose.position;
                    var left = (point - scene.leftController.position).sqrMagnitude;
                    var right = (point - scene.rightController.position).sqrMagnitude;
                    disc.hand = left < right ? XRNode.LeftHand : XRNode.RightHand;
                    break;
                case PointerEventType.Unselect:
                    disc.released |= disc.pointers.Remove(interaction.Identifier);
                    break;
                case PointerEventType.Cancel:
                    if (disc.pointers.Remove(interaction.Identifier) && disc.pointers.Count == 0 && loaded != disc)
                        Park(disc, disc.home);
                    disc.released = false;
                    break;
            }
        }

        private void ReleaseHeldDiscs()
        {
            foreach (var disc in scene.discs)
            {
                if (disc.pointers.Count == 0 && !disc.released) continue;
                var pose = new Pose(disc.body.transform.position, disc.body.transform.rotation);
                foreach (var pointer in new List<int>(disc.pointers))
                    disc.grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Cancel, pose));
                disc.pointers.Clear();
                disc.released = false;
                if (loaded != disc) Park(disc, disc.home);
            }
        }

        public void Recenter()
        {
            foreach (var disc in scene.discs)
                if (disc.pointers.Count > 0) return;

            var forward = Vector3.ProjectOnPlane(scene.head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.Cross(scene.head.right, Vector3.up);

            scene.station.SetPositionAndRotation(scene.head.position, Quaternion.LookRotation(forward, Vector3.up));
            foreach (var disc in scene.discs)
                Park(disc, loaded == disc ? scene.slot : disc.home);
            screenOffset = Vector2.zero;
            PositionScreen();
            placed = true;
            scene.station.gameObject.SetActive(true);
            Debug.Log("[DVR] Station placed");
        }

        private void PositionScreen()
        {
            var center = new Vector3(-0.30f, 0.06f, 3.02f);
            var scale = (theaterMode ? 4f : 3f) / 1.04f;
            scene.screen.localPosition = center + new Vector3(screenOffset.x, screenOffset.y, 0);
            scene.screen.localScale = new Vector3(scale, scale, 1f);
        }

        private void ToggleTheater()
        {
            theaterMode = !theaterMode;
            Label(scene.room).text = theaterMode ? "Lounge" : "Theater";
            screenOffset = Vector2.zero;
            PositionScreen();
            Debug.Log($"[DVR] {(theaterMode ? "Theater" : "Lounge")}");
        }

        private void ToggleMove()
        {
            movingScreen = !movingScreen;
            Label(scene.move).text = movingScreen ? "Done moving" : "Move screen";
            scene.footer.text = movingScreen ? "Use the left thumbstick to position the screen." : FooterText();
        }

        private void ToggleControls()
        {
            if (movingScreen) ToggleMove();
            controlsVisible = !controlsVisible;
            scene.hide.transform.parent.gameObject.SetActive(controlsVisible);
            scene.sourcePanel.SetActive(controlsVisible);
            if (!controlsVisible) scene.screenStatus.enabled = false;
        }

        private void Insert(DiscObject disc)
        {
            if (loaded != null) return;

            handoffRequest++;
            loaded = disc;
            foreach (var interaction in disc.interactions) interaction.SetActive(false);
            StartCoroutine(SeatDisc(disc));
            Feedback(disc.hand);
            RefreshPanel();
            if (!controlsVisible) ToggleControls();
            if (IsFilm) PlayFilm();
            Debug.Log($"[DVR] Disc inserted {disc.media.Id}");
        }

        private System.Collections.IEnumerator SeatDisc(DiscObject disc)
        {
            var station = scene.station;
            var start = station.InverseTransformPoint(disc.body.transform.position);
            var rotation = Quaternion.Inverse(station.rotation) * disc.body.transform.rotation;
            var elapsed = 0f;
            while (loaded == disc && elapsed < 0.14f)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.SmoothStep(0, 1, elapsed / 0.14f);
                var position = Vector3.Lerp(station.TransformPoint(start), scene.slot.position, t);
                var angle = Quaternion.Slerp(station.rotation * rotation, scene.slot.rotation, t);
                disc.body.transform.SetPositionAndRotation(position, angle);
                yield return null;
            }
            if (loaded == disc) Park(disc, scene.slot);
        }

        public void Eject()
        {
            if (loaded == null) return;

            var disc = loaded;
            handoffRequest++;
            if (IsPersonal) scene.personal.Hide();
            Television.Stop();
            hadPicture = false;
            loaded = null;
            selected = -1;
            page = 0;
            Park(disc, disc.home);
            foreach (var interaction in disc.interactions) interaction.SetActive(true);
            Feedback(disc.hand);
            RefreshPanel();
            Debug.Log($"[DVR] Disc ejected {disc.media.Id}");
        }

        private void Feedback(XRNode hand)
        {
            scene.mechanicalAudio.Play();
            var device = InputDevices.GetDeviceAtXRNode(hand);
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0, 0.38f, 0.07f);
        }

        private static void Park(DiscObject disc, Transform target)
        {
            disc.body.transform.SetPositionAndRotation(target.position, target.rotation);
            disc.body.position = target.position;
            disc.body.rotation = target.rotation;
        }

        private void RefreshPanel()
        {
            scene.footer.text = FooterText();
            scene.title.text = loaded?.media.Name ?? "Disc library";
            scene.status.text = loaded == null ? "Insert a disc, or open an official streaming service."
                : IsFilm ? "Film" : IsPersonal ? "Google Drive" : loaded.media.Channels.Count == 0 ? "No channels are available." : "Select a live channel.";
            scene.screenStatus.text = loaded == null ? "Insert a disc" : scene.title.text;
            scene.eject.interactable = loaded != null;
            scene.panelEject.interactable = loaded != null;
            scene.previous.gameObject.SetActive(IsLive || IsPersonal);
            scene.next.gameObject.SetActive(IsLive || IsPersonal);
            scene.search.gameObject.SetActive(IsPersonal);
            scene.back.gameObject.SetActive(IsPersonal);
            scene.previous.interactable = true;
            scene.next.interactable = true;
            scene.random.gameObject.SetActive(IsLive);
            scene.netflix.gameObject.SetActive(loaded == null || IsPersonal);
            scene.prime.gameObject.SetActive(loaded == null || IsPersonal);
            for (var i = 0; i < scene.rows.Length; i++)
                scene.rows[i].gameObject.SetActive(IsPersonal);

            if (IsLive) DisplayChannelPage();
            if (IsFilm) SetRow(0, "Play from beginning");
            if (IsPersonal) scene.personal.Show();
        }

        private string FooterText() => loaded == null ? "Grip a disc and place it on the tray." : $"{loaded.media.Name}  •  Loaded";

        private void DisplayChannelPage()
        {
            var channels = loaded.media.Channels;
            var start = page * scene.rows.Length;
            for (var i = 0; i < scene.rows.Length; i++)
            {
                var index = start + i;
                if (index < channels.Count)
                    SetRow(i, (index == selected ? "▶  " : "") + channels[index].Name);
                else
                    scene.rows[i].gameObject.SetActive(false);
            }

            scene.previous.interactable = page > 0;
            scene.next.interactable = start + scene.rows.Length < channels.Count;
            scene.random.interactable = channels.Count > 0;
        }

        private void SetRow(int index, string value)
        {
            scene.rows[index].gameObject.SetActive(true);
            var label = Label(scene.rows[index]);
            label.text = value;
            label.color = new Color32(245, 224, 189, 255);
        }

        private void SelectRow(int index)
        {
            if (IsPersonal) scene.personal.Select(index);
            if (IsFilm && index == 0) PlayFilm();
            if (IsLive) SelectChannel(page * scene.rows.Length + index);
        }

        private void StepPage(int direction)
        {
            if (IsPersonal)
            {
                if (direction < 0) scene.personal.Previous();
                else scene.personal.Next();
            }
            if (!IsLive) return;
            var last = Mathf.Max(0, (loaded.media.Channels.Count - 1) / scene.rows.Length);
            page = Mathf.Clamp(page + direction, 0, last);
            DisplayChannelPage();
        }

        private void SelectChannel(int index)
        {
            if (!IsLive || index < 0 || index >= loaded.media.Channels.Count) return;
            selected = index;
            page = index / scene.rows.Length;
            DisplayChannelPage();
            var channel = loaded.media.Channels[index];
            Debug.Log($"[DVR] Selected {channel.Name}");
            hadPicture = false;
            Television.Play(channel.Url, message => ShowPlaybackStatus(channel.Name, message));
        }

        public void RandomChannel()
        {
            if (IsLive) SelectChannel(ChannelCatalog.PickOther(loaded.media.Channels.Count, selected, random));
        }

        private void PlayFilm()
        {
            if (!IsFilm) return;
            var film = loaded.media;
            hadPicture = false;
            if (film.Kind == ChannelCatalog.DiscKind.OfflineFilm)
                Television.PlayDemo(message => ShowPlaybackStatus(film.Name, message));
            else
                Television.Play(film.Url, message => ShowPlaybackStatus(film.Name, message), movie: true);
        }

        private void ShowPlaybackStatus(string item, string message)
        {
            scene.status.text = $"{item}\n{message}";
            scene.screenStatus.text = $"{item}\n{message}";
        }

        private void AdjustVolume(float delta)
        {
            var previous = volume;
            volume = Mathf.Clamp01(volume + delta);
            Television.ChangeVolume(volume - previous);
            scene.volumeLabel.text = $"{Mathf.RoundToInt(volume * 100)}%";
        }

        private void SetPlaybackControls()
        {
            var movie = (IsFilm || IsPersonal) && Television.HasPicture;
            if (scene.pause.interactable == movie) return;
            scene.pause.interactable = movie;
            scene.rewind.interactable = movie;
            scene.forward.interactable = movie;
        }

        private void OpenService(string label, string url)
        {
            Television.Stop();
            hadPicture = false;
            if (!controlsVisible) ToggleControls();
            ShowPlaybackStatus(label, "Opening official service…");

#if UNITY_ANDROID && !UNITY_EDITOR
            var request = ++handoffRequest;
            var unityThread = System.Threading.SynchronizationContext.Current;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    string result;
                    try
                    {
                        using var current = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        using var app = current.GetStatic<AndroidJavaObject>("currentActivity");
                        using var uriClass = new AndroidJavaClass("android.net.Uri");
                        using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url);
                        using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW", uri);
                        app.Call("startActivity", intent);
                        result = "Continue in the browser or provider app. Return to DVR when finished.";
                        Debug.Log($"[DVR] {label} handoff accepted by Android");
                    }
                    catch (AndroidJavaException)
                    {
                        result = $"Could not open the service. Visit {new Uri(url).Host} in your browser.";
                        Debug.LogWarning($"[DVR] {label} handoff failed");
                    }

                    unityThread.Post(_ =>
                    {
                        if (this && request == handoffRequest) ShowPlaybackStatus(label, result);
                    }, null);
                }));
            }
            catch (AndroidJavaException)
            {
                ShowPlaybackStatus(label, $"Could not open the service. Visit {new Uri(url).Host} in your browser.");
                Debug.LogWarning($"[DVR] {label} handoff failed");
            }
#else
            Application.OpenURL(url);
            ShowPlaybackStatus(label, "Requested the official site in your browser.");
#endif
        }

        private static Text Label(Button button) => button.GetComponentInChildren<Text>(true);

        private void OnDestroy()
        {
            OVRManager.InputFocusLost -= ReleaseHeldDiscs;
            if (scene?.discs == null) return;
            foreach (var disc in scene.discs)
                if (disc.grabbable != null) disc.grabbable.WhenPointerEventRaised -= disc.listener;
        }
    }
}
