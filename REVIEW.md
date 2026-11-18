# DVR mixed-reality checkpoint

## Tested build · 19 November 2026

Quest 3 has the Android ARM64 development build from implementation commit `78fb61e` on `feature/mr-iptv`. The APK SHA-256 is `BC41FF88EC1CD2CFD0D98FDC215A1078F37358CFD4E56391354139C0728C20FA`. See [AGENTS.md](AGENTS.md) for the build and installation commands.

- [x] Build the Unity OpenXR scene with Meta passthrough, a controller-grabbable disc, a deck, a TV and a channel panel.
- [x] Select two live IPTV feeds from [iptv-org](https://github.com/iptv-org/iptv). Desktop ffprobe identified H.264 video and AAC audio on both.
- [x] Install on Quest 3. Confirm head tracking, station placement and passthrough initialization.
- [x] Exercise insertion using injected Meta Interaction SDK pointer events. The TV stayed blank until channel selection. An unavailable stream left the TV blank and a subsequent selection started playback.
- [x] Play both feeds through Android MediaPlayer and a Meta compositor surface. Each produced a 1920×1080 first frame. In two-second samples the decoder advanced from 12 to 67 frames on Al Jazeera English and from 12 to 68 on Al Jazeera Mubasher.
- [x] Switch with Random and confirm the old picture clears. Eject during playback and during loading. Both returned the TV to idle.
- [x] End the probe, stop the app and verify Guardian and the proximity sensor are enabled.

## Resume here

- [ ] Launch DVR normally while wearing Quest 3. Reach for the disc with a controller, release it at the slot, aim at a channel on the side panel and confirm the picture and audible sound. Try Random, Recenter and Eject. Record any issue with reach, ray targeting or screen placement.
- [ ] After wearer acceptance, expand the two-channel catalog. The current build uses fixed feeds. Broadcaster availability can change.
- [ ] Later milestones cover movie discs with direct cloud media links, Google Drive integration and a fully virtual cinema.

To repeat the automated check on a development build, launch the Unity activity with the boolean intent extra `dvr_probe=true` and inspect Unity logs for `[DVR probe]`. The successful run reached `Complete` at 00:06:44 on 19 November. Android MediaPlayer handles HLS because Unity VideoPlayer failed to play the feeds on Quest.
