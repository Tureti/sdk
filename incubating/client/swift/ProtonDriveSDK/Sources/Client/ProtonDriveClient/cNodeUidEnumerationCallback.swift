import Foundation
import SwiftProtobuf

final class NodeUidEnumerationCallbackWrapper: Sendable {
    let callback: NodeUidCallback

    init(callback: @escaping NodeUidCallback) {
        self.callback = callback
    }

    deinit {
        CallbackHandleRegistry.shared.removeAll(ownedBy: self)
    }
}

let cNodeUidEnumerationCallback: CCallback = { statePointer, byteArray in
    typealias BoxType = BoxedCompletionBlock<Int, WeakReference<NodeUidEnumerationCallbackWrapper>>

    guard let stateRawPointer = UnsafeRawPointer(bitPattern: statePointer) else {
        assertionFailure("cNodeUidEnumerationCallback.statePointer is nil")
        return
    }
    let stateTypedPointer = Unmanaged<BoxType>.fromOpaque(stateRawPointer)
    let weakWrapper = stateTypedPointer.takeUnretainedValue().state

    let stringValue = Google_Protobuf_StringValue(byteArray: byteArray)
    let rawValue = stringValue.value
    guard let nodeUid = SDKNodeUid(sdkCompatibleIdentifier: rawValue) else {
        weakWrapper.value?.callback(.failure(
            ProtonDriveSDKError(interopError: .incorrectIDFormat(id: rawValue))
        ))
        return
    }
    weakWrapper.value?.callback(.success(nodeUid))
}
