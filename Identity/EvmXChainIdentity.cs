using System;
using System.Collections;
using UnityEngine;

namespace Blockmaker
{

    public class EvmXChainIdentity : IBlockmakerIdentity
    {
        private static string SessionKey => BlockmakerPrefs.Key("evm_xchain_session");

        public string       Address      { get; }
        public string       DisplayName  { get; }
        public IdentityTier Tier         => IdentityTier.SelfCustody;
        public string       ProviderName => "EvmXChain";
        public bool         HasWallet    => true;

        public bool CanSign =>
    #if UNITY_WEBGL && !UNITY_EDITOR
            true;
    #else
            ReownWalletConnector.Instance != null && ReownWalletConnector.Instance.IsConnected;
    #endif

        public bool SupportsAtomicGroupSign => true;

        public string EvmAddress { get; }

        public EvmXChainIdentity(string algorandAddress, string evmAddress)
        {
            if (string.IsNullOrEmpty(algorandAddress))
                throw new ArgumentException("Invalid Algorand address", nameof(algorandAddress));
            if (string.IsNullOrEmpty(evmAddress))
                throw new ArgumentException("Invalid EVM address", nameof(evmAddress));

            Address     = algorandAddress;
            EvmAddress  = evmAddress;
            DisplayName = evmAddress.Length >= 10
                ? $"ETH · {evmAddress[..6]}...{evmAddress[^4..]}"
                : $"ETH · {evmAddress}";
        }

        public IEnumerator SignTransaction(
            string         unsignedTxnBase64,
            Action<string> onSigned,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(unsignedTxnBase64))
            {
                onError?.Invoke("Something went wrong. Please try again.");
                yield break;
            }

            string result = null;
            string error  = null;

    #if UNITY_WEBGL && !UNITY_EDITOR
            if (BlockmakerAuth.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            int signGen = BlockmakerAuth.Instance.BeginPendingSign();
            BlockmakerWalletBridge.EvmSignTransaction(
                unsignedTxnBase64,
                EvmAddress,
                BlockmakerAuth.Instance.gameObject.name,
                nameof(BlockmakerAuth.Instance.OnTxnSignedFromJS),
                nameof(BlockmakerAuth.Instance.OnTxnErrorFromJS)
            );

            float elapsed = 0f;
            while (BlockmakerAuth.Instance != null &&
                   BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen) &&
                   BlockmakerAuth.Instance.PendingSignedTxn == null &&
                   BlockmakerAuth.Instance.PendingSignError == null &&
                   elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (BlockmakerAuth.Instance != null && !BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen))
            {
                onError?.Invoke("The request was interrupted. Please try again.");
                yield break;
            }

            if (BlockmakerAuth.Instance == null)
            {
                onError?.Invoke("The request was interrupted. Please try again.");
                yield break;
            }

            if (BlockmakerAuth.Instance.PendingSignedTxn == null &&
                BlockmakerAuth.Instance.PendingSignError == null)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            result = BlockmakerAuth.Instance.ConsumePendingSignedTxn();
            error  = BlockmakerAuth.Instance.ConsumePendingSignError();
    #else
            var connector = ReownWalletConnector.Instance;
            if (connector == null || !connector.IsConnected)
            {
                onError?.Invoke("Your wallet session has ended. Please connect your wallet again to continue.");
                yield break;
            }

            var unsignedBytes = Convert.FromBase64String(unsignedTxnBase64);
            var txnId = XChainAddressDeriver.ComputeTransactionId(unsignedBytes);
            var typedData = XChainAddressDeriver.BuildEip712TypedData(txnId);
            var program = XChainAddressDeriver.GetLogicSigProgram(EvmAddress);

            bool signDone = false;

            connector.SignEvmTypedData(
                EvmAddress,
                typedData,
                onSigned: hexSig =>
                {
                    try
                    {
                        var sigArg = XChainAddressDeriver.ParseEvmSignature(hexSig);
                        var signed = XChainAddressDeriver.BuildSignedTransaction(unsignedBytes, program, sigArg);
                        result = Convert.ToBase64String(signed);
                    }
                    catch (Exception ex)
                    {
                        BlockmakerLog.Error($"[EvmXChainIdentity] Build signed txn error: {ex.Message}");
                        error = "Something went wrong while completing the signature. Please try again.";
                    }
                    signDone = true;
                },
                onError: err =>
                {
                    error = err;
                    signDone = true;
                }
            );

            float elapsed = 0f;
            while (!signDone && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!signDone)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }
    #endif

            if (result != null) onSigned?.Invoke(result);
            else if (error != null) onError?.Invoke(error);
            else onError?.Invoke("The request could not be completed. Please try again.");
        }

        public IEnumerator SignTransactions(
            string[]         unsignedTxnsBase64,
            Action<string[]> onSigned,
            Action<string>   onError)
        {
            if (unsignedTxnsBase64 == null || unsignedTxnsBase64.Length == 0)
            {
                onError?.Invoke("Something went wrong. Please try again.");
                yield break;
            }

            // Single transaction — delegate to SignTransaction
            if (unsignedTxnsBase64.Length == 1)
            {
                string signed = null;
                string err = null;
                yield return SignTransaction(unsignedTxnsBase64[0], s => { signed = s; }, e => { err = e; });
                if (err != null) { onError?.Invoke(err); yield break; }
                onSigned?.Invoke(new[] { signed });
                yield break;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: delegate to JS bridge which handles groups atomically
            if (BlockmakerAuth.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            int signGen = BlockmakerAuth.Instance.BeginPendingSign();

            var txnsJson = "[" + string.Join(",", System.Array.ConvertAll(unsignedTxnsBase64, t => "\"" + t + "\"")) + "]";
            BlockmakerWalletBridge.EvmSignGroupTransaction(
                txnsJson, EvmAddress,
                BlockmakerAuth.Instance.gameObject.name,
                nameof(BlockmakerAuth.Instance.OnGroupTxnSignedFromJS),
                nameof(BlockmakerAuth.Instance.OnTxnErrorFromJS)
            );

            float elapsed = 0f;
            while (BlockmakerAuth.Instance != null &&
                   BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen) &&
                   BlockmakerAuth.Instance.PendingSignedTxns == null &&
                   BlockmakerAuth.Instance.PendingSignError == null &&
                   elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (BlockmakerAuth.Instance == null || !BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen))
            {
                onError?.Invoke("The request was interrupted. Please try again.");
                yield break;
            }

            if (BlockmakerAuth.Instance.PendingSignedTxns == null && BlockmakerAuth.Instance.PendingSignError == null)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            var webglResults = BlockmakerAuth.Instance.ConsumePendingSignedTxns();
            var webglError = BlockmakerAuth.Instance.ConsumePendingSignError();

            if (webglResults != null) onSigned?.Invoke(webglResults);
            else if (webglError != null) onError?.Invoke(webglError);
            else onError?.Invoke("The request could not be completed. Please try again.");
#else
            // Native: atomic group signing via GroupID
            var connector = ReownWalletConnector.Instance;
            if (connector == null || !connector.IsConnected)
            {
                onError?.Invoke("Your wallet session has ended. Please connect your wallet again to continue.");
                yield break;
            }

            try
            {
                // Decode first transaction to extract GroupID
                var firstTxnBytes = Convert.FromBase64String(unsignedTxnsBase64[0]);
                var groupId = XChainAddressDeriver.ExtractGroupId(firstTxnBytes);

                if (groupId == null)
                {
                    // No group field — sign individually (not an atomic group)
                    var results = new string[unsignedTxnsBase64.Length];
                    for (int i = 0; i < unsignedTxnsBase64.Length; i++)
                    {
                        string signed = null;
                        string err = null;
                        yield return SignTransaction(unsignedTxnsBase64[i], s => { signed = s; }, e => { err = e; });
                        if (err != null) { onError?.Invoke(err); yield break; }
                        results[i] = signed;
                    }
                    onSigned?.Invoke(results);
                    yield break;
                }

                // Build EIP-712 typed data with GroupID (not individual TxID)
                var typedData = XChainAddressDeriver.BuildEip712TypedData(groupId);
                var program = XChainAddressDeriver.GetLogicSigProgram(EvmAddress);

                string hexSig = null;
                string signError = null;
                bool signDone = false;

                connector.SignEvmTypedData(
                    EvmAddress,
                    typedData,
                    onSigned: sig => { hexSig = sig; signDone = true; },
                    onError: err => { signError = err; signDone = true; }
                );

                float elapsed = 0f;
                while (!signDone && elapsed < BlockmakerAuth.WalletSignTimeout)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!signDone)
                {
                    onError?.Invoke("The request timed out. Please try again.");
                    yield break;
                }

                if (signError != null)
                {
                    onError?.Invoke(signError);
                    yield break;
                }

                // Parse the single signature and apply it to ALL transactions
                var sigArg = XChainAddressDeriver.ParseEvmSignature(hexSig);
                var signedResults = new string[unsignedTxnsBase64.Length];

                for (int i = 0; i < unsignedTxnsBase64.Length; i++)
                {
                    var txnBytes = Convert.FromBase64String(unsignedTxnsBase64[i]);
                    var signedBytes = XChainAddressDeriver.BuildSignedTransaction(txnBytes, program, sigArg);
                    signedResults[i] = Convert.ToBase64String(signedBytes);
                }

                onSigned?.Invoke(signedResults);
            }
            catch (Exception ex)
            {
                BlockmakerLog.Error($"[EvmXChainIdentity] Group sign error: {ex.Message}");
                onError?.Invoke("Something went wrong while signing the transactions. Please try again.");
            }
#endif
        }

        public void SaveSession()
        {
            var data = new EvmSessionData
            {
                algorandAddress = Address,
                evmAddress      = EvmAddress
            };
            SecurePrefs.SetString(SessionKey, JsonUtility.ToJson(data));
            SecurePrefs.Save();
            BlockmakerLog.Info($"[EvmXChainIdentity] Session saved: {EvmAddress} → {Address}");
        }

        public void ClearSession()
        {
            SecurePrefs.DeleteKey(SessionKey);
            SecurePrefs.Save();

            var connector = ReownWalletConnector.Instance;
            if (connector != null &&
                (string.Equals(connector.ConnectedAddress, EvmAddress, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(connector.ConnectedAddress, Address, StringComparison.OrdinalIgnoreCase)))
            {
                connector.Disconnect();
            }

    #if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.EvmDisconnect();
    #endif
            BlockmakerLog.Info("[EvmXChainIdentity] Session cleared.");
        }

        public static EvmSessionData TryLoadSessionData()
        {
            string json = SecurePrefs.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var data = JsonUtility.FromJson<EvmSessionData>(json);
                if (string.IsNullOrEmpty(data.algorandAddress) ||
                    string.IsNullOrEmpty(data.evmAddress))
                    return null;
                return data;
            }
            catch (Exception e)
            {
                BlockmakerLog.Warning($"[EvmXChainIdentity] Corrupt session data, clearing: {e.Message}");
                SecurePrefs.DeleteKey(SessionKey);
                SecurePrefs.Save();
                return null;
            }
        }

        [Serializable]
        public class EvmSessionData
        {
            public string algorandAddress;
            public string evmAddress;
        }
    }

}