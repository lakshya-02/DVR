# DVR agent guide

Read [REVIEW.md](REVIEW.md) first for the last tested revision, APK fingerprint and outstanding wearer checks. [MEDIA.md](MEDIA.md) records source URLs, playback terms and the bundled film attribution.

## Build and run

Open the repository root with the editor pinned in `ProjectSettings/ProjectVersion.txt`, including the Android SDK, NDK and OpenJDK. The Meta Interaction SDK provides the rig, controller grabs and ray canvases. Meta XR Core provides passthrough. The scene generator owns the screen, deck, disc artwork, cradles and controls. Rebuild it after changing geometry, source rows or serialized bindings. Rebuilding replaces authored scene contents.

From the repository root with the Editor closed:

```powershell
$project = (Resolve-Path .).Path
$apk = Join-Path $project 'Builds/DVR-Cinema.apk'
unity build $project --target Android --execute-method DvrBuild.RebuildAndroid --output $apk --no-tail
metavr device list
metavr app install --replace $apk
metavr app launch com.lakshya.dvr --cold-start --verify
```

If the Unity process stops the ADB server, run `adb start-server` and check `metavr device list` again. The APK targets Quest 3, ARM64 and OpenGLES3. Android MediaPlayer feeds a SurfaceTexture. The native render-thread code copies new frames into the same Unity picture drawn for both eyes. Rebuild the bundled native library after edits to `Native/VideoTexture.cpp` using `Native/Build-VideoTexture.ps1 -Ndk <Unity-Android-NDK-path>`.

## Controls and testing

Point either controller at a tile in Discs or insert a physical disc. Choose Personal for Drive and the official streaming services. B, Y or Show controls restores transport during playback. X recenters the station at the current eye height and heading. Select Deck height or Screen position and use either thumbstick to adjust. Eject on the floating controls or deck returns the disc to its cradle.

For a trusted Quest attached by USB, substitute `DvrBuild.RebuildWiredPilot` for `DvrBuild.RebuildAndroid` in the build command. This enables the loopback companion and diagnostic checks. The default build accepts an HTTPS Drive origin and omits developer diagnostics. Before each diagnostic cold launch, grant a single run with `adb shell run-as com.lakshya.dvr touch files/dvr-diagnostics-once`. A recognized diagnostic intent consumes the marker. `dvr_probe=true` runs the catalog, transport and interaction checks. Add `dvr_capture=true` for left and right eye PNGs in the app's private external files directory. `dvr_preview=offline|news|sports|personal` opens one disc. `dvr_open=netflix|prime` tests an external handoff. The string extra `dvr_drive_check` takes a folder name and checks the connected account, navigation and private playback. It logs fixed status messages. Do not capture private Drive movies or upload room imagery for verification. Use the licensed offline film for screenshots and record wearer observations separately from automated results.

## Personal media

Follow [Drive companion setup](DriveService/README.md). `npm test` in `DriveService/` checks the Google integration with mocked endpoints. The free Windows pilot connects the development build to a PC companion over USB. A standalone deployment needs a separately configured HTTPS service. The SQLite database, consent tokens, local service settings, signing material and credentials belong outside Git.

At each checkpoint update [REVIEW.md](REVIEW.md) with the implementation revision, artifact hash, observed tests and exact next action.
