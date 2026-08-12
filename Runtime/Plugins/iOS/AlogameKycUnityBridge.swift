import Foundation
import UIKit
import AlogameKycKit

/// Swift, not Objective-C++, and that is forced rather than chosen: the iOS SDK
/// declares no `@objc` and no `NSObject` subclass anywhere (verified by grep over
/// ios/Sources/AlogameKycSdk), and the generated `AlogameKycKit-Swift.h` inside
/// the shipped xcframework exports none of its types — it is macro boilerplate
/// only. An `.mm` file physically cannot see `AlogameKycSdk`. Swift + `@_cdecl`
/// is the one remaining path, and it is the same one already proven to work in
/// this repo's Cocos plugin.
///
/// The C symbols emitted by the `@_cdecl` functions at the bottom of this file
/// are what C# binds to with `[DllImport("__Internal")]`. No bridging header and
/// no generated interop header are involved in either direction.

/// Matches the C# delegate marshalled through `_AlogameKyc_RegisterCallback`.
public typealias AlogameKycUnityCallback = @convention(c) (UnsafePointer<CChar>?) -> Void

final class AlogameKycUnityBridge: AlogameKycListener {

    /// `AlogameKycSdk` holds its listener **weak** (see AlogameKycListener.swift:
    /// "Held `weak` by `AlogameKycSdk` — a strong reference from a singleton to a
    /// host view controller would be a retain cycle the host cannot see"). A
    /// listener created inline at the `show` call site would therefore be
    /// deallocated before the first callback. This static holds the only strong
    /// reference and lives for the process lifetime.
    static let shared = AlogameKycUnityBridge()

    private var callback: AlogameKycUnityCallback?

    private init() { }

    func register(_ callback: @escaping AlogameKycUnityCallback) {
        self.callback = callback
    }

    // MARK: - AlogameKycListener

    func onResult(_ result: AlogameKycResult) {
        switch result {
        case .success:
            send(["event": "onResult", "status": "success"])
        case .cancelled:
            send(["event": "onResult", "status": "cancelled"])
        case .failed(let reason, let message):
            var payload: [String: Any] = [
                "event": "onResult",
                "status": "failed",
                "reason": reason.rawValue,
            ]
            if let message = message { payload["message"] = message }
            send(payload)
        @unknown default:
            send([
                "event": "onResult",
                "status": "failed",
                "reason": AlogameKycFailReason.unknownError.rawValue,
            ])
        }
    }

    func onSessionTokenNeeded(uid: String) {
        send(["event": "onSessionTokenNeeded", "uid": uid])
    }

    // MARK: - Transport

    /// Always hops to the main queue first. On iOS the Unity player loop runs on
    /// the main thread, so this is also what makes the C# side safe to touch
    /// Unity APIs directly from the callback. (Android has no such guarantee and
    /// marshals separately — see AlogameKycMainThreadDispatcher.cs.)
    private func send(_ payload: [String: Any]) {
        guard
            let data = try? JSONSerialization.data(withJSONObject: payload),
            let json = String(data: data, encoding: .utf8)
        else { return }

        DispatchQueue.main.async { [weak self] in
            guard let callback = self?.callback else { return }
            json.withCString { callback($0) }
        }
    }

    /// The view controller to present from. Unity renders into the key window's
    /// root view controller; presenting from the topmost presented controller
    /// avoids "attempt to present on a controller which is already presenting".
    static func presenter() -> UIViewController? {
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        for scene in scenes where scene.activationState == .foregroundActive {
            if let window = scene.windows.first(where: { $0.isKeyWindow }) ?? scene.windows.first {
                return topmost(window.rootViewController)
            }
        }
        // Fall back to any scene at all — a game backgrounded mid-launch still
        // has a valid window, and returning nil here would surface as a spurious
        // "no root view controller" failure.
        for scene in scenes {
            if let window = scene.windows.first(where: { $0.isKeyWindow }) ?? scene.windows.first {
                return topmost(window.rootViewController)
            }
        }
        return nil
    }

    private static func topmost(_ vc: UIViewController?) -> UIViewController? {
        var current = vc
        while let presented = current?.presentedViewController {
            current = presented
        }
        return current
    }
}

// MARK: - C entry points

private func str(_ pointer: UnsafePointer<CChar>?) -> String? {
    guard let pointer = pointer else { return nil }
    return String(cString: pointer)
}

@_cdecl("_AlogameKyc_RegisterCallback")
public func _AlogameKyc_RegisterCallback(_ callback: @escaping AlogameKycUnityCallback) {
    AlogameKycUnityBridge.shared.register(callback)
}

/// `env` is 1 for prod, anything else for dev — fail-safe toward dev so a
/// marshalling mistake never points a test build at production.
@_cdecl("_AlogameKyc_Init")
public func _AlogameKyc_Init(_ env: Int32) {
    let resolved: AlogameKycEnv = (env == 1) ? .prod : .dev
    // tokenProvider always nil — a C# delegate cannot cross as a live Swift
    // closure. Refresh goes through onSessionTokenNeeded instead.
    AlogameKycSdk.shared.initialize(AlogameKycConfig(env: resolved, tokenProvider: nil))
}

@_cdecl("_AlogameKyc_SetGameRole")
public func _AlogameKyc_SetGameRole(
    _ uid: UnsafePointer<CChar>?,
    _ sessionToken: UnsafePointer<CChar>?,
    _ serverId: UnsafePointer<CChar>?,
    _ roleId: UnsafePointer<CChar>?
) {
    AlogameKycSdk.shared.setGameRole(
        uid: str(uid) ?? "",
        sessionToken: str(sessionToken) ?? "",
        serverId: str(serverId),
        roleId: str(roleId)
    )
}

@_cdecl("_AlogameKyc_SetPrefill")
public func _AlogameKyc_SetPrefill(_ fullName: UnsafePointer<CChar>?) {
    AlogameKycSdk.shared.setPrefill(str(fullName))
}

@_cdecl("_AlogameKyc_IsVerified")
public func _AlogameKyc_IsVerified() -> Int32 {
    return AlogameKycSdk.shared.isVerified ? 1 : 0
}

@_cdecl("_AlogameKyc_ProvideSessionToken")
public func _AlogameKyc_ProvideSessionToken(_ token: UnsafePointer<CChar>?) {
    AlogameKycSdk.shared.provideSessionToken(str(token) ?? "")
}

@_cdecl("_AlogameKyc_AbortPendingToken")
public func _AlogameKyc_AbortPendingToken() {
    AlogameKycSdk.shared.abortPendingToken()
}

@_cdecl("_AlogameKyc_Show")
public func _AlogameKyc_Show() {
    DispatchQueue.main.async {
        guard let presenter = AlogameKycUnityBridge.presenter() else {
            AlogameKycUnityBridge.shared.onResult(
                .failed(reason: .unknownError, message: "no presenting view controller")
            )
            return
        }
        AlogameKycSdk.shared.show(from: presenter, listener: AlogameKycUnityBridge.shared)
    }
}
