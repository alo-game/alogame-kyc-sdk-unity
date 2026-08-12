using UnityEngine;
using Alogame.KycSdk;

namespace Alogame.KycSdk.Sample
{
    /// <summary>
    /// Minimal integration. Drop on any GameObject, fill in uid and sessionToken
    /// in the Inspector, run on a device, press the on-screen button.
    ///
    /// Get a token for testing with:
    ///   curl -X POST https://api-xmdt.dev.alogame.vn/sample/session \
    ///        -H 'Content-Type: application/json' -d '{"uid":"your-test-uid"}'
    ///
    /// In a real game the token comes from your own game server, which mints it
    /// with its HMAC-signed call to the identity backend. Never ship a token
    /// baked into the client.
    /// </summary>
    public sealed class AlogameKycSample : MonoBehaviour
    {
        [SerializeField] private string uid = "unity-test-uid";
        [SerializeField] private string sessionToken = "";
        [SerializeField] private AlogameKycEnv env = AlogameKycEnv.Dev;

        private string _status = "not started";

        private void Start()
        {
            AlogameKycSdk.Init(env);
            Debug.Log("[Sample] initialised for " + env);
        }

        private void OnGUI()
        {
            // Deliberately IMGUI, not uGUI: this sample has to work when dropped
            // into an empty scene with no Canvas, no EventSystem and no prefabs.
            var scale = Mathf.Max(1f, Screen.dpi / 96f);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

            GUILayout.BeginArea(new Rect(20, 20, 400, 300));
            GUILayout.Label("Alogame KYC — sample");
            GUILayout.Label("status: " + _status);
            GUILayout.Space(10);

            if (GUILayout.Button("Show KYC", GUILayout.Height(40)))
            {
                if (string.IsNullOrEmpty(sessionToken))
                {
                    _status = "set sessionToken in the Inspector first";
                }
                else
                {
                    AlogameKycSdk.SetGameRole(uid, sessionToken);
                    AlogameKycSdk.Show(new SampleListener(this));
                    _status = "showing…";
                }
            }

            if (GUILayout.Button("IsVerified?", GUILayout.Height(30)))
            {
                _status = "isVerified = " + AlogameKycSdk.IsVerified();
            }

            GUILayout.EndArea();
        }

        private sealed class SampleListener : AlogameKycListener
        {
            private readonly AlogameKycSample _owner;

            internal SampleListener(AlogameKycSample owner) { _owner = owner; }

            public override void OnResult(AlogameKycResult result)
            {
                _owner._status = "result: " + result;
                Debug.Log("[Sample] OnResult " + result);
            }

            public override void OnSessionTokenNeeded(string uid)
            {
                // A real game asks its own server for a fresh token here, then
                // calls ProvideSessionToken with it. This sample has no server to
                // ask, so it ends the flow cleanly rather than letting it stall
                // until the native timeout fires.
                Debug.Log("[Sample] OnSessionTokenNeeded(" + uid + ") — aborting, no server in this sample");
                _owner._status = "token needed — aborted (sample has no server)";
                AlogameKycSdk.AbortPendingToken();
            }
        }
    }
}
