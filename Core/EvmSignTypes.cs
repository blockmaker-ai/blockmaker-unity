using System.Collections.Generic;
using UnityEngine.Scripting;
// Alias only the Clock type — importing the whole Reown.Core.Common.Utils namespace
// pulls in a second PreserveAttribute that collides with UnityEngine.Scripting.Preserve.
using Clock = Reown.Core.Common.Utils.Clock;
using Reown.Core.Network.Models;

namespace Blockmaker
{

    // WalletConnect v2 (Reown) JSON-RPC request types for EVM signing.
    //
    // Reown's SignClient.Request<T, …> resolves the RPC method name from the
    // [RpcMethod] attribute on T (see RpcMethodAttribute.MethodForType). A raw
    // string[] has no such attribute, so passing one throws:
    //   "Type System.String[] has no RpcMethodAttribute defined."
    // Each request type therefore extends List<string> — so it serializes as the
    // exact JSON params array the method expects — and carries the attribute.
    // Mirrors Reown.Sign.Nethereum's own PersonalSign / EthSignTypedDataV4, but
    // defined here so the SDK doesn't take a hard dependency on that assembly.

    [RpcMethod("personal_sign")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99998)]
    public class EvmPersonalSign : List<string>
    {
        // personal_sign params: [hexUtf8Message, account]
        public EvmPersonalSign(string hexUtf8Message, string account) : base(new[] { hexUtf8Message, account }) { }
        [Preserve] public EvmPersonalSign() { }
    }

    [RpcMethod("eth_signTypedData_v4")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99999)]
    public class EvmSignTypedDataV4 : List<string>
    {
        // eth_signTypedData_v4 params: [account, typedDataJson]
        public EvmSignTypedDataV4(string account, string typedDataJson) : base(new[] { account, typedDataJson }) { }
        [Preserve] public EvmSignTypedDataV4() { }
    }

}
