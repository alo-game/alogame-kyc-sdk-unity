namespace Alogame.KycSdk
{
    /// <summary>
    /// Selects which of the two compile-time base URLs the native SDK talks to.
    /// There is no way to supply a URL directly — not a setting, not a manifest
    /// entry, not a setter.
    /// </summary>
    public enum AlogameKycEnv
    {
        Dev,
        Prod,
    }

    /// <summary>
    /// Exactly these 9 values, closed on purpose — identical set to
    /// <c>AlogameKycFailReason</c> on Kotlin and Swift. Adding a tenth on any
    /// one platform without the others is a parity bug.
    ///
    /// No value means "not old enough": under-age is a storage rule enforced
    /// inside the screen, and a player who abandons there ends with
    /// <see cref="AlogameKycResult.Cancelled"/> like any other abandoned form.
    /// No value means "temporary network error" either — that is always
    /// recoverable inside the screen and never reaches OnResult.
    /// </summary>
    public enum AlogameKycFailReason
    {
        /// <summary>Integration bug — Show() or SetGameRole() called before Init(). Never show this to a player.</summary>
        NotInitialized,

        /// <summary>Integration bug — Show() called before SetGameRole(). Never show this to a player.</summary>
        NoGameRole,

        /// <summary>Could not obtain a session token in time — covers both "expired" and "no token supplied".</summary>
        SessionUnavailable,

        /// <summary>The server rejected a fresh token twice in a row — never retried a third time.</summary>
        SessionInvalid,

        /// <summary>Player exceeded the per-session wrong-code limit.</summary>
        OtpAttemptsExceeded,

        /// <summary>The backend refused to send because of an OTP-provider abuse limit — distinct from RateLimited.</summary>
        OtpUnavailable,

        /// <summary>The backend refused because of its own per-uid/device/IP abuse limit.</summary>
        RateLimited,

        /// <summary>5xx persisted after the player retried.</summary>
        ServiceUnavailable,

        /// <summary>A code the SDK does not recognise. Never a crash.</summary>
        UnknownError,
    }

    /// <summary>
    /// Mirrors the Kotlin sealed class / Swift enum. The private constructor plus
    /// nested subclasses makes the hierarchy genuinely closed — a C# nested type
    /// may call its enclosing type's private constructor, but nothing outside can,
    /// so no fourth case can be declared elsewhere.
    ///
    /// No PII rides on any branch: a game gets a status and, on failure, a closed
    /// reason it can switch on, nothing more.
    /// </summary>
    public abstract class AlogameKycResult
    {
        private AlogameKycResult() { }

        public sealed class Success : AlogameKycResult { }

        public sealed class Cancelled : AlogameKycResult { }

        public sealed class Failed : AlogameKycResult
        {
            public readonly AlogameKycFailReason Reason;

            /// <summary>Optional detail for logging. May be null. Never player-facing copy.</summary>
            public readonly string Message;

            public Failed(AlogameKycFailReason reason, string message = null)
            {
                Reason = reason;
                Message = message;
            }
        }

        public override string ToString()
        {
            if (this is Failed f)
            {
                return string.IsNullOrEmpty(f.Message)
                    ? "Failed(" + f.Reason + ")"
                    : "Failed(" + f.Reason + ", " + f.Message + ")";
            }
            return this is Success ? "Success" : "Cancelled";
        }

        /// <summary>
        /// Rebuilds a result from the flat (status, reason, message) triple both
        /// native bridges hand back. Kept here rather than in either bridge so the
        /// two platforms cannot drift — this is the single place the wire strings
        /// are interpreted.
        /// </summary>
        internal static AlogameKycResult FromFlat(string status, string reason, string message)
        {
            switch (status)
            {
                case "success":
                    return new Success();
                case "cancelled":
                    return new Cancelled();
                case "failed":
                    return new Failed(ParseReason(reason), message);
                default:
                    return new Failed(AlogameKycFailReason.UnknownError,
                        "unrecognised status from native bridge: " + (status ?? "<null>"));
            }
        }

        /// <summary>
        /// The identifiers are the raw Kotlin enum names / Swift raw values, which
        /// are identical by design. An explicit switch rather than Enum.Parse: the
        /// C# names are PascalCase and the wire values are camelCase, and a silent
        /// parse failure here would turn a real, actionable reason into
        /// UnknownError with no trace of which one it was.
        /// </summary>
        private static AlogameKycFailReason ParseReason(string reason)
        {
            switch (reason)
            {
                case "notInitialized":      return AlogameKycFailReason.NotInitialized;
                case "noGameRole":          return AlogameKycFailReason.NoGameRole;
                case "sessionUnavailable":  return AlogameKycFailReason.SessionUnavailable;
                case "sessionInvalid":      return AlogameKycFailReason.SessionInvalid;
                case "otpAttemptsExceeded": return AlogameKycFailReason.OtpAttemptsExceeded;
                case "otpUnavailable":      return AlogameKycFailReason.OtpUnavailable;
                case "rateLimited":         return AlogameKycFailReason.RateLimited;
                case "serviceUnavailable":  return AlogameKycFailReason.ServiceUnavailable;
                case "unknownError":        return AlogameKycFailReason.UnknownError;
                default:                    return AlogameKycFailReason.UnknownError;
            }
        }
    }

    /// <summary>
    /// The only channel the SDK reports back on.
    ///
    /// An abstract class rather than an interface with a default member on
    /// purpose: default interface methods are a C# 8 feature that only became
    /// usable in Unity 2021.2+, and pinning the whole package to that just to
    /// make one method optional is a bad trade. This shape works everywhere.
    /// </summary>
    public abstract class AlogameKycListener
    {
        /// <summary>
        /// Fires exactly once per <see cref="AlogameKycSdk.Show"/>, on the Unity
        /// main thread, after the native screen has already finished closing —
        /// safe to present your own UI immediately.
        /// </summary>
        public abstract void OnResult(AlogameKycResult result);

        /// <summary>
        /// Fires zero or more times before the terminal OnResult, when the token
        /// passed to SetGameRole has gone stale mid-flow. Respond with exactly one
        /// of <see cref="AlogameKycSdk.ProvideSessionToken"/> or
        /// <see cref="AlogameKycSdk.AbortPendingToken"/>, or the flow ends in
        /// SessionUnavailable once the wait times out.
        ///
        /// Empty by default — override only if your game mints tokens on demand.
        /// </summary>
        public virtual void OnSessionTokenNeeded(string uid) { }
    }
}
