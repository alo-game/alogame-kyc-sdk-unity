#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace Alogame.KycSdk.Internal
{
    /// <summary>
    /// Talks to vn.alogame.kycsdk.unity.AlogameKycUnityBridge — the flat Java
    /// facade shipped in Runtime/Plugins/Android — never to the Kotlin SDK
    /// directly. See that file for why: AlogameKycSdk is a Kotlin `object`
    /// without @JvmStatic, so CallStatic() against it compiles and then fails at
    /// runtime, and AlogameKycResult is a sealed class that would need
    /// getClass().getName() string-matching to unpack from here.
    /// </summary>
    internal static class AlogameKycAndroidBridge
    {
        private const string BridgeClassName = "vn.alogame.kycsdk.unity.AlogameKycUnityBridge";
        private const string ListenerInterfaceName = "vn.alogame.kycsdk.unity.AlogameKycFlatListener";

        private static AndroidJavaClass _bridge;
        private static AndroidJavaObject _activity;

        /// <summary>
        /// Held in a static so the proxy survives for the whole flow. A proxy that
        /// only the JNI side references is eligible for GC on the C# side, and a
        /// collected proxy turns the next callback into a hard crash.
        /// </summary>
        private static ListenerProxy _activeProxy;

        private static AndroidJavaClass Bridge
        {
            get
            {
                if (_bridge == null) _bridge = new AndroidJavaClass(BridgeClassName);
                return _bridge;
            }
        }

        private static AndroidJavaObject Activity
        {
            get
            {
                if (_activity == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    {
                        _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    }
                }
                return _activity;
            }
        }

        internal static void Init(AlogameKycEnv env)
        {
            Bridge.CallStatic("init", Activity, env == AlogameKycEnv.Prod ? "prod" : "dev");
        }

        internal static void SetGameRole(string uid, string sessionToken, string serverId, string roleId)
        {
            Bridge.CallStatic("setGameRole", uid, sessionToken, serverId, roleId);
        }

        internal static void SetPrefill(string fullName)
        {
            Bridge.CallStatic("setPrefill", fullName);
        }

        internal static bool IsVerified()
        {
            return Bridge.CallStatic<bool>("isVerified");
        }

        internal static void ProvideSessionToken(string token)
        {
            Bridge.CallStatic("provideSessionToken", token);
        }

        internal static void AbortPendingToken()
        {
            Bridge.CallStatic("abortPendingToken");
        }

        internal static void Show(AlogameKycListener listener)
        {
            _activeProxy = new ListenerProxy(listener);
            Bridge.CallStatic("show", Activity, _activeProxy);
        }

        /// <summary>
        /// Implements the flat Java interface. Method names must match the Java
        /// source exactly and in lowerCamelCase — AndroidJavaProxy dispatches by
        /// name at runtime, so a rename here fails silently as "no such method"
        /// rather than at compile time.
        /// </summary>
        private sealed class ListenerProxy : AndroidJavaProxy
        {
            private readonly AlogameKycListener _listener;

            internal ListenerProxy(AlogameKycListener listener) : base(ListenerInterfaceName)
            {
                _listener = listener;
            }

            // Invoked on the Android UI thread.
            public void onResult(string status, string reason, string message)
            {
                var result = AlogameKycResult.FromFlat(status, reason, message);
                AlogameKycMainThreadDispatcher.Enqueue(() =>
                {
                    // Released before delivery, not after: the game is free to
                    // call Show() again from inside OnResult, and that call
                    // installs its own proxy.
                    _activeProxy = null;
                    _listener.OnResult(result);
                });
            }

            // Invoked on the Android UI thread.
            public void onSessionTokenNeeded(string uid)
            {
                AlogameKycMainThreadDispatcher.Enqueue(() => _listener.OnSessionTokenNeeded(uid));
            }
        }
    }
}
#endif
