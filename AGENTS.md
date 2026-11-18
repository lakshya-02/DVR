# DVR agent guide

Read [REVIEW.md](REVIEW.md) first for the last tested revision, APK fingerprint and outstanding wearer checks. [MEDIA.md](MEDIA.md) records source URLs, playback terms and the bundled film attribution.

## Build and run

Open the project in `Unity/` with the editor pinned in `ProjectSettings/ProjectVersion.txt`, including the Android SDK, NDK and OpenJDK. The Meta Interaction SDK provides the rig, controller grabs and ray canvases. Meta XR Core provides passthrough. The scene generator owns the screen, deck, disc artwork, cradles and controls. Rebuild it after changing geometry, source rows or serialized bindings. Rebuilding replaces authored scene contents.

From the repository root with the Editor closed:

```powershell
$project = (Resolve-Path ./Unity).Path
$apk = Join-Path $project 'Builds/DVR-Cinema.apk'
unity build $project --target Android --execute-method DvrBuild.RebuildAndroid --output $apk --no-tail
metavr device list
metavr app install --replace $apk
metavr app launch com.lakshya.dvr --cold-start --verify
```

If the Unity process stops the ADB server, run `adb start-server` and check `metavr device list` again. The development APK targets Quest 3, ARM64 and OpenGLES3. Android MediaPlayer feeds a SurfaceTexture. The native render-thread code copies new frames into the same Unity picture drawn for both eyes. Rebuild the bundled native library after edits to `Native/VideoTexture.cpp` using `Native/Build-VideoTexture.ps1 -Ndk <Unity-Android-NDK-path>`.

## Controls and testing

Insert a film to play it. Insert a live collection to choose a channel, or Personal to browse Drive and open provider players. B or Y restores the controls during playback. X recenters the room. The Move screen control enables the left thumbstick. Eject returns a disc to its cradle.

Development diagnostic intent extras are consumed on a cold launch. `dvr_probe=true` runs the catalog, transport and interaction checks. Add `dvr_capture=true` for left and right eye PNGs in the app's private external files directory. `dvr_preview=offline|news|sports|personal` opens one disc. `dvr_open=netflix|prime` tests an external handoff. Do not capture private Drive movies or upload room imagery for verification. Use the licensed offline film for screenshots and record wearer observations separately from automated results.

## Personal media

Follow [Drive companion setup](DriveService/README.md). `npm test` in `DriveService/` checks the Google integration with mocked endpoints. The free Windows pilot connects the development build to a PC companion over USB. A standalone deployment needs a separately configured HTTPS service. The SQLite database, consent tokens, local service settings, signing material and credentials belong outside Git.

At each checkpoint update [REVIEW.md](REVIEW.md) with the implementation revision, artifact hash, observed tests and exact next action.
