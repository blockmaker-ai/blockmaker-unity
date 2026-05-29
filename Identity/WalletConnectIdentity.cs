using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Blockmaker;

public abstract class WalletConnectIdentity : IBlockmakerIdentity
{
    private static readonly Regex Base64Regex = new Regex(@"^[A-Za-z0-9+/]*=*$", RegexOptions.Compiled);

    private static bool IsValidBase64(string value)
    {
        return !string.IsNullOrEmpty(value) && Base64Regex.IsMatch(value);
    }

    private static string SessionKeyPrefix => BlockmakerPrefs.Key("wc_session_");

    public string       Address      { get; }
    public string       DisplayName  { get; }
    public IdentityTier Tier         => IdentityTier.SelfCustody;
    public abstract string ProviderName { get; }
    public bool         HasWallet    => true;
    public bool CanSign
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            if (OwnWCv1Client != null && !OwnWCv1Client.IsDisposed) return true;
            var connector = ReownWalletConnector.Instance;
            return connector != null && connector.IsConnected;
#endif
        }
    }

    internal WalletConnectV1Client OwnWCv1Client { get; set; }

    protected WalletConnectIdentity(string address)
    {
        if (!IsValidAlgorandAddress(address))
            throw new ArgumentException("Invalid Algorand wallet address", nameof(address));

        Address     = address;
        DisplayName = $"{address[..6]}...{address[^4..]}";
    }

    public static bool IsValidAlgorandAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length != 58)
            return false;
        for (int i = 0; i < address.Length; i++)
        {
            char c = address[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= '2' && c <= '7')))
                return false;
        }

        try
        {
            byte[] decoded = XChainAddressDeriver.Base32Decode(address);
            if (decoded.Length != 36) return false;

            byte[] publicKey = new byte[32];
            byte[] checksum  = new byte[4];
            Buffer.BlockCopy(decoded, 0, publicKey, 0, 32);
            Buffer.BlockCopy(decoded, 32, checksum, 0, 4);

            byte[] hash = XChainAddressDeriver.Sha512_256(publicKey);
            if (hash[28] != checksum[0] || hash[29] != checksum[1] ||
                hash[30] != checksum[2] || hash[31] != checksum[3])
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }

    private WalletConnectV1Client GetWCv1Client()
    {
        if (OwnWCv1Client != null && !OwnWCv1Client.IsDisposed)
            return OwnWCv1Client;

        var authClient = BlockmakerAuth.Instance?.WCv1Client;
        if (authClient != null && !authClient.IsDisposed)
        {
            OwnWCv1Client = authClient;
            return authClient;
        }

        var savedSession = TryLoadWCv1Session(ProviderName);
        if (savedSession != null)
        {
            var restored = WalletConnectV1Client.FromSession(savedSession);
            OwnWCv1Client = restored;
            return restored;
        }

        return null;
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
        bool   done   = false;

        var wcv1 = GetWCv1Client();
        if (wcv1 != null &&
            string.Equals(wcv1.Address, Address, StringComparison.OrdinalIgnoreCase))
        {
            if (!wcv1.IsConnected)
            {
                BlockmakerLog.Info($"[{ProviderName}Identity] WCv1 session exists but disconnected — reconnecting...");
                var reconnectTask = wcv1.Reconnect();
                float rcElapsed = 0f;
                while (!reconnectTask.IsCompleted && rcElapsed < 10f)
                {
                    rcElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (reconnectTask.IsFaulted || !wcv1.IsConnected)
                {
                    BlockmakerLog.Warning($"[{ProviderName}Identity] Reconnect failed: {reconnectTask.Exception?.InnerException?.Message}");
                    wcv1.Dispose();
                    OwnWCv1Client = null;
                    onError?.Invoke("Your wallet connection was lost. Please connect your wallet again to continue.");
                    yield break;
                }
                BlockmakerLog.Info($"[{ProviderName}Identity] Reconnected to WCv1 bridge.");
            }

            if (!IsValidBase64(unsignedTxnBase64))
            {
                onError?.Invoke("Something went wrong preparing the transaction. Please try again.");
                yield break;
            }

            string paramsJson = $"[[{{\"txn\":\"{unsignedTxnBase64}\"}}]]";
            var signTask = wcv1.SendCustomRequest("algo_signTxn", paramsJson);

            float elapsed = 0f;
            while (!signTask.IsCompleted && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!signTask.IsCompleted)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (signTask.IsFaulted)
            {
                BlockmakerLog.Error($"[{ProviderName}Identity] Sign error: {signTask.Exception?.InnerException?.Message}");
                onError?.Invoke("Your wallet could not complete the request. Please try again.");
                yield break;
            }

            string responseJson = signTask.Result;
            var signResult = JsonUtility.FromJson<WcJsonRpcResult>(responseJson);
            if (signResult.result != null && signResult.result.Length > 0 && !string.IsNullOrEmpty(signResult.result[0]))
            {
                onSigned?.Invoke(signResult.result[0]);
            }
            else
            {
                onError?.Invoke("The transaction was not approved in your wallet. Please try again.");
            }
            yield break;
        }

        // Try Reown WC v2 (Defly, EVM)
        var connector = ReownWalletConnector.Instance;
        if (connector != null && connector.IsConnected &&
            string.Equals(connector.ConnectedAddress, Address, StringComparison.OrdinalIgnoreCase))
        {
            connector.SignAlgorandTransaction(
                unsignedTxnBase64,
                onSigned: signed => { result = signed; done = true; },
                onError:  err    => { error = err; done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!done)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (result != null)     onSigned?.Invoke(result);
            else if (error != null) onError?.Invoke(error);
            else                    onError?.Invoke("The request could not be completed. Please try again.");
            yield break;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (BlockmakerAuth.Instance == null)
        {
            onError?.Invoke("Something went wrong. Please restart the game and try again.");
            yield break;
        }

        int signGen = BlockmakerAuth.Instance.BeginPendingSign();
        BlockmakerWalletBridge.SignTransaction(
            provider:          ProviderName,
            txnBase64:         unsignedTxnBase64,
            gameObjectName:    BlockmakerAuth.Instance.gameObject.name,
            successCallback:   nameof(BlockmakerAuth.Instance.OnTxnSignedFromJS),
            errorCallback:     nameof(BlockmakerAuth.Instance.OnTxnErrorFromJS)
        );

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
        BlockmakerLog.Warning($"[{ProviderName}Identity] No active WC v1/v2 session and no JS bridge available.");
        error = "Your wallet is not connected. Please connect your wallet again to continue.";
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

        string[] results = null;
        string   error   = null;
        bool     done    = false;

        var wcv1 = GetWCv1Client();
        if (wcv1 != null &&
            string.Equals(wcv1.Address, Address, StringComparison.OrdinalIgnoreCase))
        {
            if (!wcv1.IsConnected)
            {
                BlockmakerLog.Info($"[{ProviderName}Identity] WCv1 session exists but disconnected — reconnecting...");
                var reconnectTask = wcv1.Reconnect();
                float rcElapsed = 0f;
                while (!reconnectTask.IsCompleted && rcElapsed < 10f)
                {
                    rcElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (reconnectTask.IsFaulted || !wcv1.IsConnected)
                {
                    BlockmakerLog.Warning($"[{ProviderName}Identity] Reconnect failed: {reconnectTask.Exception?.InnerException?.Message}");
                    wcv1.Dispose();
                    OwnWCv1Client = null;
                    onError?.Invoke("Your wallet connection was lost. Please connect your wallet again to continue.");
                    yield break;
                }
                BlockmakerLog.Info($"[{ProviderName}Identity] Reconnected to WCv1 bridge.");
            }

            for (int i = 0; i < unsignedTxnsBase64.Length; i++)
            {
                if (!IsValidBase64(unsignedTxnsBase64[i]))
                {
                    onError?.Invoke("Something went wrong preparing the transaction. Please try again.");
                    yield break;
                }
            }

            var txnObjects = new System.Text.StringBuilder("[");
            for (int i = 0; i < unsignedTxnsBase64.Length; i++)
            {
                if (i > 0) txnObjects.Append(",");
                txnObjects.Append($"{{\"txn\":\"{unsignedTxnsBase64[i]}\"}}");
            }
            txnObjects.Append("]");
            string paramsJson = $"[{txnObjects}]";

            var signTask = wcv1.SendCustomRequest("algo_signTxn", paramsJson);

            float elapsed = 0f;
            while (!signTask.IsCompleted && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!signTask.IsCompleted)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (signTask.IsFaulted)
            {
                BlockmakerLog.Error($"[{ProviderName}Identity] Sign error: {signTask.Exception?.InnerException?.Message}");
                onError?.Invoke("Your wallet could not complete the request. Please try again.");
                yield break;
            }

            var signResult = JsonUtility.FromJson<WcJsonRpcResult>(signTask.Result);
            if (signResult.result != null && signResult.result.Length == unsignedTxnsBase64.Length)
            {
                bool allSigned = true;
                for (int j = 0; j < signResult.result.Length; j++)
                {
                    if (string.IsNullOrEmpty(signResult.result[j]))
                    { allSigned = false; break; }
                }
                if (allSigned)
                    onSigned?.Invoke(signResult.result);
                else
                    onError?.Invoke("Not all transactions were approved. Please approve all of them in your wallet and try again.");
            }
            else
            {
                onError?.Invoke("The transactions were not approved in your wallet. Please try again.");
            }
            yield break;
        }

        var connector = ReownWalletConnector.Instance;
        if (connector != null && connector.IsConnected &&
            string.Equals(connector.ConnectedAddress, Address, StringComparison.OrdinalIgnoreCase))
        {
            connector.SignAlgorandTransactions(
                unsignedTxnsBase64,
                onSigned: signed => { results = signed; done = true; },
                onError:  err    => { error = err; done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!done)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (results != null) onSigned?.Invoke(results);
            else if (error != null) onError?.Invoke(error);
            else onError?.Invoke("The request could not be completed. Please try again.");
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

        string txnsJson = "[";
        for (int i = 0; i < unsignedTxnsBase64.Length; i++)
        {
            if (i > 0) txnsJson += ",";
            txnsJson += $"\"{unsignedTxnsBase64[i]}\"";
        }
        txnsJson += "]";

        int signGen = BlockmakerAuth.Instance.BeginPendingSign();
        BlockmakerWalletBridge.SignGroupTransaction(
            provider:          ProviderName,
            txnsJson:          txnsJson,
            gameObjectName:    BlockmakerAuth.Instance.gameObject.name,
            successCallback:   nameof(BlockmakerAuth.Instance.OnGroupTxnSignedFromJS),
            errorCallback:     nameof(BlockmakerAuth.Instance.OnTxnErrorFromJS)
        );

        float jsElapsed = 0f;
        while (BlockmakerAuth.Instance != null &&
               BlockmakerAuth.Instance.IsSignGenerationCurrent(signGen) &&
               BlockmakerAuth.Instance.PendingSignedTxns == null &&
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

        if (BlockmakerAuth.Instance.PendingSignedTxns == null &&
            BlockmakerAuth.Instance.PendingSignError == null)
        {
            onError?.Invoke("The request timed out. Please try again.");
            yield break;
        }

        results = BlockmakerAuth.Instance.ConsumePendingSignedTxns();
        error   = BlockmakerAuth.Instance.ConsumePendingSignError();

        if (results != null) onSigned?.Invoke(results);
        else if (error != null) onError?.Invoke(error);
        else onError?.Invoke("The request could not be completed. Please try again.");
        yield break;
#endif

        onError?.Invoke("Your wallet is not connected. Please connect your wallet again to continue.");
        yield return null;
    }

    public void SaveSession()
    {
        var data = new WalletSessionData
        {
            providerName = ProviderName,
            address      = Address
        };
        string key = SessionKeyPrefix + ProviderName.ToLower();
        SecurePrefs.SetString(key, JsonUtility.ToJson(data));

        var wcv1 = GetWCv1Client();
        if (wcv1 != null)
        {
            var sessionData = wcv1.GetSessionData();
            if (sessionData != null)
            {
                string wcKey = SessionKeyPrefix + ProviderName.ToLower() + "_wcv1";
                SecurePrefs.SetString(wcKey, JsonUtility.ToJson(sessionData));
            }
        }

        SecurePrefs.Save();
    }

    public void ClearSession()
    {
        string key = SessionKeyPrefix + ProviderName.ToLower();
        SecurePrefs.DeleteKey(key);
        SecurePrefs.DeleteKey(key + "_wcv1");
        SecurePrefs.Save();

        if (OwnWCv1Client != null && !OwnWCv1Client.IsDisposed)
        {
            OwnWCv1Client.Dispose();
        }
        OwnWCv1Client = null;

        var connector = ReownWalletConnector.Instance;
        if (connector != null &&
            string.Equals(connector.ConnectedAddress, Address, StringComparison.OrdinalIgnoreCase))
        {
            connector.Disconnect();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        BlockmakerWalletBridge.Disconnect(ProviderName);
#endif
        BlockmakerLog.Info($"[{ProviderName}Identity] Session cleared.");
    }

    public static WalletSessionData TryLoadSessionData(string providerName)
    {
        string key  = SessionKeyPrefix + providerName.ToLower();
        string json = SecurePrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var data = JsonUtility.FromJson<WalletSessionData>(json);
            if (string.IsNullOrEmpty(data.address))
                return null;
            return data;
        }
        catch (Exception e)
        {
            BlockmakerLog.Warning($"[WalletConnectIdentity] Corrupt session data for {providerName}, clearing: {e.Message}");
            SecurePrefs.DeleteKey(key);
            SecurePrefs.Save();
            return null;
        }
    }

    public static WalletConnectV1Client.SavedSession TryLoadWCv1Session(string providerName)
    {
        string key  = SessionKeyPrefix + providerName.ToLower() + "_wcv1";
        string json = SecurePrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<WalletConnectV1Client.SavedSession>(json); }
        catch (Exception e)
        {
            BlockmakerLog.Warning($"[WalletConnectIdentity] Corrupt WCv1 session for {providerName}, clearing: {e.Message}");
            SecurePrefs.DeleteKey(key);
            SecurePrefs.Save();
            return null;
        }
    }

    [Serializable]
    public class WalletSessionData
    {
        public string providerName;
        public string address;
    }
}

public class PeraIdentity : WalletConnectIdentity
{
    public override string ProviderName => "Pera";
    public PeraIdentity(string address) : base(address) { }
}

public class DeflyIdentity : WalletConnectIdentity
{
    public override string ProviderName => "Defly";
    public DeflyIdentity(string address) : base(address) { }
}
