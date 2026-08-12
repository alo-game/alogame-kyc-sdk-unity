# Alogame KYC SDK — Unity package

UPM package wrapping the native Android/iOS `alogame-kyc-sdk`. Native binaries
are vendored inside the package (`Runtime/Plugins/`), so there is no JitPack,
CocoaPods or SPM resolution at a game's build time.

> **Status: both platforms verified on physical devices.** On an iPhone 13 Pro
> (iOS 26.5.2) the full round trip works — SDK initialises, a real session token
> is fetched, `Show()` runs and `OnResult` arrives back in C#. On a Galaxy S9
> (Android 10) the SDK initialises, fetches a token and opens the native
> `KycActivity` full-screen. See [Verification](#verification) for what each run
> proved, and the three runtime bugs the runs caught that every build-time check
> passed.

## Install

**Prerequisites:** Unity 2021.3+ with **Android Build Support** (incl. SDK/NDK
and OpenJDK) and/or **iOS Build Support** installed through Unity Hub. Without
the platform module the package's native code paths compile out entirely and a
green console proves nothing.

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/alo-game/alogame-kyc-sdk-unity.git#0.1.3
```

Pin a tag once one exists (`https://github.com/alo-game/alogame-kyc-sdk-unity.git#0.1.3`). While iterating locally,
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
| **Real device run — Android** | Galaxy S9, Android 10, arm64 | `Init()` succeeds, a real session token is fetched from the dev backend, `Show()` opens `vn.alogame.kycsdk.internal.ui.KycActivity` full-screen |
| **Real device run — iOS** | iPhone 13 Pro, iOS 26.5.2, signed with automatic provisioning | **The whole flow, by hand, on a fresh uid**: `Init()`, token fetch (`xmdtCompleted=False`), `Show()` opens the native screen, the form and OTP are completed, and `OnResult Success` arrives back in C#. No `dyld` error, no fatal error — the rpath and resource-bundle work both hold |

Four real bugs were found this way and fixed, all of the kind no amount of
reading catches — and the two that mattered most survived every build-time
check and only appeared on a device:

- The embed step picked its framework slice by filesystem order, so it could
  have shipped the **simulator** binary in a device build. Slice selection is now
  explicit, keyed off `PlayerSettings.iOS.sdkVersion`.
- An `LD_RUNPATH_SEARCH_PATHS` entry added on the wrong reasoning
  (`@loader_path/Frameworks`, which resolves *inside* `UnityFramework.framework`).
  Removed; `@executable_path/Frameworks` is the entry that does the work, and the
  build confirmed the binary carrying the `@rpath` load command is
  `UnityFramework`, not the app executable.

- **Missing Kotlin stdlib — the one that mattered.** The APK built, installed and
  launched cleanly, then died on the first SDK call:

  ```
  java.lang.NoClassDefFoundError: kotlin.enums.EnumEntriesKt
    at vn.alogame.kycsdk.AlogameKycEnv.<clinit>
  ```

  The AAR is vendored as a plain file, so it carries no POM and Gradle never
  learns its transitive dependencies; the SDK's Kotlin enums need
  `kotlin-stdlib` 1.9+ at runtime. `Editor/AlogameKycAndroidPostprocessBuild.cs`
  now appends the coordinate to the generated Gradle project. Nothing at build
  time could have caught this — which is the entire argument for the device run.

- **The vendored xcframework was missing its SPM resource bundle.** On iOS the
  app launched, initialised and fetched a token, then died the moment `Show()`
  drew the logo:

  ```
  AlogameKycKit/resource_bundle_accessor.swift:44:
  Fatal error: unable to find bundle named AlogameKycKit_AlogameKycKit
  ```

  Not a wrapper bug at all — `ios/build-xcframework.sh` skipped the
  resource-bundle step under a comment asserting the package declares no SPM
  resources, which stopped being true when `Resources/logo-alo-white.png` was
  added. **Every xcframework that script has produced is affected, including the
  one behind the iOS SPM release**, so this is not Unity-specific. Fixed in that
  script; it now also refuses to ship a build whose bundle is empty, because the
  first attempt copied an empty placeholder from `BuildProductsPath` and turned a
  loud crash into a silently missing logo.

## Known-unverified

1. **Only the happy path has been walked.** The full flow — form, OTP, terminal
   `OnResult Success` — is confirmed on iOS. What has never been exercised on a
   device: a wrong OTP code, an expired token mid-flow (so `OnSessionTokenNeeded`
   has never actually fired), cancellation, and the Android screen past the point
   where it opens.

   Note that a uid which has already completed XMDT makes `Show()` short-circuit
   straight to `Success` without opening anything — correct behaviour, but it
   means each uid only exercises the flow once. Use a fresh uid per attempt.
3. **The iOS Simulator is not a usable test path, and that is Unity's limit, not
   this package's.** The simulator branch of the slice selection works — a build
   logs `embedded the simulator slice (…)` and `xcodebuild -sdk iphonesimulator`
   succeeds — but the resulting app will not launch on an Apple Silicon Mac:
   Unity's own prebuilt `baselib.a` for the simulator is **x86_64 only**, while
   modern simulator runtimes are arm64-native, so iOS rejects it with "This app
   needs to be updated by the developer". Forcing `ARCHS=arm64` then fails at
   link time (`symbol(s) not found for architecture arm64`) because Unity ships
   no arm64 simulator libraries. Test iOS on a physical device.
4. **`minSdk`.** Verified to build at 24, but the native Android SDK's own
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
