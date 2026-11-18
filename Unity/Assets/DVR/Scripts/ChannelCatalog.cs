using System;
using System.Collections.Generic;

namespace DVR
{
    internal static class ChannelCatalog
    {
        internal readonly struct Channel
        {
            public readonly string Name;
            public readonly string Url;

            public Channel(string name, string url)
            {
                Name = name;
                Url = url;
            }
        }

        // Public streams discovered through the iptv-org catalog; see REVIEW.md.
        internal static IReadOnlyList<Channel> Create()
        {
            var channels = new[]
            {
                new Channel("Al Jazeera English", "https://live-hls-apps-aje-fa.getaj.net/AJE/index.m3u8"),
                new Channel("Al Jazeera Mubasher", "https://live-hls-apps-ajm-fa.getaj.net/AJM/index.m3u8"),
            };
            var result = new List<Channel>(channels.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in channels)
            {
                if (!string.IsNullOrWhiteSpace(channel.Name)
                    && Uri.TryCreate(channel.Url, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
                    && uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                    && seen.Add(uri.AbsoluteUri))
                    result.Add(channel);
            }

            return result;
        }

        internal static int PickOther(int count, int current, Random random)
        {
            if (count <= 0) return -1;
            if (count == 1 || current < 0 || current >= count) return random.Next(count);

            var picked = random.Next(count - 1);
            return picked < current ? picked : picked + 1;
        }
    }
}
