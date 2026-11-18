using System;
using System.Collections.Generic;

namespace DVR
{
    public static class ChannelCatalog
    {
        public enum DiscKind { OfflineFilm, OnlineFilm, Sports, IndianTv, News, Personal }

        public readonly struct Disc
        {
            public readonly string Id;
            public readonly string Name;
            public readonly DiscKind Kind;
            public readonly string Url;
            public readonly IReadOnlyList<Channel> Channels;

            internal Disc(string id, string name, DiscKind kind, string url = null, params Channel[] channels)
            {
                Id = id;
                Name = name;
                Kind = kind;
                Url = url;
                Channels = Array.AsReadOnly(channels);
            }
        }

        public readonly struct Channel
        {
            public readonly string Name;
            public readonly string Url;

            public Channel(string name, string url)
            {
                Name = name;
                Url = url;
            }
        }

        // Media provenance and viewing terms are recorded in MEDIA.md.
        public static IReadOnlyList<Disc> Discs { get; } = Array.AsReadOnly(new[]
        {
            new Disc("offline", "Big Buck Bunny", DiscKind.OfflineFilm),
            new Disc("feature", "Sita Sings the Blues", DiscKind.OnlineFilm,
                "https://archive.org/download/Sita_Sings_the_Blues/Sita_Sings_the_Blues_480p_2150kbps.mp4"),
            new Disc("sports", "Live Sports", DiscKind.Sports, null,
                new Channel("Red Bull TV · English", "https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master_3360.m3u8"),
                new Channel("Real Madrid TV · English", "https://rmtv.akamaized.net/hls/live/2043154/rmtv-en-web/bitrate_3.m3u8")),
            // NIC's master playlists use scheme-relative entries that Android MediaPlayer cannot load.
            new Disc("indian", "Indian TV", DiscKind.IndianTv, null,
                new Channel("Sansad TV 1 · Hindi / English", "https://playhls.media.nic.in/live/lstv/index.m3u8"),
                new Channel("Sansad TV 2 · Hindi / English", "https://playhls.media.nic.in/live/rstv/index.m3u8")),
            new Disc("news", "World News", DiscKind.News, null,
                new Channel("DW News · English", "https://dwamdstream102.akamaized.net/hls/live/2015525/dwstream102/master.m3u8"),
                new Channel("DD News · Hindi", "https://playhls.media.nic.in/live/ddnews/index.m3u8")),
            new Disc("personal", "Personal Media", DiscKind.Personal),
        });

        internal static int PickOther(int count, int current, Random random)
        {
            if (count <= 0) return -1;
            if (count == 1 || current < 0 || current >= count) return random.Next(count);

            var picked = random.Next(count - 1);
            return picked < current ? picked : picked + 1;
        }
    }
}
