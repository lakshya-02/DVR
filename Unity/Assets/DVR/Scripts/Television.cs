using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace DVR
{
    public sealed class Television : MonoBehaviour
    {
        [SerializeField] private RawImage picture;
        [SerializeField] private VideoClip demoClip;

        private VideoPlayer player;
        private AudioSource audioOutput;
        private RenderTexture videoTexture;
        private Action<string> report;
        private float deadline;
        private bool waiting;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidLiveVideo live;
        private bool livePicture;
#endif

        public bool HasDemo => demoClip != null;
        internal bool HasPicture
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (live != null) return livePicture;
#endif
                return picture.enabled;
            }
        }
        internal long DecodedFrames
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (live != null && livePicture) return live.DecodedFrames;
#endif
                return player != null ? player.frame : 0;
            }
        }

        private void Awake()
        {
            picture.enabled = false;
            videoTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            picture.texture = videoTexture;
        }

        public void Play(string url, Action<string> onStatus)
        {
            StartPlayback(url, null, onStatus);
        }

        public void PlayDemo(Action<string> onStatus)
        {
            if (demoClip != null) StartPlayback(null, demoClip, onStatus);
        }

        private void StartPlayback(string url, VideoClip clip, Action<string> onStatus)
        {
            Stop();
            report = onStatus;
            deadline = Time.realtimeSinceStartup + 20f;
            waiting = true;
            report?.Invoke("Connecting…");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (clip == null)
            {
                try
                {
                    live = new AndroidLiveVideo(picture.rectTransform, url);
                }
                catch (AndroidJavaException error)
                {
                    Fail("Channel unavailable. Try another.", error.Message);
                }

                return;
            }
#endif

            var session = new GameObject("Playback session");
            session.transform.SetParent(transform, false);
            audioOutput = session.AddComponent<AudioSource>();
            audioOutput.playOnAwake = false;
            audioOutput.mute = true;

            player = session.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = clip == null ? VideoSource.Url : VideoSource.VideoClip;
            if (clip == null) player.url = url;
            else player.clip = clip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = videoTexture;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.SetTargetAudioSource(0, audioOutput);
            player.waitForFirstFrame = true;
            player.sendFrameReadyEvents = true;
            player.prepareCompleted += OnPrepared;
            player.frameReady += OnFrame;
            player.errorReceived += OnError;
            player.loopPointReached += OnEnded;
            player.Prepare();
        }

        private void OnPrepared(VideoPlayer source)
        {
            if (source == player) source.Play();
        }

        private void OnFrame(VideoPlayer source, long frame)
        {
            if (source != player || !waiting) return;

            waiting = false;
            source.sendFrameReadyEvents = false;
            picture.enabled = true;
            audioOutput.mute = false;
            Debug.Log($"[DVR] First video frame {frame}, {source.width}x{source.height}, audio tracks {source.audioTrackCount}");
            report?.Invoke("Playing");
        }

        private void OnError(VideoPlayer source, string error)
        {
            if (source == player) Fail("Channel unavailable. Try another.", error);
        }

        private void OnEnded(VideoPlayer source)
        {
            if (source == player) Fail("Playback ended. Choose a channel.", "Playback ended");
        }

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (live != null) UpdateLive();
#endif
            if (waiting && Time.realtimeSinceStartup >= deadline)
                Fail("Connection timed out. Try another.", "First-frame deadline expired");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void UpdateLive()
        {
            if (live.Error != null)
            {
                Fail("Channel unavailable. Try another.", live.Error);
                return;
            }

            try
            {
                live.Update();
                if (!waiting || !live.HasFrame) return;

                live.Reveal();
                livePicture = true;
                waiting = false;
                report?.Invoke("Playing");
            }
            catch (AndroidJavaException error)
            {
                Fail("Channel unavailable. Try another.", error.Message);
            }
        }
#endif

        private void Fail(string message, string detail)
        {
            var callback = report;
            Stop();
            Debug.LogWarning($"[DVR] {detail}");
            callback?.Invoke(message);
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var previousLive = live;
            live = null;
            livePicture = false;
            previousLive?.Dispose();
#endif
            var previous = player;
            player = null;
            waiting = false;
            report = null;
            if (picture != null) picture.enabled = false;
            if (previous == null) return;

            audioOutput.mute = true;
            previous.prepareCompleted -= OnPrepared;
            previous.frameReady -= OnFrame;
            previous.errorReceived -= OnError;
            previous.loopPointReached -= OnEnded;
            previous.Stop();
            previous.targetTexture = null;
            Destroy(previous.gameObject);
            audioOutput = null;
        }

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            if (videoTexture == null) return;
            videoTexture.Release();
            Destroy(videoTexture);
        }
    }
}
