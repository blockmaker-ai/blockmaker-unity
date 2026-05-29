using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// Email identity backed by Magic SDK. The player's private key lives on
    /// their device (inside Magic's encrypted iframe sandbox) — never on the server.
    ///
    /// Signing is client-side via the Magic JS bridge, unlike the legacy
    /// EmailIdentity which delegates to the Blockmaker server.
    ///
    /// Session flow:
    ///   1. Player enters email → Magic handles OTP verification in its own UI
    ///   2. Magic creates or recovers the Algorand wallet on the device
    ///   3. We send Magic's DID token to the Blockmaker server to get a JWT
    ///   4. JWT stored in SecurePrefs for future API calls
    /// </summary>
    public class MagicIdentity : IBlockmakerIdentity
    {
        private static readonly Regex Base64Regex = new Regex(@"^[A-Za-z0-9+/]*=*$", RegexOptions.Compiled);

        private static bool IsValidBase64(string value)
        {
            return !string.IsNullOrEmpty(value) && Base64Regex.IsMatch(value);
        }

        private static string SessionKey => BlockmakerPrefs.Key("magic_session");

        public string       Address      { get; }
        public string       DisplayName  { get; }
        public IdentityTier Tier         => IdentityTier.Email;
        public string       ProviderName => "Magic";
        public bool         HasWallet    => true;

        public bool CanSign =>
    #if UNITY_WEBGL && !UNITY_EDITOR
            true;
    #else
            false;
    #endif

        public string Email        { get; }
        public string SessionToken { get; private set; }
        public string RefreshToken { get; private set; }

        public MagicIdentity(string email, string walletAddress, string sessionToken, string refreshToken = null)
        {
            if (string.IsNullOrEmpty(email))
                throw new ArgumentException("Invalid email", nameof(email));
            if (string.IsNullOrEmpty(walletAddress))
                throw new ArgumentException("Invalid wallet address", nameof(walletAddress));

            Email        = email;
            Address      = walletAddress;
            SessionToken = sessionToken;
            RefreshToken = refreshToken;
            DisplayName  = email.Split('@')[0];
        }

        internal void UpdateTokens(string sessionToken, string refreshToken)
        {
            if (!string.IsNullOrEmpty(sessionToken)) SessionToken = sessionToken;
            if (!string.IsNullOrEmpty(refreshToken)) RefreshToken = refreshToken;
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
            BlockmakerWalletBridge.MagicSignTransaction(
                unsignedTxnBase64,
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
            BlockmakerLog.Warning("[MagicIdentity] SignTransaction called outside WebGL.");
            error = "Transaction signing with email accounts is only available in the web browser version of this game.";
            yield return null;
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

    #if UNITY_WEBGL && !UNITY_EDITOR
            if (BlockmakerAuth.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            for (int i = 0; i < unsignedTxnsBase64.Length; i++)
            {
                if (!IsValidBase64(unsignedTxnsBase64[i]))
                {
                    onError?.Invoke("Something went wrong preparing the transaction. Please try again.");
                    yield break;
                }
            }

            int signGen = BlockmakerAuth.Instance.BeginPendingSign();

            var txnsJson = "[" + string.Join(",", System.Array.ConvertAll(unsignedTxnsBase64, t => "\"" + t + "\"")) + "]";
            BlockmakerWalletBridge.MagicSignGroupTransaction(
                txnsJson,
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

            if (BlockmakerAuth.Instance == null ||
                !BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen))
            {
                onError?.Invoke("The request was interrupted. Please try again.");
                yield break;
            }

            if (BlockmakerAuth.Instance.PendingSignedTxns == null &&
                BlockmakerAuth.Instance.PendingSignError == null)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            var results = BlockmakerAuth.Instance.ConsumePendingSignedTxns();
            var error   = BlockmakerAuth.Instance.ConsumePendingSignError();

            if (results != null)
                onSigned?.Invoke(results);
            else if (error != null)
                onError?.Invoke(error);
            else
                onError?.Invoke("The request could not be completed. Please try again.");
    #else
            BlockmakerLog.Warning("[MagicIdentity] SignTransactions called outside WebGL.");
            onError?.Invoke("Transaction signing with email accounts is only available in the web browser version of this game.");
            yield return null;
    #endif
        }

        public void SaveSession()
        {
            var data = new MagicSessionData
            {
                email         = Email,
                walletAddress = Address,
                sessionToken  = SessionToken,
                refreshToken  = RefreshToken
            };
            SecurePrefs.SetString(SessionKey, JsonUtility.ToJson(data));
            SecurePrefs.Save();
            BlockmakerLog.Info($"[MagicIdentity] Session saved for {Email}");
        }

        public void ClearSession()
        {
            SecurePrefs.DeleteKey(SessionKey);
            SecurePrefs.Save();

    #if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.MagicLogout();
    #endif
            BlockmakerLog.Info("[MagicIdentity] Session cleared.");
        }

        public static MagicIdentity TryLoadSession()
        {
            string json = SecurePrefs.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var data = JsonUtility.FromJson<MagicSessionData>(json);

                if (string.IsNullOrEmpty(data.walletAddress))
                    return null;
                if (string.IsNullOrEmpty(data.sessionToken) && string.IsNullOrEmpty(data.refreshToken))
                    return null;

                BlockmakerLog.Info($"[MagicIdentity] Restored session for {data.email}");
                return new MagicIdentity(data.email, data.walletAddress, data.sessionToken, data.refreshToken);
            }
            catch (Exception e)
            {
                BlockmakerLog.Warning($"[MagicIdentity] Corrupt session data, clearing: {e.Message}");
                SecurePrefs.DeleteKey(SessionKey);
                SecurePrefs.Save();
                return null;
            }
        }

        [Serializable]
        private class MagicSessionData
        {
            public string email;
            public string walletAddress;
            public string sessionToken;
            public string refreshToken;
        }
    }

}