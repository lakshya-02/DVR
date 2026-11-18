# DVR projection room · Quest 3 checkpoint

## Build

The sideloaded development APK was built from implementation revision `8158da1` on `feature/projection-room`. Its size is 159,139,445 bytes and its SHA-256 is `95FA750D19AA6997DA0FD3A4E6790291715CAB36BDE137892BC6EFEB4A79315A`. Revision `f6c3658` improves restarting the Windows companion without rebuilding the APK. See [AGENTS.md](AGENTS.md) for rebuild and diagnostic commands and [MEDIA.md](MEDIA.md) for the film licenses and broadcaster sources.

## Implemented

- [x] Metal deck, six illustrated physical discs, individual cradles, one-disc loading, haptic feedback and ejection. Movie discs autoplay. Live collections open a channel picker.
- [x] 3 m Lounge and 4 m Theater screen widths in passthrough, smoked controls, screen positioning, recenter, adjustable volume, and movie pause/seek. The Unity splash is disabled.
- [x] Bundled animation, an English-language feature, and two choices each on Sports, Indian TV and News.
- [x] Live Android video is copied on the GPU into a Unity texture. The native bridge counts new images actually copied onto the picture surface. The previous build counted frames decoded into a separate compositor layer.
- [x] Private Google Drive pairing, filename search, paging, authenticated range playback and disconnect. The Windows USB pilot is configured on the development PC. The testing account granted Google read-only consent and the companion confirmed pairing. Public HTTPS deployment instructions are in [DriveService](DriveService/README.md).
- [x] Both subscription shortcuts launched their respective official HTTPS destinations in Quest Browser. The providers own login, entitlements and playback.

## Device evidence · 19 November 2026

The final automated probe on Quest 3 reported **41 passes, zero failures**. It exercised the Meta pointer-event path. The samples below count advancing video frames on the projection surface. Network conditions and broadcaster scheduling can affect later runs.

| Disc or channel | Frames over approximately 1.25 seconds |
| --- | --- |
| Big Buck Bunny, bundled 720p | 0 → 30 |
| Sita Sings the Blues | 2 → 31 |
| Red Bull TV | 2 → 39 |
| Real Madrid TV | 1 → 24 |
| Sansad TV 1 | 1 → 22 |
| Sansad TV 2 | 1 → 26 |
| DW News | 2 → 65 |
| DD News | 1 → 36 |

Separate left-eye and right-eye screenshots contain the film projected in the scene. The probe verified passthrough, insertion with an occupied tray, an unavailable stream, switching channels, ejection during loading and playback, end-of-film recovery, pause, seek, volume, recentering and switching screen sizes. Guardian and proximity sensing were restored after bench testing.

Six mocked Drive integration tests passed. They cover Google OAuth, paging and search, range requests, access revocation, and the development-only USB loopback restriction. The connected headset separately initiated a real pairing, and its PC companion completed the Google consent callback. Credentials, pairing codes, private movie titles and room imagery are excluded from the source handoff.

## Resume here

1. Record the wearer's assessment of picture and sound, seated reach, the side panel and screen placement.
2. With the USB companion running, insert Personal and verify a private movie plays and seeks from the real Drive account. Keep release notes free of private titles. A seven-day Google testing grant or a changed USB connection may require reconnection.
3. Provider account playback is a wearer check in each official player. For untethered private Drive playback, configure a stable HTTPS endpoint and register its callback before building that edition.
