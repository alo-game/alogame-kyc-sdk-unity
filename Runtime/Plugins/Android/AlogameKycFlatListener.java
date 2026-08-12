package vn.alogame.kycsdk.unity;

/**
 * The listener C# implements via AndroidJavaProxy. Deliberately flat — every
 * parameter is a String, so no Kotlin type ever has to cross the JNI boundary.
 *
 * Two reasons this exists rather than having C# implement
 * vn.alogame.kycsdk.AlogameKycListener directly:
 *
 *  1. AlogameKycResult is a Kotlin sealed class. Java unpacks it with three
 *     `instanceof` checks (see AlogameKycUnityBridge.show); C# over JNI would
 *     need result.getClass().getName() plus string comparison against inner-class
 *     binary names ("...AlogameKycResult$Failed") on every single result.
 *  2. AlogameKycListener.onSessionTokenNeeded has a Kotlin default body, which
 *     compiles to a Java 8 interface default method. Whether AndroidJavaProxy
 *     falls back to it correctly when C# doesn't override is unconfirmed — this
 *     interface simply has no default, so the question never arises.
 *
 * TOP-LEVEL on purpose, not nested inside AlogameKycUnityBridge: AndroidJavaProxy
 * is constructed with the interface's JNI name, and a nested interface would need
 * the "$"-separated binary name ("...AlogameKycUnityBridge$FlatListener") — one
 * more fragile string in a place where a typo only fails at runtime.
 */
public interface AlogameKycFlatListener {
    /**
     * @param status  "success" | "failed" | "cancelled"
     * @param reason  one of the 9 AlogameKycFailReason identifiers, or null unless status is "failed"
     * @param message optional detail, always null unless status is "failed"
     */
    void onResult(String status, String reason, String message);

    void onSessionTokenNeeded(String uid);
}
