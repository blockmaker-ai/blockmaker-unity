using System;
using UnityEngine.Scripting;

namespace Blockmaker;

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
