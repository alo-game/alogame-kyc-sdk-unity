#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace Alogame.KycSdk.Internal
{
    /// <summary>
    /// Binds the C symbols emitted by AlogameKycUnityBridge.swift's @_cdecl
    /// functions. There is no header and no generated interop file — the symbol
    /// names below are the contract, and they must match that Swift file
    /// character for character.
    /// </summary>
    internal static class AlogameKycIosBridge
    {
        private delegate void NativeCallback(string json);

        [DllImport("__Internal")] private static extern void _AlogameKyc_RegisterCallback(NativeCallback callback);
        [DllImport("__Internal")] private static extern void _AlogameKyc_Init(int env);
        [DllImport("__Internal")] private static extern void _AlogameKyc_SetGameRole(string uid, string sessionToken, string serverId, string roleId);
        [DllImport("__Internal")] private static extern void _AlogameKyc_SetPrefill(string fullName);
        [DllImport("__Internal")] private static extern int _AlogameKyc_IsVerified();
        [DllImport("__Internal")] private static extern void _AlogameKyc_ProvideSessionToken(string token);
        [DllImport("__Internal")] private static extern void _AlogameKyc_AbortPendingToken();
        [DllImport("__Internal")] private static extern void _AlogameKyc_Show();

        /// <summary>
        /// A delegate passed to native code is not kept alive by the native side.
        /// Without this static field the GC is free to collect it while Swift
        /// still holds the function pointer, which crashes on the next callback.
        /// </summary>
        private static readonly NativeCallback _callback = OnNativeMessage;

        private static AlogameKycListener _activeListener;
        private static bool _registered;

        private static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            _AlogameKyc_RegisterCallback(_callback);
        }

        internal static void Init(AlogameKycEnv env)
        {
            EnsureRegistered();
            _AlogameKyc_Init(env == AlogameKycEnv.Prod ? 1 : 0);
        }

        internal static void SetGameRole(string uid, string sessionToken, string serverId, string roleId)
        {
            _AlogameKyc_SetGameRole(uid, sessionToken, serverId, roleId);
        }

        internal static void SetPrefill(string fullName)
        {
            _AlogameKyc_SetPrefill(fullName);
        }

        internal static bool IsVerified()
        {
            return _AlogameKyc_IsVerified() != 0;
        }

        internal static void ProvideSessionToken(string token)
        {
            _AlogameKyc_ProvideSessionToken(token);
        }

        internal static void AbortPendingToken()
        {
            _AlogameKyc_AbortPendingToken();
        }

        internal static void Show(AlogameKycListener listener)
        {
            EnsureRegistered();
            _activeListener = listener;
            _AlogameKyc_Show();
        }

        /// <summary>
        /// Must be static and carry [MonoPInvokeCallback] — IL2CPP can only build
        /// a reverse-P/Invoke thunk for a static method marked this way.
        /// </summary>
        [MonoPInvokeCallback(typeof(NativeCallback))]
        private static void OnNativeMessage(string json)
        {
            // Parsed with JsonUtility rather than a hand-rolled reader: the shape
            // is fixed and flat, and pulling in a JSON dependency for four fields
            // would be the only third-party dependency in the whole package.
            Payload payload;
            try
            {
                payload = JsonUtility.FromJson<Payload>(json);
            }
            catch (Exception e)
            {
                Debug.LogError("[AlogameKycSdk] unparseable native message: " + json + " — " + e);
                return;
            }
            if (payload == null) return;

            var listener = _activeListener;
            if (listener == null) return;

            if (payload.@event == "onSessionTokenNeeded")
            {
                AlogameKycMainThreadDispatcher.Enqueue(() => listener.OnSessionTokenNeeded(payload.uid));
                return;
            }

            if (payload.@event == "onResult")
            {
                var result = AlogameKycResult.FromFlat(payload.status, payload.reason, payload.message);
                AlogameKycMainThreadDispatcher.Enqueue(() =>
                {
                    _activeListener = null;
                    listener.OnResult(result);
                });
            }
        }

        // JsonUtility assigns these by reflection, which the compiler cannot see —
        // without this, every game importing the package gets five CS0649
        // warnings in its console that it can do nothing about.
#pragma warning disable 649
        [Serializable]
        private sealed class Payload
        {
            // Field names are the JSON keys the Swift side emits; JsonUtility
            // matches by name, so these must not be renamed independently.
            public string @event;
            public string status;
            public string reason;
            public string message;
            public string uid;
        }
#pragma warning restore 649
    }
}
#endif
