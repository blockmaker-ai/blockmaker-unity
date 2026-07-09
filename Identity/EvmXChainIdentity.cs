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

        /// <summary>Player JWT obtained via wallet-signature login (see <see cref="Login"/>).</summary>
        public string SessionToken { get; private set; }
        /// <summary>Long-lived refresh token paired with <see cref="SessionToken"/>.</summary>
        public string RefreshToken { get; private set; }

        internal void UpdateTokens(string sessionToken, string refreshToken)
        {
            if (!string.IsNullOrEmpty(sessionToken)) SessionToken = sessionToken;
            if (!string.IsNullOrEmpty(refreshToken)) RefreshToken = refreshToken;
        }

        /// <summary>
        /// Clear only the in-memory JWT/refresh tokens, leaving the wallet identity and the
        /// live WalletConnect relay/session intact. Used when a token refresh fails but the
        /// wallet can still re-sign for a fresh JWT (see BlockmakerAuth.VerifyRestoredSession).
        /// </summary>
        internal void ClearTokens()
        {
            SessionToken = null;
            RefreshToken = null;
        }

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
            // WebGL has no atomic EVM group-signing bridge: the xChain JS SDK exposes only
            // per-transaction signing (EvmSignTransaction), which authorizes each txn by its
            // OWN transaction ID. A genuine atomic group must instead be authorized against the
            // GROUP ID with a single signature reused across every txn (see the native branch
            // below + XChainAddressDeriver) — so looping the per-txn signer would emit invalid
            // group signatures, and there is no window.ethereum path that reproduces the group-id
            // LogicSig wrapping in JS today. (The previous code called a JS bridge function that
            // was never declared/implemented, which broke the WebGL player build.) So:
            //   • no group field  → sign each txn individually via the single-txn path (identical
            //                        to the native "no group field" branch — fully correct on WebGL).
            //   • group field set → not supported on WebGL; fail cleanly rather than ship bad sigs.
            byte[] webglGroupId;
            try
            {
                webglGroupId = XChainAddressDeriver.ExtractGroupId(Convert.FromBase64String(unsignedTxnsBase64[0]));
            }
            catch (Exception ex)
            {
                BlockmakerLog.Error($"[EvmXChainIdentity] WebGL group-id parse error: {ex.Message}");
                onError?.Invoke("Something went wrong while signing the transactions. Please try again.");
                yield break;
            }

            if (webglGroupId != null)
            {
                onError?.Invoke("Signing multiple transactions together isn't supported for this wallet on the web. Please try again from the app.");
                yield break;
            }

            var webglResults = new string[unsignedTxnsBase64.Length];
            for (int wi = 0; wi < unsignedTxnsBase64.Length; wi++)
            {
                string webglSigned = null;
                string webglErr    = null;
                yield return SignTransaction(unsignedTxnsBase64[wi], s => { webglSigned = s; }, e => { webglErr = e; });
                if (webglErr != null) { onError?.Invoke(webglErr); yield break; }
                webglResults[wi] = webglSigned;
            }
            onSigned?.Invoke(webglResults);
#else
            // Native: atomic group signing via GroupID
            var connector = ReownWalletConnector.Instance;
            if (connector == null || !connector.IsConnected)
            {
                onError?.Invoke("Your wallet session has ended. Please connect your wallet again to continue.");
                yield break;
            }

            // Extract GroupID (non-yielding setup — catch sets error flag instead of yielding)
            byte[] groupId = null;
            string typedData = null;
            byte[] program = null;
            string setupError = null;
            try
            {
                var firstTxnBytes = Convert.FromBase64String(unsignedTxnsBase64[0]);
                groupId = XChainAddressDeriver.ExtractGroupId(firstTxnBytes);

                if (groupId != null)
                {
                    typedData = XChainAddressDeriver.BuildEip712TypedData(groupId);
                    program = XChainAddressDeriver.GetLogicSigProgram(EvmAddress);
                }
            }
            catch (Exception ex)
            {
                BlockmakerLog.Error($"[EvmXChainIdentity] Group sign setup error: {ex.Message}");
                setupError = "Something went wrong while signing the transactions. Please try again.";
            }

            if (setupError != null)
            {
                onError?.Invoke(setupError);
                yield break;
            }

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

            // Sign GroupID with EVM wallet (yield statements outside try-catch)
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

            // Build all signed transactions (non-yielding, in try-catch)
            try
            {
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
                evmAddress      = EvmAddress,
                sessionToken    = SessionToken,
                refreshToken    = RefreshToken
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
            public string sessionToken;
            public string refreshToken;
        }

        // ── Wallet-signature login (challenge → personal_sign → verify → store JWT) ─

        /// <summary>
        /// Acquire a player session token by signing a server challenge with the
        /// connected EVM wallet (personal_sign). The signature is verified against
        /// <see cref="EvmAddress"/>, but the account/JWT address is the derived
        /// Algorand <see cref="Address"/> (per the wallet-auth contract).
        /// </summary>
        public IEnumerator Login(Action onSuccess = null, Action<string> onError = null)
        {
            var client = BlockmakerClient.Instance;
            if (client == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            // 1) Challenge — walletAddress is the DERIVED Algorand address; evmAddress is the signer.
            WalletChallengeResult challenge = null;
            string challengeError = null;
            bool challengeDone = false;
            client.StartCoroutine(client.RequestWalletChallenge(
                Address, "evm", EvmAddress,
                r => { challenge = r; challengeDone = true; },
                e => { challengeError = e; challengeDone = true; }));

            float elapsed = 0f;
            while (!challengeDone && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!challengeDone) { onError?.Invoke("The request timed out. Please try again."); yield break; }
            if (challenge == null || !challenge.success || string.IsNullOrEmpty(challenge.message))
            {
                onError?.Invoke(challengeError ?? "Could not start wallet sign-in. Please try again.");
                yield break;
            }

            // 2) Sign with personal_sign over the EXACT message
            string signatureHex = null;
            string signError = null;
            bool signDone = false;

    #if UNITY_WEBGL && !UNITY_EDITOR
            if (BlockmakerAuth.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }
            int signGen = BlockmakerAuth.Instance.BeginPendingSign();
            BlockmakerWalletBridge.EvmSignPersonal(
                challenge.message,
                EvmAddress,
                BlockmakerAuth.Instance.gameObject.name,
                nameof(BlockmakerAuth.Instance.OnTxnSignedFromJS),
                nameof(BlockmakerAuth.Instance.OnTxnErrorFromJS));

            float jsElapsed = 0f;
            while (BlockmakerAuth.Instance != null &&
                   BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen) &&
                   BlockmakerAuth.Instance.PendingSignedTxn == null &&
                   BlockmakerAuth.Instance.PendingSignError == null &&
                   jsElapsed < BlockmakerAuth.WalletSignTimeout)
            {
                jsElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (BlockmakerAuth.Instance == null ||
                !BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen))
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
            signatureHex = BlockmakerAuth.Instance.ConsumePendingSignedTxn();
            signError    = BlockmakerAuth.Instance.ConsumePendingSignError();
    #else
            var connector = ReownWalletConnector.Instance;
            if (connector == null || !connector.IsConnected)
            {
                onError?.Invoke("Your wallet session has ended. Please connect your wallet again to continue.");
                yield break;
            }

            connector.SignEvmPersonalMessage(
                EvmAddress, challenge.message,
                onSignedHex: sig => { signatureHex = sig; signDone = true; },
                onError:     err => { signError = err; signDone = true; });

            float sElapsed = 0f;
            while (!signDone && sElapsed < BlockmakerAuth.WalletSignTimeout)
            {
                sElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!signDone) { onError?.Invoke("The request timed out. Please try again."); yield break; }
    #endif

            if (!string.IsNullOrEmpty(signError)) { onError?.Invoke(signError); yield break; }
            if (string.IsNullOrEmpty(signatureHex))
            {
                onError?.Invoke("The sign-in request was not approved in your wallet. Please try again.");
                yield break;
            }

            // 3) Verify → mint JWT (walletAddress = derived Algorand address; evmAddress = signer)
            EmailVerifyResult verify = null;
            string verifyError = null;
            bool verifyDone = false;
            client.StartCoroutine(client.VerifyWalletSignature(
                Address, "evm", signatureHex, null, challenge.nonce, EvmAddress,
                r => { verify = r; verifyDone = true; },
                e => { verifyError = e; verifyDone = true; }));

            float vElapsed = 0f;
            while (!verifyDone && vElapsed < BlockmakerAuth.WalletSignTimeout)
            {
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                vElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!verifyDone) { onError?.Invoke("The request timed out. Please try again."); yield break; }
            if (verify == null || !verify.success || string.IsNullOrEmpty(verify.sessionToken))
            {
                onError?.Invoke(verifyError ?? "Wallet sign-in failed. Please try again.");
                yield break;
            }

            // 4) Store
            UpdateTokens(verify.sessionToken, verify.refreshToken);
            SaveSession();
            BlockmakerLog.Info("[EvmXChainIdentity] Wallet sign-in complete — session token acquired.");
            onSuccess?.Invoke();
        }
    }

}