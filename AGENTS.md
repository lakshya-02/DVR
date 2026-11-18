# DVR agent guide

Read [REVIEW.md](REVIEW.md) before resuming work. It holds the latest build evidence, pending acceptance checks, and next task.

The Unity project is in `Unity/`. Use the editor version in `Unity/ProjectSettings/ProjectVersion.txt` with Android SDK, NDK, and OpenJDK. The pinned Meta XR Core and Interaction SDKs own headset setup and input. `Unity/Assets/DVR/DVR.unity` is the runtime scene. `DVR > Rebuild prototype scene` regenerates the scene from `DvrBuild.Assemble` and replaces its authored contents.

## Build and sideload

Run from the repository root with this Unity project closed:

```powershell
$project = (Resolve-Path ./Unity).Path
$apk = Join-Path $project 'Builds/DVR.apk'
unity build $project --target Android --execute-method DvrBuild.Android --output $apk --no-tail
metavr device list
metavr app install --replace $apk
metavr app launch com.lakshya.dvr --cold-start --verify
```

Use `metavr log --tag Unity` for device errors. If Windows sees the headset but Meta VR CLI loses it after Unity exits, restart the locally installed ADB host daemon and rerun `metavr device list`.

Keep build outputs, caches, signing material, machine-local settings, and private media URLs out of Git. Update [REVIEW.md](REVIEW.md) after each checkpoint with the exact source revision, APK hash, observed test results, and next task.
