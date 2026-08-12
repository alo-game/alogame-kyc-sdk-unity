# Alogame KYC SDK — Unity package

UPM package wrapping the native Android/iOS `alogame-kyc-sdk`. Native binaries
are vendored inside the package (`Runtime/Plugins/`), so there is no JitPack,
CocoaPods or SPM resolution at a game's build time.

> **Status: builds end to end on both platforms; not yet run on a device.**
> A real APK was produced from a real Unity project and contains both the native
> SDK and this package's Java shim. A real Xcode project was exported, compiled
> with `** BUILD SUCCEEDED **`, and the resulting `.app` has the framework
> embedded as the arm64 device slice with a working `@rpath` and all eight
> `@_cdecl` entry points exported. See [Verification](#verification) for exactly
> what was checked and how. What remains is running the flow on a physical
> device — installing, pressing the button, completing OTP.

## Install

**Prerequisites:** Unity 2021.3+ with **Android Build Support** (incl. SDK/NDK
and OpenJDK) and/or **iOS Build Support** installed through Unity Hub. Without
the platform module the package's native code paths compile out entirely and a
green console proves nothing.

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/alo-game/alogame-kyc-sdk-unity.git#0.1.0
```

Pin a tag once one exists (`https://github.com/alo-game/alogame-kyc-sdk-unity.git#0.1.0`). While iterating locally,
**Add package from disk** pointed at this `unity/` folder works too and makes the
package mutable.

Nothing else to configure — no Gradle edit, no `Podfile`, no manifest entry, no
Xcode setting. Player Settings only need what your game already sets, plus
Android `minSdkVersion` ≥ 24.

### Verify the install in 60 seconds

1. Import the sample (below), drop `AlogameKycSample` on a GameObject in a saved
   scene.
2. Build for Android. On success, confirm the SDK really landed:
   ```sh
   unzip -p your.apk classes.dex | strings | grep -c KycActivity   # expect > 0
   ```
3. For iOS, build the Xcode project and check the console for
   `[AlogameKycSdk] embedded the device slice (…) and configured rpath.` — if
   that line is missing, the postprocess step did not run and the app will crash
   at launch.

## Use

```csharp
using Alogame.KycSdk;

// once, at startup
AlogameKycSdk.Init(AlogameKycEnv.Dev);

// whenever uid + session token arrive from your game server (always together)
AlogameKycSdk.SetGameRole(uid, sessionToken);

// when the game needs the player verified
AlogameKycSdk.Show(new MyListener());

class MyListener : AlogameKycListener
{
    public override void OnResult(AlogameKycResult result)
    {
        switch (result)
        {
            case AlogameKycResult.Success _:   /* verified */ break;
            case AlogameKycResult.Cancelled _: /* player backed out */ break;
            case AlogameKycResult.Failed f:    Debug.LogWarning(f.Reason); break;
        }
    }

    // optional — only if your server can mint tokens on demand
    public override void OnSessionTokenNeeded(string uid)
    {
        StartCoroutine(FetchToken(uid, AlogameKycSdk.ProvideSessionToken));
        // or AlogameKycSdk.AbortPendingToken() if you can't
    }
}
```

`OnResult` fires **exactly once** per `Show()`, on the Unity main thread, after
the native screen has finished closing. `OnSessionTokenNeeded` may fire zero or
more times before it.

`IsVerified()` is a RAM cache for UI state — **never** an authorization source.
Your game server must call `POST /xmdt/user-status` before trusting it.

In the Editor and on desktop every call is a logged no-op, and `Show()` answers
with `Failed(UnknownError)` so your result-handling branch still runs in Play
Mode.

## What the package does to your build

Nothing you have to configure. For the record:

**Android** — Unity merges `Runtime/Plugins/Android/alogame-kyc-sdk.aar` into the
Gradle project it generates. The two `.java` files there compile alongside it.
No manifest entry, no permission, no Gradle edit. The SDK takes no build-time
secrets: `env`, `uid` and `sessionToken` are all runtime arguments.

**iOS** — `Editor/AlogameKycIosPostprocessBuild.cs` runs after Unity generates
the Xcode project and sets three things Unity does not: `SWIFT_VERSION` (the
bridge is Swift, and no Unity template target uses Swift), embedding for
`AlogameKycKit.framework`, and `LD_RUNPATH_SEARCH_PATHS` — without which the app
builds cleanly and then crashes at launch with `Library not loaded:
@rpath/AlogameKycKit.framework/AlogameKycKit`.

## Sample

Package Manager → this package → **Samples** → *Reference Scene* → Import. Drop
`AlogameKycSample` on any GameObject, set `sessionToken` in the Inspector, build
to a device. Get a test token with:

```sh
curl -X POST https://api-xmdt.dev.alogame.vn/sample/session \
     -H 'Content-Type: application/json' -d '{"uid":"your-test-uid"}'
```

## Architecture

C# never touches a Kotlin or Swift type across the language boundary. Each
platform has one flat shim:

| | Shim | Mechanism |
|---|---|---|
| Android | `AlogameKycUnityBridge.java` | `AndroidJavaProxy` implements `AlogameKycFlatListener` (all-`String` interface) |
| iOS | `AlogameKycUnityBridge.swift` | `@_cdecl` C symbols + `[DllImport("__Internal")]`, callback via function pointer |

Both shims flatten `AlogameKycResult` (a Kotlin sealed class / Swift enum with
an associated value) into `(status, reason, message)` before it crosses.
`AlogameKycResult.FromFlat` rebuilds it in one place shared by both platforms, so
they cannot drift.

Callbacks are marshalled onto the Unity main thread by
`AlogameKycMainThreadDispatcher` — required on Android (callbacks arrive on the
Android UI thread), redundant but kept on iOS so both platforms behave
identically.

## Verification

What was actually run, not inferred:

| Checked | How | Result |
|---|---|---|
| C# runtime, `UNITY_ANDROID` and `UNITY_IOS` | Unity's Roslyn `csc` + `UnityEngine.*Module.dll` | clean, including `-warnaserror` |
| Editor postprocess script | + `UnityEditor.iOS.Extensions.Xcode.dll` | clean — confirms `GetUnityMainTargetGuid`, `AddFileToEmbedFrameworks` exist with these signatures on Unity 6 |
| Java shim vs the **vendored AAR** | `javac -cp classes.jar:android-36:kotlin-stdlib` | clean — confirms `AlogameKycSdk.INSTANCE`, the 2-arg `AlogameKycConfig(env, null)` constructor, `Failed.getReason()/getMessage()` |
| Swift shim vs the **vendored xcframework** | `swiftc -typecheck -target arm64-apple-ios15.0` | clean — confirms the `AlogameKycListener` conformance and all seven `@_cdecl` entry points |
| Whole-package import | Unity 6000.5.6f1 batchmode | package resolves, all files import, both assemblies build, zero `CS` errors |
| **Real Android APK** | `BuildPipeline.BuildPlayer`, minSdk 24, ARM64 | `Succeeded`, 0 errors. The dex contains `AlogameKycUnityBridge`, `AlogameKycFlatListener`, `AlogameKycSdk` and `KycActivity` — the AAR merged and the `.java` shim compiled under Gradle |
| **Real iOS Xcode export + compile** | Unity iOS build, then `xcodebuild -sdk iphoneos` | `** BUILD SUCCEEDED **`, 0 errors |
| The shipped `.app` | `lipo`, `otool -l`, `nm` | `AlogameKycKit.framework` embedded as **arm64** (device slice, not simulator); `LC_RPATH @executable_path/Frameworks` present on the binary that loads it; all 8 `_AlogameKyc_*` C symbols exported for `DllImport` |

Two real bugs were found this way and fixed, both of the kind no amount of
reading catches:

- The embed step picked its framework slice by filesystem order, so it could
  have shipped the **simulator** binary in a device build. Slice selection is now
  explicit, keyed off `PlayerSettings.iOS.sdkVersion`.
- An `LD_RUNPATH_SEARCH_PATHS` entry added on the wrong reasoning
  (`@loader_path/Frameworks`, which resolves *inside* `UnityFramework.framework`).
  Removed; `@executable_path/Frameworks` is the entry that does the work, and the
  build confirmed the binary carrying the `@rpath` load command is
  `UnityFramework`, not the app executable.

## Known-unverified

1. **Nothing has run on a physical device.** Everything above is build-time.
   The flow itself — screen opens, OTP completes, `OnResult` arrives — is
   unproven, and the Cocos plugin in this repo is a standing reminder that a
   clean build can still crash on the first bridge call.
2. **Simulator builds are untested.** The slice-selection code has a simulator
   branch; only the device branch has been exercised.
3. **`minSdk`.** Verified to build at 24, but the native Android SDK's own
   minimum is still an open item in `alogame-kyc-sdk-android/design.md`.

### A note on `.meta` files

They are committed, and they are guid-only — no `PluginImporter` block on the
`.aar`/`.xcframework`/`.swift`/`.java`, no `MonoImporter` on the scripts. That is
what Unity itself writes, and it is correct: an absent importer block means "all
defaults", and the defaults come from the folder (`Plugins/Android` → Android,
`Plugins/iOS` → iOS). Both platform builds above ran with exactly these files
present, which is the evidence that matters.

They are committed rather than ignored because the GUIDs are what a consuming
project stores in its own references; regenerating them per machine would churn
those. Leaving them untracked would be worse than either option — Unity rewrites
them on every import, so they would sit permanently in `git status`.

## Requirements

- Unity 2021.3+
- Android: `compileSdk 36` in the generated project (the native SDK needs it)
- iOS 15+
