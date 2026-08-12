using UnityEngine;
using Alogame.KycSdk.Internal;

namespace Alogame.KycSdk
{
    /// <summary>
    /// The entire public API. Maps 1:1 onto the native Kotlin/Swift surface, so
    /// integration knowledge transfers directly between this package, the Cocos
    /// plugin, and a native integration.
    ///
    /// Typical order: Init once at startup, SetGameRole whenever the player's
    /// uid/session token arrives from your game server, then Show when the game
    /// needs the player verified.
    ///
    /// In the Editor and on desktop every call is a logged no-op rather than an
    /// exception — a game must stay playable in Play Mode without a device.
    /// </summary>
    public static class AlogameKycSdk
    {
        private const string Tag = "[AlogameKycSdk]";

        /// <summary>Call once before SetGameRole or Show. Idempotent — a later call replaces the earlier config.</summary>
        public static void Init(AlogameKycEnv env)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AlogameKycAndroidBridge.Init(env);
#elif UNITY_IOS && !UNITY_EDITOR
            AlogameKycIosBridge.Init(env);
#else
            LogUnsupported("Init(" + env + ")");
#endif
        }

        /// <summary>
        /// uid and sessionToken always come from the same game-server response —
        /// never call this with one and not the other. Switching uid clears the
        /// cached verified state and prefill name for the previous one.
        /// </summary>
        public static void SetGameRole(string uid, string sessionToken, string serverId = null, string roleId = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AlogameKycAndroidBridge.SetGameRole(uid, sessionToken, serverId, roleId);
#elif UNITY_IOS && !UNITY_EDITOR
            AlogameKycIosBridge.SetGameRole(uid, sessionToken, serverId, roleId);
#else
            LogUnsupported("SetGameRole(" + uid + ")");
#endif
        }

        /// <summary>
        /// Optional — prefills the name field when your login layer already knows
        /// it. On iOS, ASAuthorizationAppleIDCredential.fullName arrives only on
        /// the *first* authorize for a given Apple ID and is nil forever after
        /// unless the user revokes the app in Settings; capture it there or it is
        /// gone permanently.
        /// </summary>
        public static void SetPrefill(string fullName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AlogameKycAndroidBridge.SetPrefill(fullName);
#elif UNITY_IOS && !UNITY_EDITOR
            AlogameKycIosBridge.SetPrefill(fullName);
#else
            LogUnsupported("SetPrefill");
#endif
        }

        /// <summary>
        /// In-memory cache from the most recent successful flow for the current
        /// uid. UI state only — <b>never</b> an authorization source. Your game
        /// server must call POST /xmdt/user-status before trusting that a player
        /// has verified; a modified client can make this return anything.
        /// </summary>
        public static bool IsVerified()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return AlogameKycAndroidBridge.IsVerified();
#elif UNITY_IOS && !UNITY_EDITOR
            return AlogameKycIosBridge.IsVerified();
#else
            LogUnsupported("IsVerified");
            return false;
#endif
        }

        /// <summary>Respond to OnSessionTokenNeeded with a freshly minted token.</summary>
        public static void ProvideSessionToken(string token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AlogameKycAndroidBridge.ProvideSessionToken(token);
#elif UNITY_IOS && !UNITY_EDITOR
            AlogameKycIosBridge.ProvideSessionToken(token);
#else
            LogUnsupported("ProvideSessionToken");
#endif
        }

        /// <summary>
        /// Respond to OnSessionTokenNeeded when a fresh token could not be
        /// obtained — ends the flow with SessionUnavailable rather than leaving
        /// the player on a stalled screen.
        /// </summary>
        public static void AbortPendingToken()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AlogameKycAndroidBridge.AbortPendingToken();
#elif UNITY_IOS && !UNITY_EDITOR
            AlogameKycIosBridge.AbortPendingToken();
#else
            LogUnsupported("AbortPendingToken");
#endif
        }

        /// <summary>
        /// Presents the native verification screen. listener.OnResult fires
        /// exactly once, terminally, on the Unity main thread;
        /// OnSessionTokenNeeded may fire zero or more times before it. Calling
        /// Show again while a flow is already open is ignored by the native SDK
        /// (logged, not an error).
        /// </summary>
        public static void Show(AlogameKycListener listener)
        {
            if (listener == null)
            {
                Debug.LogError(Tag + " Show(null) — a listener is required.");
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            AlogameKycAndroidBridge.Show(listener);
#elif UNITY_IOS && !UNITY_EDITOR
            AlogameKycIosBridge.Show(listener);
#else
            LogUnsupported("Show");
            // Answer on the same path a device would, so Editor play-testing
            // exercises the game's real result-handling branch instead of
            // silently doing nothing.
            AlogameKycMainThreadDispatcher.Enqueue(() => listener.OnResult(
                new AlogameKycResult.Failed(
                    AlogameKycFailReason.UnknownError,
                    "AlogameKycSdk is not available on this platform")));
#endif
        }

#if !((UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR)
        private static void LogUnsupported(string call)
        {
            Debug.Log(Tag + " " + call + " ignored — Android/iOS device builds only.");
        }
#endif
    }
}
