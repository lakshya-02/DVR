# Media credits and sources

## Film discs

**Big Buck Bunny** (2008) · © copyright 2008, Blender Foundation / www.bigbuckbunny.org. Creative Commons Attribution 3.0 Unported. `Assets/DVR/Media/OfflineDemo.mp4` is a 1280 × 720 H.264/AAC transcode of the studio’s complete 720p release. It keeps the film and its original credits and converts the audio to stereo. [Film and attribution](https://peach.blender.org/about/) · [Official source ZIP](https://download.blender.org/peach/bigbuckbunny_movies/big_buck_bunny_720p_h264.mov.zip) · [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)

**Sita Sings the Blues** (2008) · Nina Paley, writer and director. English-language animated feature, 81 minutes 31 seconds. The disc streams the complete, unchanged 480p H.264/AAC MP4 that Paley links from her official viewing page. Her CC0 dedication applies to the rights she holds. The license page identifies more restricted underlying music and permits hearing it in the context of the original film. The original audio stays inside the film, which is streamed from the Archive release. [Official viewing page](https://www.sitasingstheblues.com/watch.html) · [Film and soundtrack terms](https://www.sitasingstheblues.com/license.html) · [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) · [Archive release](https://archive.org/details/Sita_Sings_the_Blues)

## Live discs

DVR requests live video directly from publisher or distributor servers for private viewing. The catalog supplies links and channel names. The publishers control scheduling, access and content rights. A playable link does not grant rights to rehost or redistribute a broadcast.

| Disc | Channel | Publisher and endpoint evidence |
| --- | --- | --- |
| Live Sports | Red Bull TV · English | Red Bull's free streaming service. The public Red Bull Media Network Akamai master identifies the `BoRB-AT` service and English captions. The decoded sample displayed Red Bull Signature Series action-sports programming. [Red Bull TV](https://www.redbull.tv/en) · [Publisher and licensing contact](https://www.redbull.com/int-en/support-hub/media-and-platforms) · [HLS master](https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master.m3u8) · [720p media playlist](https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master_3360.m3u8) |
| Live Sports | Real Madrid TV · English | The club publishes its own Spanish and international live signals. The direct RMTV Akamai English web feed decoded club football programming. The channel carries club coverage, replays and highlights. [Official live channel and schedule](https://www.realmadrid.com/en-US/real-madrid-tv) · [Club's description of both live signals](https://www.realmadrid.com/en-US/news/club/latest-news/real-madrid-launches-rm-play-the-platform-to-view-all-of-the-club-s-video-content) · [HLS master](https://rmtv.akamaized.net/hls/live/2043154/rmtv-en-web/master.m3u8) · [720p media playlist](https://rmtv.akamaized.net/hls/live/2043154/rmtv-en-web/bitrate_3.m3u8) |
| Indian TV | Sansad TV 1 · Hindi / English | Parliament of India publishes both channels. The National Informatics Centre (NIC) public player embeds the `lstv` HLS source for channel 1. [Sansad TV live](https://sansadtv.nic.in/live-tv) · [Languages](https://sansadtv.nic.in/about-us) · [NIC channel 1 player](https://webcast.gov.in/lstvlive/) · [HLS master](https://playhls.media.nic.in/hls/live/lstv/lstv.m3u8) · [Media playlist](https://playhls.media.nic.in/live/lstv/index.m3u8) |
| Indian TV | Sansad TV 2 · Hindi / English | The NIC public player embeds `rstv/rstv.m3u8`. Samples showed distinct channel 1 and 2 station bugs and tickers during shared programming. [Sansad TV live](https://sansadtv.nic.in/live-tv) · [Languages](https://sansadtv.nic.in/about-us) · [NIC channel 2 player](https://webcast.gov.in/rstvlive/) · [HLS master](https://playhls.media.nic.in/hls/live/rstv/rstv.m3u8) · [Media playlist](https://playhls.media.nic.in/live/rstv/index.m3u8) |
| World News | DW News · English | Deutsche Welle's English live page includes the exact DW Akamai HLS master. [Official live channel](https://www.dw.com/en/live-tv/channel-english) · [HLS](https://dwamdstream102.akamaized.net/hls/live/2015525/dwstream102/master.m3u8) |
| World News | DD News · Hindi | Prasar Bharati's public news channel. The NIC public webcast homepage embeds the DD News HLS master. [Prasar Bharati channel list](https://prasarbharati.gov.in/en/dd-channels/) · [NIC public live player](https://webcast.gov.in/) · [HLS master](https://playhls.media.nic.in/hls/live/ddnews/ddnews.m3u8) · [Media playlist](https://playhls.media.nic.in/live/ddnews/index.m3u8) |

The NIC masters link to their media playlists with scheme-relative URLs. DVR uses those media playlists directly over HTTPS after Android MediaPlayer failed to load the variants selected from the master. The sports discs similarly use publisher-listed 720p media playlists so playback starts at 720p.

## Desktop verification · 19 November 2026

- Big Buck Bunny is 596.462 seconds, H.264 Main at 1280 × 720 with two-channel AAC LC. FFmpeg decoded both tracks in full. The bundled MP4 is 74,809,624 bytes with SHA-256 `8C9371A7A64B765B681517978FAF33E33E31942FBDE7A088D2551B564BBC741D`.
- The exact Sita Sings the Blues stream returned 848 × 480 H.264 Constrained Baseline video and two-channel AAC LC. FFmpeg decoded audio and video at 0:00, 43:20 and 81:20.
- FFmpeg decoded audio and video from six unauthenticated HTTPS HLS URLs above. Samples showed the named broadcaster or a program listed by that broadcaster. Live programming and availability can change.

## Provider marks

Google Drive’s product mark is displayed in the Personal account badge to identify the connected Google service. Google supplied the original PNG and DVR embeds it unchanged. [Official Google Drive PNG](https://www.gstatic.com/images/branding/product/2x/drive_2020q4_48dp.png) · [Google brand guidance](https://about.google/brand-resource-center/).

## Typography

Atkinson Hyperlegible Regular and Bold by the Braille Institute of America are bundled under the SIL Open Font License 1.1. [Font source and license](https://github.com/google/fonts/tree/main/ofl/atkinsonhyperlegible).
