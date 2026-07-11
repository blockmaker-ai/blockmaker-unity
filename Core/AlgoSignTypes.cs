using System;
using System.Collections.Generic;
using UnityEngine.Scripting;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;

namespace Blockmaker
{

    /// <summary>
    /// WalletConnect v2 JSON-RPC types for the Algorand algo_signTxn method.
    /// Used as List&lt;List&lt;AlgoSignTxnParam&gt;&gt; — the SDK serializes the list
    /// directly as the params field of the JSON-RPC request.
    /// </summary>
    [Preserve]
    public class AlgoSignTxnParam
    {
        [Preserve] public string txn { get; set; }
        [Preserve] public string message { get; set; }
    }

    /// <summary>
    /// The algo_signTxn REQUEST wrapper. Reown resolves the RPC method name from the
    /// [RpcMethod] attribute on the request type, so the params must be a decorated
    /// class — a bare List&lt;List&lt;AlgoSignTxnParam&gt;&gt; throws
    /// "has no RpcMethodAttribute defined". Extending the same list type keeps the
    /// serialized wire format identical (ARC-0025 [[{txn:…}]]).
    /// </summary>
    [RpcMethod("algo_signTxn")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99997)]
    public class AlgoSignTxnRequest : List<List<AlgoSignTxnParam>>
    {
        public AlgoSignTxnRequest(List<List<AlgoSignTxnParam>> groups) : base(groups) { }
        [Preserve] public AlgoSignTxnRequest() { }
    }

}