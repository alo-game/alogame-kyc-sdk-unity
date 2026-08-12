package vn.alogame.kycsdk.unity;

import android.app.Activity;

import vn.alogame.kycsdk.AlogameKycConfig;
import vn.alogame.kycsdk.AlogameKycEnv;
import vn.alogame.kycsdk.AlogameKycListener;
import vn.alogame.kycsdk.AlogameKycResult;
import vn.alogame.kycsdk.AlogameKycSdk;

/**
 * Flat Java facade over the Kotlin SDK, so the C# side never touches a Kotlin
 * type across JNI. Every parameter and return value here is a String, boolean,
 * Activity, or {@link AlogameKycFlatListener}.
 *
 * NOTE ON AlogameKycSdk.INSTANCE: AlogameKycSdk is a Kotlin `object` and has no
 * @JvmStatic annotations anywhere in the SDK (verified by grep across
 * android/sdk/src/main/kotlin). It therefore compiles to a singleton class with
 * an INSTANCE field and *instance* methods — `AlogameKycSdk.init(...)` is not a
 * static call at the bytecode level. Java source resolves this automatically
 * when you write AlogameKycSdk.INSTANCE.init(...); C# calling CallStatic()
 * against the class would compile fine and fail only at runtime. Routing every
 * call through this file removes that trap entirely.
 *
 * This file is the Unity counterpart of AlogameKycCocosBridge.java, minus the
 * JSB transport: Unity's AndroidJavaProxy delivers callbacks directly, so there
 * is no callbackId registry and no JSON envelope on this platform.
 */
public final class AlogameKycUnityBridge {

    private AlogameKycUnityBridge() { }

    /**
     * @param env "prod" selects production; anything else (including null) selects dev.
     *            Deliberately fail-safe toward dev — a typo must never silently
     *            point a test build at the production identity backend.
     */
    public static void init(Activity activity, String env) {
        AlogameKycEnv resolved = "prod".equals(env) ? AlogameKycEnv.PROD : AlogameKycEnv.DEV;
        // tokenProvider is always null: a C# delegate cannot survive as a live
        // native callback across JNI. Token refresh goes through
        // AlogameKycFlatListener.onSessionTokenNeeded instead — the path the
        // native SDK documents specifically for bridge/engine integrations.
        AlogameKycSdk.INSTANCE.init(activity.getApplicationContext(), new AlogameKycConfig(resolved, null));
    }

    public static void setGameRole(String uid, String sessionToken, String serverId, String roleId) {
        AlogameKycSdk.INSTANCE.setGameRole(uid, sessionToken, serverId, roleId);
    }

    public static void setPrefill(String fullName) {
        AlogameKycSdk.INSTANCE.setPrefill(fullName);
    }

    public static boolean isVerified() {
        return AlogameKycSdk.INSTANCE.isVerified();
    }

    public static void provideSessionToken(String token) {
        AlogameKycSdk.INSTANCE.provideSessionToken(token);
    }

    public static void abortPendingToken() {
        AlogameKycSdk.INSTANCE.abortPendingToken();
    }

    /**
     * show() must run on the Android UI thread; Unity calls in from its own main
     * thread, which is a different thread on Android. The listener callbacks
     * therefore arrive on the UI thread too — the C# side marshals them back to
     * the Unity main thread (see AlogameKycMainThreadDispatcher).
     */
    public static void show(final Activity activity, final AlogameKycFlatListener listener) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                AlogameKycSdk.INSTANCE.show(activity, new AlogameKycListener() {
                    @Override
                    public void onResult(AlogameKycResult result) {
                        if (result instanceof AlogameKycResult.Failed) {
                            AlogameKycResult.Failed failed = (AlogameKycResult.Failed) result;
                            listener.onResult("failed", failed.getReason().name(), failed.getMessage());
                        } else if (result instanceof AlogameKycResult.Cancelled) {
                            listener.onResult("cancelled", null, null);
                        } else {
                            listener.onResult("success", null, null);
                        }
                    }

                    @Override
                    public void onSessionTokenNeeded(String uid) {
                        listener.onSessionTokenNeeded(uid);
                    }
                });
            }
        });
    }
}
