#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace DVR
{
    public sealed partial class DiscDeck
    {
        private int probeFailures;

        private void RunRequestedDeviceProbe()
        {
            try
            {
                using var launcher = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = launcher.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null) return;

                var openService = intent.Call<string>("getStringExtra", "dvr_open");
                var preview = intent.Call<string>("getStringExtra", "dvr_preview");
                var folder = intent.Call<string>("getStringExtra", "dvr_drive_check");
                var handoff = openService == "netflix" || openService == "prime";
                var showPreview = preview == "offline" || preview == "news" || preview == "sports" || preview == "personal";
                var checkDrive = !string.IsNullOrWhiteSpace(folder) && folder.Length <= 100;
                var probe = intent.Call<bool>("getBooleanExtra", "dvr_probe", false);
                if (!handoff && !showPreview && !checkDrive && !probe) return;

                using var files = activity.Call<AndroidJavaObject>("getFilesDir");
                using var permit = new AndroidJavaObject("java.io.File", files, "dvr-diagnostics-once");
                if (!permit.Call<bool>("delete")) return;

                if (handoff) StartCoroutine(ProbeHandoff(openService));
                else if (showPreview) StartCoroutine(PreviewSource(preview));
                else if (checkDrive) StartCoroutine(ProbeDriveLibrary(folder));
                else StartCoroutine(DeviceProbe(intent.Call<bool>("getBooleanExtra", "dvr_capture", false)));
            }
            catch (AndroidJavaException)
            {
                Debug.LogWarning("[DVR] Optional diagnostics could not be started");
            }
        }

        private IEnumerator ProbeDriveLibrary(string name)
        {
            yield return Until(() => placed, 90);
            if (!ProbeCheck(placed, "Drive station placed")) yield break;
            yield return null;
            var disc = Array.Find(scene.discs, entry => entry.catalogId == "personal");
            if (!ProbeCheck(disc != null, "personal disc present")) yield break;
            yield return ReleaseOnTray(disc);
            if (!ProbeCheck(loaded == disc, "personal disc inserted")) yield break;
            yield return scene.personal.VerifyLibrary(name);
            Eject();
        }

        private IEnumerator ProbeHandoff(string service)
        {
            yield return Until(() => placed, 90);
            if (!placed) yield break;
            yield return null;
            var personal = Array.Find(scene.discs, disc => disc.catalogId == "personal");
            yield return ReleaseOnTray(personal);
            if (!ProbeCheck(IsPersonal, "personal disc inserted for service handoff")) yield break;
            var button = service == "netflix" ? scene.netflix : scene.prime;
            button.onClick.Invoke();
            Debug.Log($"[DVR probe] Requested {service} handoff");
        }

        private IEnumerator PreviewSource(string id)
        {
            yield return Until(() => placed, 90);
            if (!ProbeCheck(placed, "preview station placed")) yield break;
            yield return null;
            var disc = Array.Find(scene.discs, item => item.catalogId == id);
            if (!ProbeCheck(disc != null, $"preview disc present ({id})")) yield break;
            yield return ReleaseOnTray(disc);
            if (!ProbeCheck(loaded == disc, $"preview disc inserted ({id})")) yield break;
            if (IsPersonal)
            {
                if (Label(scene.rows[0]).text == "Connect Google Drive") scene.rows[0].onClick.Invoke();
                Debug.Log("[DVR preview] Personal disc opened");
                yield break;
            }
            if (IsLive) scene.rows[0].onClick.Invoke();
            yield return CheckFrames(disc.media.Name + " preview");
            Debug.Log("[DVR preview] Source left in normal controls. X recenters. B/Y shows controls.");
        }

        private IEnumerator DeviceProbe(bool capture)
        {
            yield return Until(() => placed, 45);
            if (!ProbeCheck(placed, "head tracking and projection room placement")) yield break;
            yield return Until(OVRManager.IsInsightPassthroughInitialized, 6);
            ProbeCheck(OVRManager.IsInsightPassthroughInitialized(), "Meta passthrough initialized");
            yield return null;

            var offline = Array.Find(scene.discs, disc => disc.media.Kind == ChannelCatalog.DiscKind.OfflineFilm);
            var personal = Array.Find(scene.discs, disc => disc.media.Kind == ChannelCatalog.DiscKind.Personal);
            var live = Array.Find(scene.discs, disc => disc.media.Channels.Count > 0);
            if (!ProbeCheck(offline != null && personal != null && live != null, "offline, personal and live source discs present"))
                yield break;

            yield return ProbeRayTransport(live);

            yield return ReleaseOnTray(live);
            if (!ProbeCheck(loaded == live && !Television.HasPicture, "live disc inserted with blank screen")) yield break;

            yield return ReleaseOnTray(offline);
            ProbeCheck(loaded == live && Vector3.Distance(offline.body.transform.position, offline.home.position) < 0.01f,
                "second disc returned to its cradle while the tray was occupied");

            var unavailable = false;
            Television.Play("https://dvr-probe.invalid/unavailable.m3u8",
                message => unavailable = message != "Connecting…" && message != "Playing");
            yield return Until(() => unavailable, 26);
            ProbeCheck(unavailable && !Television.HasPicture, "invalid stream kept the screen blank");
            Eject();

            foreach (var disc in scene.discs)
            {
                if (disc == offline || disc == personal) continue;
                yield return ReleaseOnTray(disc);
                if (!ProbeCheck(loaded == disc, $"{disc.media.Name} seated")) continue;

                if (IsFilm)
                    yield return CheckFrames(disc.media.Name);
                else if (ProbeCheck(disc.media.Channels.Count > 0, $"{disc.media.Name} provides live channels"))
                {
                    scene.rows[0].onClick.Invoke();
                    yield return CheckFrames(disc.media.Channels[0].Name);
                    if (disc.media.Channels.Count > 1)
                    {
                        scene.random.onClick.Invoke();
                        ProbeCheck(selected != 0 && !Television.HasPicture, "random switch cleared the prior picture");
                        var randomIndex = selected;
                        yield return CheckFrames(disc.media.Channels[randomIndex].Name);
                        for (var i = 1; i < disc.media.Channels.Count; i++)
                        {
                            if (i == randomIndex) continue;
                            SelectChannel(i);
                            yield return CheckFrames(disc.media.Channels[i].Name);
                        }
                    }
                }

                Eject();
                ProbeCheck(loaded == null && !Television.HasPicture, $"{disc.media.Name} ejected and screen cleared");
            }

            yield return ReleaseOnTray(live);
            scene.rows[0].onClick.Invoke();
            Eject();
            yield return new WaitForSecondsRealtime(1);
            ProbeCheck(loaded == null && !Television.HasPicture && Vector3.Distance(live.body.transform.position, live.home.position) < 0.01f,
                "eject during loading restored the disc and blank screen");

            yield return ReleaseOnTray(personal);
            ProbeCheck(IsPersonal && !Television.HasPicture && !string.IsNullOrWhiteSpace(scene.status.text) && scene.title.text.Contains("Personal")
                && scene.netflix.gameObject.activeInHierarchy && scene.prime.gameObject.activeInHierarchy,
                "personal library showed Drive status and official Netflix and Prime Video buttons");
            Eject();

            yield return ReleaseOnTray(offline);
            yield return CheckFrames(offline.media.Name + " offline");
            ProbeCheck(!controlsVisible && scene.reveal.gameObject.activeInHierarchy,
                "first movie frame tucked the panels and kept the hardware Controls button available");
            scene.reveal.onClick.Invoke();
            scene.pause.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.35f);
            var pausedFrame = Television.DecodedFrames;
            yield return new WaitForSecondsRealtime(1);
            ProbeCheck(Television.HasPicture && Math.Abs(Television.DecodedFrames - pausedFrame) <= 1,
                "pause retained the last offline film frame");
            scene.pause.onClick.Invoke();
            scene.forward.onClick.Invoke();
            yield return Until(() => Television.DecodedFrames > pausedFrame + 90, 7);
            ProbeCheck(Television.DecodedFrames > pausedFrame + 90, "movie seek advanced the playhead");
            scene.quieter.onClick.Invoke();
            ProbeCheck(scene.volumeLabel.text == "90%", "volume control updated to 90%");
            scene.louder.onClick.Invoke();

            var loungeScale = scene.screen.localScale;
            if (!controlsVisible) scene.reveal.onClick.Invoke();
            scene.room.onClick.Invoke();
            ProbeCheck(theaterMode && OVRManager.IsInsightPassthroughInitialized()
                && scene.screen.localScale.x > loungeScale.x && Television.HasPicture, "theater enlarged the picture and kept passthrough and playback");
            scene.hide.onClick.Invoke();
            ProbeCheck(!scene.sourcePanel.activeSelf && !scene.hide.gameObject.activeInHierarchy, "hide dismissed browsing and playback controls");
            scene.reveal.onClick.Invoke();
            ProbeCheck(scene.sourcePanel.activeSelf, "show restored controls");
            scene.recenter.onClick.Invoke();
            scene.hide.onClick.Invoke();
            Television.SeekRelative(86400);
            yield return Until(() => !Television.HasPicture && controlsVisible, 7);
            ProbeCheck(!Television.HasPicture && controlsVisible, "film EOF restored the controls");
            if (!controlsVisible) scene.reveal.onClick.Invoke();
            scene.rows[0].onClick.Invoke();
            yield return Until(() => Television.HasPicture, 28);
            Television.SeekRelative(34);
            yield return new WaitForSecondsRealtime(1.5f);
            if (capture && ProbeCheck(Television.HasPicture, "offline picture available for opt-in stereo capture"))
                yield return CaptureStereoscopicEyes();

            Debug.Log($"[DVR probe] Complete ({probeFailures} failed). Offline picture active {Television.HasPicture}.");
        }

        private IEnumerator ProbeRayTransport(DiscObject live)
        {
            var seated = new Vector3(-0.18f, -0.30f, 0.14f);
            var standing = new Vector3(0.18f, -0.23f, 0.18f);
            var libraryReachable = Array.TrueForAll(scene.discButtons, tile =>
                TryControlRay(tile, seated, out _, out _) && TryControlRay(tile, standing, out _, out _));
            ProbeCheck(libraryReachable, "every library tile within both fixed controller reach envelopes");
            var transport = new[] { scene.recenter, scene.move, scene.deckHeight, scene.rewind, scene.pause, scene.forward, scene.panelEject };
            ProbeCheck(Array.TrueForAll(transport, tile =>
                TryControlRay(tile, seated, out _, out _) && TryControlRay(tile, standing, out _, out _)),
                "transport, eject and calibration within both fixed controller reach envelopes");
            var rail = scene.panelEject.GetComponentInParent<Canvas>();
            var corners = new Vector3[4];
            ((RectTransform)rail.transform).GetWorldCorners(corners);
            var top = scene.station.InverseTransformPoint(corners[1]);
            var minimumTheater = (0.50f - 0.12f - 0.585f * (4f / 1.04f) / 2f) / 3.02f;
            ProbeCheck(top.y / top.z < minimumTheater, "transport stays below the lowest allowed theater picture");

            var index = Array.IndexOf(scene.discs, live);
            yield return ClickControlRay(scene.discButtons[index], seated);
            ProbeCheck(loaded == live && !Television.HasPicture, "Meta ray click loaded a disc without physical reach");
            yield return ClickControlRay(scene.panelEject, standing);
            ProbeCheck(loaded == null && !Television.HasPicture, "Meta ray click ejected the disc");
            if (loaded != null) Eject();
        }

        private bool TryControlRay(Button button, Vector3 localOrigin, out PointableCanvas input, out Pose hitPose)
        {
            var target = scene.station.InverseTransformPoint(button.transform.position);
            var ray = new Ray(scene.station.TransformPoint(localOrigin), scene.station.TransformDirection(target - localOrigin));
            var canvas = button.GetComponentInParent<Canvas>();
            input = canvas.GetComponentInChildren<PointableCanvas>();
            hitPose = default;
            if (input == null || Mathf.Abs(Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg) > 26
                || Mathf.Abs(Mathf.Atan2(target.y, target.z) * Mathf.Rad2Deg) > 33)
                return false;

            var surface = input.GetComponent<RayInteractable>();
            if (surface == null || !surface.Raycast(ray, out var hit, 3f, false)) return false;
            hitPose = new Pose(hit.Point, Quaternion.LookRotation(ray.direction));
            return true;
        }

        private IEnumerator ClickControlRay(Button button, Vector3 localOrigin)
        {
            const int pointer = -121;
            if (!TryControlRay(button, localOrigin, out var input, out var hit)) yield break;
            input.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Hover, hit));
            yield return null;
            input.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Select, hit));
            yield return null;
            input.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Unselect, hit));
            yield return null;
            if (input.isActiveAndEnabled)
                input.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Unhover, hit));
            yield return null;
        }

        private IEnumerator CaptureStereoscopicEyes()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            yield return CaptureEye($"dvr-probe-left-{timestamp}.png", ScreenCapture.StereoScreenCaptureMode.LeftEye);
            yield return CaptureEye($"dvr-probe-right-{timestamp}.png", ScreenCapture.StereoScreenCaptureMode.RightEye);
        }

        private IEnumerator CaptureEye(string filename, ScreenCapture.StereoScreenCaptureMode eye)
        {
            var path = Path.Combine(Application.persistentDataPath, filename);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(filename, eye);
            yield return Until(() => File.Exists(path) && new FileInfo(path).Length > 8, 10);
            ProbeCheck(File.Exists(path) && new FileInfo(path).Length > 8, $"saved {eye} screenshot to {path}");
        }

        private IEnumerator CheckFrames(string label)
        {
            yield return Until(() => Television.HasPicture, 28);
            var start = Television.DecodedFrames;
            yield return new WaitForSecondsRealtime(1.25f);
            ProbeCheck(Television.HasPicture && Television.DecodedFrames > start,
                $"{label} advanced video ({start} to {Television.DecodedFrames} frames)");
        }

        private static IEnumerator Until(Func<bool> ready, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            yield return new WaitUntil(() => ready() || Time.realtimeSinceStartup >= deadline);
        }

        private IEnumerator ReleaseOnTray(DiscObject disc)
        {
            const int pointer = -120;
            var before = new Pose(disc.body.transform.position, disc.body.transform.rotation);
            var destination = new Pose(scene.slot.position, before.rotation);
            disc.grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Hover, before));
            disc.grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Select, before));
            disc.grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Move, destination));
            disc.grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Unselect, destination));
            disc.grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Unhover, destination));
            yield return null;
            yield return null;
        }

        private bool ProbeCheck(bool passed, string check)
        {
            if (passed) Debug.Log($"[DVR probe] PASS {check}");
            else
            {
                probeFailures++;
                Debug.LogError($"[DVR probe] FAIL {check}");
            }
            return passed;
        }
    }
}
#endif
