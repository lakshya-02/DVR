#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace DVR
{
    internal sealed class AndroidLiveVideo : IDisposable
    {
        private const string Plugin = "dvrvideo";
        [DllImport(Plugin)] private static extern IntPtr DvrCreateSurface(IntPtr vm, IntPtr surfaceTexture, IntPtr target, int width, int height);
        [DllImport(Plugin)] private static extern int DvrPresentedFrames(IntPtr session);
        [DllImport(Plugin)] private static extern IntPtr DvrRenderEvent();

        private readonly MediaEvents events = new();
        private readonly AndroidJavaObject media;
        private readonly AndroidJavaObject producer;
        private readonly AndroidJavaObject surface;
        private IntPtr session;
        private IntPtr renderEvent;
        private readonly UnityEngine.Rendering.CommandBuffer commands = new() { name = "Present DVR video" };
        private readonly bool isMovie;
        private bool started;
        private bool paused;
        public Texture2D Texture { get; private set; }
        public float Aspect { get; private set; } = 16f / 9f;
        public bool Paused => paused;
        public long DecodedFrames => session == IntPtr.Zero ? 0 : DvrPresentedFrames(session);
        public bool HasFrame => DecodedFrames > 0;
        public bool Ended => events.Ended;
        public string Error => events.Error ?? (DecodedFrames < 0 ? "Projection texture unavailable" : null);

        public AndroidLiveVideo(string url, string bearer, bool movie)
        {
            isMovie = movie;
            try
            {
                producer = new AndroidJavaObject("android.graphics.SurfaceTexture", false);
                surface = new AndroidJavaObject("android.view.Surface", producer);
                media = new AndroidJavaObject("android.media.MediaPlayer");
                media.Call("setOnPreparedListener", new PreparedListener(events));
                media.Call("setOnErrorListener", new ErrorListener(events));
                media.Call("setOnCompletionListener", new CompletionListener(events));
                media.Call("setSurface", surface);
                media.Call("setVolume", 0f, 0f);
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var uris = new AndroidJavaClass("android.net.Uri");
                using var uri = uris.CallStatic<AndroidJavaObject>("parse", url);
                if (string.IsNullOrEmpty(bearer)) media.Call("setDataSource", activity, uri);
                else
                {
                    using var headers = new AndroidJavaObject("java.util.HashMap");
                    using var auth = headers.Call<AndroidJavaObject>("put", "Authorization", "Bearer " + bearer);
                    using var redirectPolicy = headers.Call<AndroidJavaObject>("put", "android-allow-cross-domain-redirect", "0");
                    media.Call("setDataSource", activity, uri, headers);
                }
                media.Call("prepareAsync");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Update()
        {
            if (!events.Prepared || events.Error != null) return;
            if (!started)
            {
                media.Call("start");
                started = true;
            }
            if (session == IntPtr.Zero)
            {
                int width = media.Call<int>("getVideoWidth");
                int height = media.Call<int>("getVideoHeight");
                if (width <= 0 || height <= 0) return;
                Aspect = (float)width / height;
                // An adaptive live feed can start small, then switch to HD on the same decoder surface.
                float scale = Mathf.Min(1920f / width, 1080f / height);
                if (isMovie) scale = Mathf.Min(1f, scale);
                Texture = new Texture2D(Mathf.Max(1, Mathf.RoundToInt(width * scale)), Mathf.Max(1, Mathf.RoundToInt(height * scale)),
                    TextureFormat.RGBA32, false, true) { name = "Cinema video", wrapMode = TextureWrapMode.Clamp };
                Texture.Apply(false, true);
                session = DvrCreateSurface(AndroidJNI.GetJavaVM(), producer.GetRawObject(), Texture.GetNativeTexturePtr(), Texture.width, Texture.height);
                if (session == IntPtr.Zero) throw new InvalidOperationException("Could not attach the decoder surface.");
                renderEvent = DvrRenderEvent();
            }
            Submit(1);
        }

        private void Submit(int action)
        {
            commands.Clear();
            commands.IssuePluginEventAndData(renderEvent, action, session);
            Graphics.ExecuteCommandBuffer(commands);
        }

        public void SetVolume(float volume) => media.Call("setVolume", volume, volume);

        public void TogglePause()
        {
            if (!HasFrame) return;
            media.Call(paused ? "start" : "pause");
            paused = !paused;
        }

        public void Seek(float seconds)
        {
            if (!HasFrame) return;
            int duration = media.Call<int>("getDuration");
            if (duration > 0) media.Call("seekTo", Mathf.Clamp(media.Call<int>("getCurrentPosition") + Mathf.RoundToInt(seconds * 1000), 0, duration - 1));
        }

        public void Dispose()
        {
            if (media != null)
            {
                try { media.Call("release"); }
                finally { media.Dispose(); }
            }
            if (session != IntPtr.Zero)
            {
                Submit(2);
                session = IntPtr.Zero;
            }
            else producer?.Call("release");
            commands.Dispose();
            surface?.Call("release");
            surface?.Dispose();
            producer?.Dispose();
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
        }

        private sealed class MediaEvents
        {
            public volatile bool Prepared;
            public volatile bool Ended;
            public volatile string Error;
        }

        private sealed class PreparedListener : AndroidJavaProxy
        {
            private readonly MediaEvents events;
            public PreparedListener(MediaEvents events) : base("android.media.MediaPlayer$OnPreparedListener") => this.events = events;
            [Preserve] public void onPrepared(AndroidJavaObject player) => events.Prepared = true;
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
            [Preserve] public void onCompletion(AndroidJavaObject player) => events.Ended = true;
        }
    }
}
#endif

