#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace DVR
{
    // Horizon OS provides HLS decoding through MediaPlayer and the Quest compositor
    // supplies the Android Surface. Unity never copies decoded video through the CPU.
    internal sealed class AndroidLiveVideo : IDisposable
    {
        private readonly GameObject layerObject;
        private readonly OVROverlay layer;
        private readonly AndroidJavaObject media;
        private readonly MediaEvents events = new();
        private bool started;
        private bool sourceSet;

        public AndroidLiveVideo(RectTransform screen, string url)
        {
            layerObject = new GameObject("Live channel surface");
            layerObject.SetActive(false);
            layerObject.transform.SetParent(screen, false);
            layerObject.transform.localScale = new Vector3(screen.rect.width, screen.rect.height, 1);
            layerObject.transform.localPosition = new Vector3(0, 0, -1);
            layer = layerObject.AddComponent<OVROverlay>();
            layer.isExternalSurface = true;
            layer.externalSurfaceWidth = 1280;
            layer.externalSurfaceHeight = 720;
            layer.overridePerLayerColorScaleAndOffset = true;
            layer.colorScale = new Vector4(1, 1, 1, 0);

            try
            {
                media = new AndroidJavaObject("android.media.MediaPlayer");
                media.Call("setOnPreparedListener", new PreparedListener(events));
                media.Call("setOnInfoListener", new InfoListener(events));
                media.Call("setOnErrorListener", new ErrorListener(events));
                media.Call("setOnCompletionListener", new CompletionListener(events));
                media.Call("setVolume", 0f, 0f);
                layer.externalSurfaceObjectCreated = () => Prepare(url);
                layerObject.SetActive(true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Prepare(string url)
        {
            try
            {
                using var surface = new AndroidJavaObject(layer.externalSurfaceObject);
                media.Call("setSurface", surface);
                if (sourceSet) return;
                sourceSet = true;
                using var context = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = context.GetStatic<AndroidJavaObject>("currentActivity");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url);
                media.Call("setDataSource", activity, uri);
                media.Call("prepareAsync");
            }
            catch (AndroidJavaException error)
            {
                events.Error = error.Message;
            }
        }

        public string Error => events.Error;
        public bool HasFrame => events.Frame;
        public long DecodedFrames
        {
            get
            {
                using var metrics = media.Call<AndroidJavaObject>("getMetrics");
                using var count = metrics.Call<AndroidJavaObject>("get", "android.media.mediaplayer.frames");
                return count?.Call<long>("longValue") ?? -1;
            }
        }

        public void Update()
        {
            if (!events.Prepared || started || Error != null) return;

            started = true;
            media.Call("start");
        }

        public void Reveal()
        {
            media.Call("setVolume", 1f, 1f);
            layer.colorScale = Vector4.one;
            Debug.Log($"[DVR] Android first frame {media.Call<int>("getVideoWidth")}x{media.Call<int>("getVideoHeight")}");

        }

        public void Dispose()
        {
            if (media != null)
            {
                try
                {
                    media.Call("release");
                }
                catch (AndroidJavaException error)
                {
                    Debug.LogWarning($"[DVR] Media release: {error.Message}");
                }
                finally
                {
                    media.Dispose();
                }
            }

            layer.externalSurfaceObjectCreated = null;
            layerObject.SetActive(false);
            UnityEngine.Object.Destroy(layerObject);
        }

        private sealed class MediaEvents
        {
            public volatile bool Prepared;
            public volatile bool Frame;
            public volatile string Error;
        }

        private sealed class PreparedListener : AndroidJavaProxy
        {
            private readonly MediaEvents events;
            public PreparedListener(MediaEvents events) : base("android.media.MediaPlayer$OnPreparedListener") => this.events = events;
            [Preserve] public void onPrepared(AndroidJavaObject player) => events.Prepared = true;
        }

        private sealed class InfoListener : AndroidJavaProxy
        {
            private readonly MediaEvents events;
            public InfoListener(MediaEvents events) : base("android.media.MediaPlayer$OnInfoListener") => this.events = events;

            [Preserve]
            public bool onInfo(AndroidJavaObject player, int code, int extra)
            {
                const int VideoRenderingStart = 3;
                if (code != VideoRenderingStart) return false;
                events.Frame = true;
                return true;
            }
        }

        private sealed class ErrorListener : AndroidJavaProxy
        {
            private readonly MediaEvents events;
            public ErrorListener(MediaEvents events) : base("android.media.MediaPlayer$OnErrorListener") => this.events = events;

            [Preserve]
            public bool onError(AndroidJavaObject player, int code, int extra)
            {
                events.Error = $"Android MediaPlayer error {code}, {extra}";
                return true;
            }
        }

        private sealed class CompletionListener : AndroidJavaProxy
        {
            private readonly MediaEvents events;
            public CompletionListener(MediaEvents events) : base("android.media.MediaPlayer$OnCompletionListener") => this.events = events;
            [Preserve] public void onCompletion(AndroidJavaObject player) => events.Error = "Live playback ended";
        }
    }
}
#endif
