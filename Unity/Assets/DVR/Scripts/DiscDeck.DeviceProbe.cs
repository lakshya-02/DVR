#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace DVR
{
    public sealed partial class DiscDeck
    {
        private void RunRequestedDeviceProbe()
        {
            using var launcher = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = launcher.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            if (intent != null && intent.Call<bool>("getBooleanExtra", "dvr_probe", false))
                StartCoroutine(DeviceProbe());
        }

        private IEnumerator DeviceProbe()
        {
            var until = Time.realtimeSinceStartup + 45;
            yield return new WaitUntil(() => placed || Time.realtimeSinceStartup >= until);
            if (!ProbeCheck(placed, "head tracking and station placement")) yield break;

            until = Time.realtimeSinceStartup + 3;
            yield return new WaitUntil(() => OVRManager.IsInsightPassthroughInitialized() || Time.realtimeSinceStartup >= until);
            ProbeCheck(OVRManager.IsInsightPassthroughInitialized(), "Meta passthrough initialized");

            GrabAndSetOnTray();
            yield return null;
            yield return null;
            if (!ProbeCheck(seated && channelPanel.activeSelf && !television.HasPicture, "disc inserted with blank TV")) yield break;

            var error = false;
            television.Play("https://dvr-probe.invalid/unavailable.m3u8", message => error = message != "Connecting…");
            yield return new WaitUntil(() => error);
            ProbeCheck(!television.HasPicture, "invalid stream kept the TV blank");

            rows[0].onClick.Invoke();
            until = Time.realtimeSinceStartup + 22;
            yield return new WaitUntil(() => television.HasPicture || Time.realtimeSinceStartup >= until);
            var firstFrame = television.DecodedFrames;
            yield return new WaitForSecondsRealtime(2);
            ProbeCheck(television.HasPicture && television.DecodedFrames > firstFrame, $"live channel 1 video advanced ({firstFrame} to {television.DecodedFrames} frames)");
            var first = selected;

            RandomChannel();
            ProbeCheck(selected != first && !television.HasPicture, "random switch blanked the old channel");
            until = Time.realtimeSinceStartup + 22;
            yield return new WaitUntil(() => television.HasPicture || Time.realtimeSinceStartup >= until);
            var secondFrame = television.DecodedFrames;
            yield return new WaitForSecondsRealtime(2);
            ProbeCheck(television.HasPicture && television.DecodedFrames > secondFrame, $"live channel 2 video advanced ({secondFrame} to {television.DecodedFrames} frames)");

            Eject();
            ProbeCheck(!seated && !television.HasPicture, "eject cleared active playback");
            GrabAndSetOnTray();
            yield return null;
            yield return null;

            rows[0].onClick.Invoke();
            Eject();
            yield return new WaitForSecondsRealtime(2);
            ProbeCheck(!seated && !channelPanel.activeSelf && !television.HasPicture &&
                Vector3.Distance(disc.position, stand.position) < 0.01f, "eject during loading restored the disc and blank TV");
            Debug.Log("[DVR probe] Complete");
        }

        private void GrabAndSetOnTray()
        {
            const int pointer = -120;
            var before = new Pose(disc.position, disc.rotation);
            var onTray = new Pose(slot.position, disc.rotation);
            grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Hover, before));
            grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Select, before));
            grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Move, onTray));
            grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Unselect, onTray));
            grabbable.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Unhover, onTray));
        }

        private static bool ProbeCheck(bool passed, string check)
        {
            if (passed) Debug.Log($"[DVR probe] PASS {check}");
            else Debug.LogError($"[DVR probe] FAIL {check}");
            return passed;
        }
    }
}
#endif
