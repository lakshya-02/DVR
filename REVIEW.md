# DVR � Quest 3 checkpoint

## Current build

Implementation revision `f088259`, 19 November 2026. Open this repository directly in Unity 6000.3.19f1. [Build and test commands](AGENTS.md) � [Drive setup](DriveService/README.md) � [Security boundaries](SECURITY.md)

| Edition | Private Drive connection | APK size | SHA-256 |
| --- | --- | ---: | --- |
| Installed wired pilot | Trusted development headset and PC, USB | 164,157,951 | `19CA4FED1A7A9964CD4BF6D753E539347522E6F746EDAAC7247F23A070D70A59` |
| Default Android build | Requires a configured HTTPS companion for Drive | 130,395,997 | `5F837C6E26ACD57A610E14B470E132273FB77034041CF6B2C4C7D763FDB29AE4` |

## What changed after the wearer test

The wearer approved the DVD models, cinema picture and playback quality. Their table obscured transport controls, the eject label was hard to see, and the Personal disc had no account identity or folder view.

- [x] Placement follows the viewer's eye height and horizontal heading on entry and recenter. A two-row floating panel brings transport and a red Eject button below the picture. Either controller ray can select a disc or eject it. Either thumbstick can adjust deck height or screen position.
- [x] Atkinson Hyperlegible Regular and Bold, a translucent transport background and a distinct account badge with the official Google Drive mark. The layout was authored in Unity. Figma's connector was unavailable.
- [x] Personal opens to folder sources and displays the Google account name and email. Users can browse My Drive, shared items and shared drives, search for a folder by name, follow folder shortcuts, page and return to the parent.
- [x] The repository root contains Assets, Packages, ProjectSettings and the agent handoff. Native and companion scripts resolve the project from this layout.

## Verification

- The installed wired APK completed **46 Quest checks with zero failures**, including two simulated controller-ray origins, load and eject through the Meta event path, transport placement relative to the theater screen, both films, all six live sources, pause, seek, volume, and left/right-eye captures of the public demo. Guardian stayed enabled. Proximity sensing was restored.
- The existing pairing survived an app update and cold launch. The headset obtained identity from the real Google account, opened My Drive, searched across Drive, found the requested folder and opened its contents. No MP4 or WebM appeared directly inside the matched folder. The wearer chose to check private playback and seeking personally.
- Eight mocked companion tests passed. The dependency lockfile audit reported zero known npm advisories. The defensive review covered account and folder authorization, callback and redirect handling, logging, build flags, credential storage, rate limits and Git exclusions. Both editions cold-launched on the Quest. The USB pilot was reinstalled for the wearer. The regular APK manifest omits the debuggable flag. Its managed metadata omits the one-use diagnostic marker. The pilot requires that marker for intent-driven checks.
- Separate provider handoff tests in the preceding build reached the official Netflix and Prime Video players. Their subscription and entitlement flows belong to the providers.

## Resume here

1. **Wearer check.** In the usual seat, press X, use Discs and Eject from the floating panel, and confirm table clearance and text size. Use Deck height to adjust the physical model if needed.
2. **Private media.** Open Personal, check the account badge, open the desired subfolder and choose a movie. Use Show controls to pause or skip ten seconds. Keep the companion running and the trusted USB cable connected. Google testing grants can expire after seven days.
3. **Untethered Drive.** Configure a stable HTTPS service and Google redirect URI before distributing an edition with private Drive enabled. The wired pilot has no hosted service or subscription cost of its own.

## Device evidence · 19 November 2026

The earlier automated probe on Quest 3 reported **41 passes, zero failures**. It exercised the Meta pointer-event path. The samples below count advancing video frames on the projection surface. Network conditions and broadcaster scheduling can affect later runs.

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
