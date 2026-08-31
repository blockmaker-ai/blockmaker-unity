using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Blockmaker
{

    /// <summary>
    /// HTTP client for the Blockmaker server.
    /// All game systems call this — it knows nothing about which wallet or
    /// auth provider is active.
    ///
    /// Auth identity is read from BlockmakerAuth.Instance.Identity automatically
    /// for every request that needs a wallet address.
    /// </summary>
    public partial class BlockmakerClient : MonoBehaviour
    {
        public static BlockmakerClient Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        [Header("Config")]
        public BlockmakerConfig config;

        private string _baseUrl;
        private string _gameId;
        private string _configurationError;

        /// <summary>Validated API origin currently used by the SDK, or null when configuration is invalid.</summary>
        public string BaseUrl => _baseUrl;
        /// <summary>Validated public Blockmaker game ID currently bound to requests.</summary>
        public string GameId => _gameId;
        /// <summary>True only when the SDK has a valid HTTPS/localhost origin and public game ID.</summary>
        public bool IsConfigured => _baseUrl != null && _gameId != null;
        /// <summary>User-safe configuration error when <see cref="IsConfigured"/> is false.</summary>
        public string ConfigurationError => _configurationError;

        // Refresh tokens rotate on every successful exchange. All concurrent callers for
        // the current token must therefore share one HTTP request; sending the same old
        // token twice is correctly treated as replay by the server and revokes the session.
        private bool _refreshInProgress;
        private string _refreshTokenInFlight;
        private readonly List<RefreshWaiter> _refreshWaiters = new List<RefreshWaiter>();

        // Managed-wallet signing is allowed only for exact transaction bytes authored by a
        // Blockmaker builder. HandleResponse records the short-lived server intent against
        // those exact bytes so existing identity.SignTransaction(s) call sites remain safe
        // without making the game pass authorization tokens around manually.
        private readonly Dictionary<string, CachedSigningIntent> _signingIntents =
            new Dictionary<string, CachedSigningIntent>();

        private sealed class RefreshWaiter
        {
            public Action<RefreshTokenResult> onSuccess;
            public Action<string> onError;
        }

        private sealed class CachedSigningIntent
        {
            public string token;
            public long expiresAt;
        }

        [Serializable]
        private sealed class SigningIntentEnvelope
        {
            public string signingIntent;
            public long signingIntentExpiresAt;
            public string unsignedTxnBase64;
            public string[] unsignedTxnsBase64;
            public string[] unsignedTxns;
            public string unsignedOptInTxn;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (config == null)
            {
                // Config may be assigned post-Awake by BlockmakerAuth.EnsureBlockmakerClient()
                // via InitFromAuth(). Don't destroy — just disable until config arrives.
                BlockmakerLog.Verbose("[BlockmakerClient] No BlockmakerConfig assigned yet — waiting for BlockmakerAuth.InitFromAuth().");
                Instance = null;
                enabled = false;
                return;
            }
            InitializeConfig();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Re-initializes after config was set post-Awake (e.g. when added via AddComponent at runtime).
        /// Called by BlockmakerAuth.EnsureBlockmakerClient().
        /// </summary>
        public void InitFromAuth()
        {
            if (config == null) return;
            enabled = true;
            if (Instance == null) Instance = this;
            InitializeConfig();
        }

        private void InitializeConfig()
        {
            _baseUrl = null;
            _gameId = null;
            _configurationError = null;

            if (config == null)
            {
                _configurationError = "Blockmaker is not configured. Ask the game developer to add a BlockmakerConfig asset.";
                return;
            }

            var rawUrl = string.IsNullOrWhiteSpace(config.serverUrl)
                ? BlockmakerConfig.DefaultServerUrl
                : config.serverUrl.Trim();
            Uri parsed;
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(parsed.UserInfo)
                || (parsed.AbsolutePath != "/" && parsed.AbsolutePath != "")
                || !string.IsNullOrEmpty(parsed.Query)
                || !string.IsNullOrEmpty(parsed.Fragment))
            {
                _configurationError = "Blockmaker server URL must be an exact HTTPS origin with no path, query, or credentials.";
                BlockmakerLog.Error($"[BlockmakerClient] {_configurationError}");
                return;
            }

            var isLoopback = parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || parsed.Host == "127.0.0.1" || parsed.Host == "::1" || parsed.Host == "[::1]";
            if (parsed.Scheme != Uri.UriSchemeHttps && !isLoopback)
            {
                _configurationError = "Blockmaker server URL must use HTTPS (HTTP is allowed only for localhost development).";
                BlockmakerLog.Error($"[BlockmakerClient] {_configurationError}");
                return;
            }

            var publicGameId = (config.gameId ?? "").Trim();
            if (publicGameId.Length == 0)
            {
                _configurationError = "Blockmaker public game ID is missing. Copy it from your project's Integration page.";
                BlockmakerLog.Error($"[BlockmakerClient] {_configurationError}");
                return;
            }
            if (publicGameId.StartsWith("sk_", StringComparison.OrdinalIgnoreCase))
            {
                _configurationError = "Blockmaker gameId must be the public game ID, never a server key.";
                BlockmakerLog.Error($"[BlockmakerClient] {_configurationError}");
                return;
            }
            if (publicGameId.Length > 128 || !IsSafePublicGameId(publicGameId))
            {
                _configurationError = "Blockmaker public game ID contains invalid characters. Copy it again from the Integration page.";
                BlockmakerLog.Error($"[BlockmakerClient] {_configurationError}");
                return;
            }

            _baseUrl = parsed.GetLeftPart(UriPartial.Authority);
            _gameId = publicGameId;
            BlockmakerLog.Info($"[BlockmakerClient] Initialized — server: {_baseUrl}, game: {_gameId}");
        }

        private static bool IsSafePublicGameId(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
                    || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.')
                    continue;
                return false;
            }
            return value.Length > 0;
        }

        private bool RequireReady(Action<string> onError)
        {
            if (_baseUrl != null && _gameId != null) return true;
            onError?.Invoke(_configurationError ?? "Blockmaker is not configured. Please restart the game and try again.");
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // EMAIL AUTH
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Ask the server to send an OTP to the given email.</summary>
        public IEnumerator RequestEmailOTP(string email, Action onSent, Action<string> onError)
        {
            if (!RequireReady(onError)) yield break;
            string url  = $"{_baseUrl}/v1/auth/email/request";
            string body = JsonUtility.ToJson(new EmailOTPRequest { email = email, gameId = _gameId });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds, false);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = "Something went wrong. Please check your connection and try again.";
                try
                {
                    var respBody = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(respBody))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(respBody);
                        if (!string.IsNullOrEmpty(parsed.error))
                        {
                            BlockmakerLog.Verbose($"[BlockmakerClient] Server error: {parsed.error}");
                            err = parsed.error;
                        }
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] Email OTP HTTP {req.responseCode}: {req.error}");
                onError?.Invoke(err);
            }
            else
                onSent?.Invoke();
        }

        /// <summary>Verify an OTP and receive a session token + managed wallet address.</summary>
        public IEnumerator VerifyEmailOTP(
            string email, string otp,
            Action<EmailVerifyResult> onSuccess,
            Action<string>            onError)
        {
            if (!RequireReady(onError)) yield break;
            string url  = $"{_baseUrl}/v1/auth/email/verify";
            string body = JsonUtility.ToJson(new EmailVerifyRequest { email = email, otp = otp, gameId = _gameId });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds, false);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // MAGIC AUTH
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send Magic's DID token to the server for verification.
        /// Server verifies the token with Magic's admin SDK, creates or finds
        /// the player account, and returns a JWT + Algorand address.
        /// </summary>
        public IEnumerator VerifyMagicToken(
            string                    didToken,
            string                    email,
            Action<EmailVerifyResult> onSuccess,
            Action<string>            onError)
        {
            if (!RequireReady(onError)) yield break;
            string url  = $"{_baseUrl}/v1/auth/magic/verify";
            string body = JsonUtility.ToJson(new MagicVerifyRequest { didToken = didToken, email = email, gameId = _gameId });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds, false);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // WALLET-SIGNATURE AUTH (self-custody tier)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ask the server for a single-use challenge to sign with a self-custody wallet.
        /// chain is "algorand" (Pera/Defly) or "evm" (xChain); pass the EVM signer
        /// address for the "evm" path (null for "algorand").
        /// </summary>
        public IEnumerator RequestWalletChallenge(
            string walletAddress, string chain, string evmAddress,
            Action<WalletChallengeResult> onSuccess,
            Action<string>                onError)
        {
            if (!RequireReady(onError)) yield break;
            string url  = $"{_baseUrl}/v1/auth/wallet/challenge";
            string body = JsonUtility.ToJson(new WalletChallengeRequest
                { walletAddress = walletAddress, chain = chain, evmAddress = evmAddress, gameId = _gameId });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds, false);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        /// <summary>
        /// Submit a wallet proof-of-ownership and receive a player session token +
        /// refresh token (same shape as email/magic verify).
        /// <para>algorand (Pera/Defly): pass <paramref name="signedTxn"/> (base64 of the
        /// signed 0-amount self-payment whose note == nonce) and null for
        /// <paramref name="signature"/>. evm (xChain): pass the personal_sign
        /// <paramref name="signature"/> hex and null for <paramref name="signedTxn"/>.</para>
        /// </summary>
        public IEnumerator VerifyWalletSignature(
            string walletAddress, string chain, string signature, string signedTxn, string nonce, string evmAddress,
            Action<EmailVerifyResult> onSuccess,
            Action<string>            onError)
        {
            if (!RequireReady(onError)) yield break;
            string url  = $"{_baseUrl}/v1/auth/wallet/verify";
            string body = JsonUtility.ToJson(new WalletVerifyRequest
                { walletAddress = walletAddress, chain = chain, signature = signature, signedTxn = signedTxn, nonce = nonce, evmAddress = evmAddress, gameId = _gameId });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds, false);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // SERVER-SIDE SIGNING (Email tier)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ask the Blockmaker server to sign a transaction using the player's
        /// managed wallet. Only valid for Email tier identities.
        /// </summary>
        public IEnumerator SignTransactionServerSide(
            string         unsignedTxnBase64,
            string         sessionToken,
            Action<string> onSigned,
            Action<string> onError,
            Action<BlockmakerError> onBlockmakerError = null)
        {
            if (!RequireReady(onError)) yield break;
            var txnGroup = new[] { unsignedTxnBase64 };
            var signingIntent = FindSigningIntent(txnGroup);
            if (string.IsNullOrEmpty(signingIntent))
            {
                const string message = "This transaction request is missing its Blockmaker authorization. Start the action again from the game.";
                onBlockmakerError?.Invoke(new BlockmakerError("TX_INTENT_REQUIRED", message, 403));
                onError?.Invoke(message);
                yield break;
            }

            string url  = $"{_baseUrl}/v1/auth/sign";
            string body = JsonUtility.ToJson(new ServerSignRequest
                { unsignedTxnBase64 = unsignedTxnBase64, signingIntent = signingIntent, gameId = _gameId });

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.longRequestTimeoutSeconds)
            };
            ApplyCommonHeaders(req, true);
            req.SetRequestHeader("Authorization", $"Bearer {sessionToken}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = "Something went wrong. Please try again.";
                string code = "";
                string requestId = req.GetResponseHeader("X-Request-ID") ?? "";
                try
                {
                    var respBody = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(respBody))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(respBody);
                        if (!string.IsNullOrEmpty(parsed.error))
                        {
                            BlockmakerLog.Verbose($"[BlockmakerClient] Server error: {parsed.error}");
                            err = parsed.error;
                        }
                        code = parsed.code ?? "";
                        if (!string.IsNullOrEmpty(parsed.requestId)) requestId = parsed.requestId;
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] Sign HTTP {req.responseCode}: {req.error}");
                onBlockmakerError?.Invoke(new BlockmakerError(code, err, (int)req.responseCode, requestId, RetryAfterSeconds(req)));
                onError?.Invoke(err);
                yield break;
            }

            try
            {
                var result = JsonUtility.FromJson<ServerSignResult>(req.downloadHandler.text);
                if (result == null || !result.success || string.IsNullOrEmpty(result.signedTxnBase64))
                {
                    onError?.Invoke(result?.error ?? "The transaction could not be signed. Start the action again from the game.");
                    yield break;
                }
                ForgetSigningIntent(txnGroup);
                onSigned?.Invoke(result.signedTxnBase64);
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[BlockmakerClient] Sign parse error: {e.Message}");
                onError?.Invoke("Something went wrong. Please try again.");
            }
        }

        public IEnumerator SignTransactionsServerSide(
            string[]         unsignedTxnsBase64,
            string           sessionToken,
            Action<string[]> onSigned,
            Action<string>   onError,
            Action<BlockmakerError> onBlockmakerError = null)
        {
            if (!RequireReady(onError)) yield break;
            var signingIntent = FindSigningIntent(unsignedTxnsBase64);
            if (string.IsNullOrEmpty(signingIntent))
            {
                const string message = "This transaction request is missing its Blockmaker authorization. Start the action again from the game.";
                onBlockmakerError?.Invoke(new BlockmakerError("TX_INTENT_REQUIRED", message, 403));
                onError?.Invoke(message);
                yield break;
            }

            string url  = $"{_baseUrl}/v1/auth/sign";
            string body = JsonUtility.ToJson(new ServerSignRequest
                { unsignedTxnsBase64 = unsignedTxnsBase64, signingIntent = signingIntent, gameId = _gameId });

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.longRequestTimeoutSeconds)
            };
            ApplyCommonHeaders(req, true);
            req.SetRequestHeader("Authorization", $"Bearer {sessionToken}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = "Something went wrong. Please try again.";
                string code = "";
                string requestId = req.GetResponseHeader("X-Request-ID") ?? "";
                try
                {
                    var respBody = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(respBody))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(respBody);
                        if (!string.IsNullOrEmpty(parsed.error))
                        {
                            BlockmakerLog.Verbose($"[BlockmakerClient] Server error: {parsed.error}");
                            err = parsed.error;
                        }
                        code = parsed.code ?? "";
                        if (!string.IsNullOrEmpty(parsed.requestId)) requestId = parsed.requestId;
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] Sign HTTP {req.responseCode}: {req.error}");
                onBlockmakerError?.Invoke(new BlockmakerError(code, err, (int)req.responseCode, requestId, RetryAfterSeconds(req)));
                onError?.Invoke(err);
                yield break;
            }

            try
            {
                var result = JsonUtility.FromJson<ServerSignResult>(req.downloadHandler.text);
                if (result == null || !result.success || result.signedTxnsBase64 == null || result.signedTxnsBase64.Length == 0)
                {
                    onError?.Invoke(result?.error ?? "The transactions could not be signed. Start the action again from the game.");
                    yield break;
                }
                ForgetSigningIntent(unsignedTxnsBase64);
                onSigned?.Invoke(result.signedTxnsBase64);
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[BlockmakerClient] Sign parse error: {e.Message}");
                onError?.Invoke("Something went wrong. Please try again.");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // REWARDS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send a reward from the game treasury wallet.
        /// Only available in the Unity Editor — never in player builds.
        /// A player who extracts the API key from a build can call the rewards
        /// endpoint directly and drain your treasury.
        /// For production, call the rewards endpoint from your own trusted server.
        /// </summary>
        /// <param name="contextId">
        /// Idempotency key. Pass a STABLE id you own for this logical reward (e.g.
        /// "{raceId}:{wallet}:{reason}") and reuse the SAME value on any retry — the
        /// server then dedups a retried send and never double-pays. Leave null and the
        /// SDK mints a fresh key per call (protects only this call, not a caller-level retry).
        /// </param>
        public void SendReward(
            string               recipientWallet,
            long                 amountMicroAlgo,
            string               reason    = "reward",
            long                 assetId   = 0,
            string               contextId = null,
            Action<RewardResult> onSuccess = null,
            Action<string>       onError   = null)
        {
    #if !UNITY_EDITOR
            BlockmakerLog.Error("[Blockmaker] SendReward is disabled in player builds — use a trusted server to send rewards.");
            onError?.Invoke("This action is not available right now.");
            return;
    #else
            StartCoroutine(PostJson<RewardResult>(
                $"{_baseUrl}/v1/rewards/send",
                new RewardRequest
                {
                    recipientWallet = recipientWallet,
                    assetId         = assetId,
                    amountMicroAlgo = amountMicroAlgo,
                    reason          = reason,
                    // Stable key dedups retries; mint one if the caller didn't supply it.
                    contextId       = string.IsNullOrEmpty(contextId)
                        ? System.Guid.NewGuid().ToString("N")
                        : contextId
                },
                config.longRequestTimeoutSeconds,
                onSuccess, onError
            ));
    #endif
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // RACE RESULTS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>POST JSON to a server path. Use for game-specific endpoints.</summary>
        public void Post<TReq, TRes>(string path, TReq body, Action<TRes> onSuccess = null, Action<string> onError = null) where TRes : class
        {
            Post(path, body, config.defaultTimeoutSeconds, onSuccess, onError);
        }

        /// <summary>
        /// POST JSON to a server path with an explicit timeout. Use a longer timeout (e.g.
        /// <c>config.walletTimeoutSeconds</c>) for endpoints that scan a whole wallet — large
        /// wallets can take well over the 10s default to enumerate on-chain.
        /// </summary>
        public void Post<TReq, TRes>(string path, TReq body, float timeoutSeconds, Action<TRes> onSuccess = null, Action<string> onError = null) where TRes : class
        {
            if (!RequireReady(onError)) return;
            string url;
            if (!TryBuildApiUrl(path, out url))
            {
                onError?.Invoke("Blockmaker API paths must start with /v1/ and stay on the configured server.");
                return;
            }
            StartCoroutine(PostJsonAuth<TRes>(
                url, body,
                timeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>GET JSON from a server path (auto-appends wallet). Use for game-specific endpoints.</summary>
        public void Get<TRes>(string path, Action<TRes> onSuccess, Action<string> onError = null) where TRes : class
        {
            if (!RequireReady(onError)) return;
            string baseRequestUrl;
            if (!TryBuildApiUrl(path, out baseRequestUrl))
            {
                onError?.Invoke("Blockmaker API paths must start with /v1/ and stay on the configured server.");
                return;
            }
            string wallet = UnityWebRequest.EscapeURL(BlockmakerAuth.Instance?.Address ?? "");
            string sep = path.Contains("?") ? "&" : "?";
            StartCoroutine(GetJson<TRes>(
                $"{baseRequestUrl}{sep}wallet={wallet}",
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // TRANSACTION BUILDER
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Build an unsigned payment transaction via the server.</summary>
        public void BuildPayment(
            string recipient, long amountMicroAlgo, string note = null,
            Action<BuildTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<BuildTransactionResult>(
                $"{_baseUrl}/v1/transactions/build",
                new BuildTransactionRequest { type = "payment", recipient = recipient, amount = amountMicroAlgo, note = note ?? "", walletAddress = BlockmakerAuth.Instance?.Address ?? "" },
                config.defaultTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>Build an unsigned ASA transfer transaction via the server.</summary>
        public void BuildAssetTransfer(
            string recipient, long assetId, long amount, string note = null,
            Action<BuildTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<BuildTransactionResult>(
                $"{_baseUrl}/v1/transactions/build",
                new BuildTransactionRequest { type = "asset_transfer", recipient = recipient, amount = amount, assetId = assetId, note = note ?? "", walletAddress = BlockmakerAuth.Instance?.Address ?? "" },
                config.defaultTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>Build an unsigned ASA opt-in transaction (0-amount self-transfer).</summary>
        public void BuildAssetOptIn(
            long assetId,
            Action<BuildTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<BuildTransactionResult>(
                $"{_baseUrl}/v1/transactions/build",
                new BuildTransactionRequest { type = "asset_optin", assetId = assetId, walletAddress = BlockmakerAuth.Instance?.Address ?? "" },
                config.defaultTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>
        /// Build an ATOMIC GROUP of unsigned 0-amount ASA opt-ins (one per assetId), all
        /// tied together with a shared Algorand GroupID via the server's assignGroupID.
        /// Sign the returned array as ONE group so an xChain (EVM) LogicSig produces a
        /// SINGLE signature over the shared GroupID (instead of one prompt per asset).
        /// Algorand caps a group at 16 txns. Managed email wallets have a stricter
        /// server-signing cap of 5, so chunk assetIds at 5 when that tier must work.
        /// onSuccess receives the grouped unsigned txns (base64 msgpack).
        /// </summary>
        public void BuildAssetOptInGroup(long[] assetIds, Action<string[]> onSuccess, Action<string> onError)
        {
            StartCoroutine(PostJsonAuth<BuildAssetOptInGroupResult>(
                $"{_baseUrl}/v1/transactions/build-optin-group",
                new BuildAssetOptInGroupRequest { assetIds = assetIds, walletAddress = BlockmakerAuth.Instance?.Address ?? "" },
                config.defaultTimeoutSeconds,
                res =>
                {
                    if (res != null && res.success && res.unsignedTxnsBase64 != null)
                        onSuccess?.Invoke(res.unsignedTxnsBase64);
                    else
                        onError?.Invoke(res?.error ?? "Could not build the opt-in group. Please try again.");
                },
                onError));
        }

        /// <summary>Submit a signed transaction to the Algorand network via the server.</summary>
        public void SubmitTransaction(
            string signedTxnBase64,
            Action<SubmitTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<SubmitTransactionResult>(
                $"{_baseUrl}/v1/transactions/submit",
                new SubmitTransactionRequest { signedTxnBase64 = signedTxnBase64 },
                config.longRequestTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>Submit signed group transactions to the Algorand network via the server.</summary>
        public void SubmitTransactions(
            string[] signedTxnsBase64,
            Action<SubmitTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<SubmitTransactionResult>(
                $"{_baseUrl}/v1/transactions/submit",
                new SubmitTransactionRequest { signedTxnsBase64 = signedTxnsBase64 },
                config.longRequestTimeoutSeconds, onSuccess, onError));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // SERVER HEALTH
        // ═══════════════════════════════════════════════════════════════════════════

        public void VerifyConnection(Action<bool> onResult)
        {
            if (!RequireReady(_ => onResult?.Invoke(false))) return;
            StartCoroutine(VerifyConnectionRoutine(onResult));
        }

        private IEnumerator VerifyConnectionRoutine(Action<bool> onResult)
        {
            using var req = BuildGet($"{_baseUrl}/v1/me", config.defaultTimeoutSeconds);
            yield return req.SendWebRequest();
            onResult?.Invoke(req.result == UnityWebRequest.Result.Success);
        }

        /// <summary>
        /// Verify a specific JWT session token against the server.
        /// Used during Magic session restore to confirm the server-side JWT
        /// is still valid even when the Magic JS session has expired.
        /// </summary>
        public void VerifySessionToken(string sessionToken, Action<bool> onResult)
        {
            if (!RequireReady(_ => onResult?.Invoke(false))) return;
            StartCoroutine(VerifySessionTokenRoutine(sessionToken, onResult));
        }

        private IEnumerator VerifySessionTokenRoutine(string sessionToken, Action<bool> onResult)
        {
            using var req = new UnityWebRequest($"{_baseUrl}/v1/auth/session", "GET")
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.defaultTimeoutSeconds)
            };
            ApplyCommonHeaders(req, false);
            req.SetRequestHeader("Authorization", $"Bearer {sessionToken}");
            yield return req.SendWebRequest();
            onResult?.Invoke(req.result == UnityWebRequest.Result.Success);
        }

        /// <summary>
        /// Exchange a refresh token for a new JWT + rotated refresh token.
        /// Call on session restore to get a fresh short-lived JWT.
        /// </summary>
        public void RefreshToken(string refreshToken, Action<RefreshTokenResult> onSuccess, Action<string> onError = null)
        {
            if (!RequireReady(onError)) return;
            refreshToken = (refreshToken ?? "").Trim();
            if (refreshToken.Length == 0)
            {
                onError?.Invoke("Your session has expired. Please sign in again.");
                return;
            }

            if (_refreshInProgress)
            {
                if (!string.Equals(_refreshTokenInFlight, refreshToken, StringComparison.Ordinal))
                {
                    onError?.Invoke("A different player session is already being refreshed. Please try again.");
                    return;
                }
                _refreshWaiters.Add(new RefreshWaiter { onSuccess = onSuccess, onError = onError });
                return;
            }

            _refreshInProgress = true;
            _refreshTokenInFlight = refreshToken;
            _refreshWaiters.Add(new RefreshWaiter { onSuccess = onSuccess, onError = onError });
            StartCoroutine(RefreshTokenRoutine(refreshToken));
        }

        private IEnumerator RefreshTokenRoutine(string refreshToken)
        {
            var body = JsonUtility.ToJson(new RefreshTokenRequest { refreshToken = refreshToken, gameId = _gameId });
            using var req = new UnityWebRequest($"{_baseUrl}/v1/auth/refresh", "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.defaultTimeoutSeconds)
            };
            ApplyCommonHeaders(req, true);
            yield return req.SendWebRequest();
            HandleResponse<RefreshTokenResult>(req,
                result =>
                {
                    if (result == null || !result.success || string.IsNullOrEmpty(result.sessionToken) || string.IsNullOrEmpty(result.refreshToken))
                        CompleteRefresh(null, "Your session could not be refreshed. Please sign in again.");
                    else
                        CompleteRefresh(result, null);
                },
                error => CompleteRefresh(null, error));
        }

        private void CompleteRefresh(RefreshTokenResult result, string error)
        {
            var waiters = _refreshWaiters.ToArray();
            _refreshWaiters.Clear();
            _refreshInProgress = false;
            _refreshTokenInFlight = null;

            foreach (var waiter in waiters)
            {
                try
                {
                    if (result != null) waiter.onSuccess?.Invoke(result);
                    else waiter.onError?.Invoke(error ?? "Your session could not be refreshed. Please sign in again.");
                }
                catch (Exception callbackError)
                {
                    BlockmakerLog.Warning($"[BlockmakerClient] Refresh callback failed: {callbackError.Message}");
                }
            }
        }

        /// <summary>
        /// Invalidate a refresh token on the server (logout).
        /// Fire-and-forget — does not report errors.
        /// </summary>
        public void ServerLogout(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return;
            if (!RequireReady(null)) return;
            StartCoroutine(ServerLogoutRoutine(refreshToken));
        }

        private IEnumerator ServerLogoutRoutine(string refreshToken)
        {
            var body = JsonUtility.ToJson(new LogoutRequest { refreshToken = refreshToken, gameId = _gameId });
            using var req = new UnityWebRequest($"{_baseUrl}/v1/auth/logout", "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.defaultTimeoutSeconds)
            };
            ApplyCommonHeaders(req, true);
            yield return req.SendWebRequest();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        private string ProfileUrl(string path)
        {
            string url = $"{_baseUrl}{path}";
            string wallet = BlockmakerAuth.Instance?.Address;
            if (!string.IsNullOrEmpty(wallet))
                url += (url.Contains("?") ? "&" : "?") + $"wallet={UnityWebRequest.EscapeURL(wallet)}";
            return url;
        }

        // PROFILE
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Fetch the current player's profile + game data.</summary>
        public void GetProfile(Action<ProfileResponse> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(GetJson<ProfileResponse>(
                ProfileUrl("/v1/profile"),
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Fetch all ASA holdings for the authenticated wallet.</summary>
        public void GetHoldings(Action<HoldingsResponse> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(GetJson<HoldingsResponse>(
                ProfileUrl("/v1/profile/holdings"),
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Check if the wallet holds NFTs from the given creator addresses.</summary>
        public void CheckCollections(
            string[]                           creators,
            Action<CollectionCheckResponse>    onSuccess,
            Action<string>                     onError = null)
        {
            StartCoroutine(PostJsonAuth<CollectionCheckResponse>(
                ProfileUrl("/v1/profile/collection-check"),
                new CollectionCheckRequest { creators = creators },
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Fetch the full asset registry (NFT collections and tokens).</summary>
        public void GetRegistry(Action<AssetRegistryResponse> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(GetJson<AssetRegistryResponse>(
                $"{_baseUrl}/v1/profile/registry",
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Check if a username is available. Set isChange=true for change flow pricing.</summary>
        public void CheckUsername(
            string                     username,
            Action<UsernameCheckResult> onSuccess,
            Action<string>              onError = null,
            bool                        isChange = false)
        {
            StartCoroutine(PostJsonAuth<UsernameCheckResult>(
                ProfileUrl("/v1/profile/username/check"),
                new UsernameCheckRequest
                {
                    username      = username,
                    walletAddress = BlockmakerAuth.Instance?.Address,
                    isChange      = isChange
                },
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Step 1 of username claim: server validates, reserves the username,
        /// pins ARC-3 metadata to IPFS, and returns an unsigned AssetCreateTxn.
        /// </summary>
        public void PrepareUsernameClaim(
            string                       username,
            Action<UsernamePrepareResult> onSuccess,
            Action<string>                onError = null)
        {
            StartCoroutine(PostJsonAuth<UsernamePrepareResult>(
                ProfileUrl("/v1/profile/username/claim/prepare"),
                new UsernameClaimPrepareRequest { username = username },
                config.longRequestTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Step 2 of username claim: send signed group txns; server broadcasts,
        /// confirms, and activates the username on-chain.
        /// </summary>
        public void CompleteUsernameClaim(
            string                     reservationId,
            string[]                   signedTxnsBase64,
            Action<UsernameClaimResult> onSuccess,
            Action<string>              onError = null)
        {
            StartCoroutine(PostJsonAuth<UsernameClaimResult>(
                ProfileUrl("/v1/profile/username/claim/complete"),
                new UsernameCompleteRequest
                {
                    reservationId    = reservationId,
                    signedTxnsBase64 = signedTxnsBase64,
                },
                config.longRequestTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Step 1 of username change: builds atomic 3-txn group
        /// (deregister old + payment + register new).
        /// </summary>
        public void PrepareUsernameChange(
            string                       newUsername,
            Action<UsernamePrepareResult> onSuccess,
            Action<string>                onError = null)
        {
            StartCoroutine(PostJsonAuth<UsernamePrepareResult>(
                ProfileUrl("/v1/profile/username/change/prepare"),
                new UsernameChangePrepareRequest { newUsername = newUsername },
                config.longRequestTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Step 2 of username change: broadcast signed group, server confirms.</summary>
        public void CompleteUsernameChange(
            string                     reservationId,
            string[]                   signedTxnsBase64,
            Action<UsernameClaimResult> onSuccess,
            Action<string>              onError = null)
        {
            StartCoroutine(PostJsonAuth<UsernameClaimResult>(
                ProfileUrl("/v1/profile/username/change/complete"),
                new UsernameCompleteRequest
                {
                    reservationId    = reservationId,
                    signedTxnsBase64 = signedTxnsBase64,
                },
                config.longRequestTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Set the player's profile picture to an NFT they own.
        /// The server verifies ownership via the Algorand indexer, resolves the
        /// ARC-3/ARC-69 image URL, and persists both on the profile.
        /// Endpoint: POST /v1/profile/pfp
        /// </summary>
        public void SetProfilePicNft(
            long                       assetId,
            Action<SetProfilePicResult> onSuccess,
            Action<string>              onError = null)
        {
            StartCoroutine(PostJsonAuth<SetProfilePicResult>(
                ProfileUrl("/v1/profile/pfp"),
                new SetProfilePicRequest { assetId = assetId },
                config.longRequestTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Search the current player's wallet for NFTs.
        /// The server queries the Algorand indexer for all ASAs held by the wallet,
        /// filters by the search term (matches name or asset ID), and returns
        /// name + assetId + imageUrl for each match.
        /// Endpoint: POST /v1/profile/wallet/nfts
        /// </summary>
        public void SearchWalletNFTs(
            string                          searchTerm,
            Action<WalletNFTSearchResult>   onSuccess,
            Action<string>                  onError = null)
        {
            StartCoroutine(PostJsonAuth<WalletNFTSearchResult>(
                ProfileUrl("/v1/profile/wallet/nfts"),
                new WalletNFTSearchRequest { search = searchTerm ?? "" },
                config.walletTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Resolve the image URL for a single NFT (preview before setting as pfp).
        /// Endpoint: POST /v1/profile/nft/image
        /// </summary>
        public void GetNftImageUrl(
            long                    assetId,
            Action<NftImageResult>  onSuccess,
            Action<string>          onError = null)
        {
            StartCoroutine(PostJsonAuth<NftImageResult>(
                ProfileUrl("/v1/profile/nft/image"),
                new NftImageRequest { assetId = assetId },
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>
        /// Set the player's profile picture to a built-in default avatar.
        /// No NFT ownership required.
        /// Endpoint: POST /v1/profile/pfp/default
        /// </summary>
        public void SetDefaultAvatar(
            string                     avatarId,
            Action<SetProfilePicResult> onSuccess,
            Action<string>              onError = null)
        {
            StartCoroutine(PostJsonAuth<SetProfilePicResult>(
                ProfileUrl("/v1/profile/pfp/default"),
                new SetDefaultAvatarRequest { avatarId = avatarId },
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Upload a profile image. Pass raw image bytes and MIME type.</summary>
        public IEnumerator UploadProfileImage(
            byte[]                      imageBytes,
            string                      mimeType,
            string                      filename,
            Action<ProfileImageResult>  onSuccess,
            Action<string>              onError = null)
        {
            if (!RequireReady(onError)) yield break;
            string url = ProfileUrl("/v1/profile/image");
            var form   = new WWWForm();
            form.AddBinaryData("image", imageBytes, filename, mimeType);

            using var req = UnityWebRequest.Post(url, form);
            req.timeout = SafeTimeout(config.longRequestTimeoutSeconds);
            ApplyCommonHeaders(req, false);
            var token = GetSessionToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");

            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        /// <summary>Fetch onboarding status and wallet balances for the current player.</summary>
        public void GetOnboardingStatus(
            Action<OnboardingStatus> onSuccess,
            Action<string>           onError = null)
        {
            StartCoroutine(GetJson<OnboardingStatus>(
                ProfileUrl("/v1/profile/onboarding-status"),
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>Advance the player's onboarding step.</summary>
        public void AdvanceOnboardingStep(
            string         step,
            Action         onSuccess = null,
            Action<string> onError   = null)
        {
            StartCoroutine(PostJsonAuth<ProfileResponse>(
                ProfileUrl("/v1/profile/onboarding/advance"),
                new OnboardingAdvanceRequest { step = step },
                config.defaultTimeoutSeconds,
                _ => onSuccess?.Invoke(), onError
            ));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // CONVENIENCE METHODS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>True when a wallet is connected and the SDK can make authenticated requests.</summary>
        public bool IsConnected => BlockmakerAuth.Instance != null && BlockmakerAuth.Instance.HasWallet;

        /// <summary>Check whether the current player holds a specific asset (any amount > 0).</summary>
        public void OwnsAsset(long assetId, Action<bool> onResult, Action<string> onError = null)
        {
            GetHoldings(result =>
            {
                bool owns = result?.holdings != null && result.holdings.Exists(h => h.assetId == assetId && h.amount > 0);
                onResult?.Invoke(owns);
            }, onError);
        }

        /// <summary>Get the balance of a specific ASA for the current player.</summary>
        public void GetAssetBalance(long assetId, Action<long> onResult, Action<string> onError = null)
        {
            GetHoldings(result =>
            {
                long balance = 0;
                if (result?.holdings != null)
                {
                    var holding = result.holdings.Find(h => h.assetId == assetId);
                    if (holding != null) balance = holding.amount;
                }
                onResult?.Invoke(balance);
            }, onError);
        }

        /// <summary>Check whether the current player is opted into a specific ASA.</summary>
        public void IsOptedIn(long assetId, Action<bool> onResult, Action<string> onError = null)
        {
            GetHoldings(result =>
            {
                bool optedIn = result?.holdings != null && result.holdings.Exists(h => h.assetId == assetId);
                onResult?.Invoke(optedIn);
            }, onError);
        }

        /// <summary>Build, sign, and submit a payment transaction in one call.</summary>
        public void SendPayment(string recipient, long amountMicroAlgo, Action<string> onTxId, Action<string> onError = null, string note = null)
        {
            BuildPayment(recipient, amountMicroAlgo, note, buildResult =>
            {
                if (!buildResult.success) { onError?.Invoke(buildResult.error ?? "Something went wrong while preparing your payment. Please try again."); return; }
                var txnB64 = buildResult.unsignedTxnBase64;
                var identity = BlockmakerAuth.Instance?.Identity;
                if (identity == null || !identity.CanSign) { onError?.Invoke("No wallet connected. Please connect a wallet or sign in first."); return; }
                StartCoroutine(SignAndSubmit(txnB64, onTxId, onError));
            }, onError);
        }

        /// <summary>Build, sign, and submit an ASA opt-in transaction in one call.</summary>
        public void OptInToAsset(long assetId, Action<string> onTxId, Action<string> onError = null)
        {
            BuildAssetOptIn(assetId, buildResult =>
            {
                if (!buildResult.success) { onError?.Invoke(buildResult.error ?? "Something went wrong. Please try again."); return; }
                var txnB64 = buildResult.unsignedTxnBase64;
                var identity = BlockmakerAuth.Instance?.Identity;
                if (identity == null || !identity.CanSign) { onError?.Invoke("No wallet connected. Please connect a wallet or sign in first."); return; }
                StartCoroutine(SignAndSubmit(txnB64, onTxId, onError));
            }, onError);
        }

        private IEnumerator SignAndSubmit(string unsignedTxnBase64, Action<string> onTxId, Action<string> onError)
        {
            var identity = BlockmakerAuth.Instance?.Identity;
            string signedTxn = null;
            string signError = null;
            bool signDone = false;

            StartCoroutine(identity.SignTransaction(unsignedTxnBase64, signed =>
            {
                signedTxn = signed;
                signDone = true;
            }, err =>
            {
                signError = err;
                signDone = true;
            }));

            while (!signDone) yield return null;

            if (!string.IsNullOrEmpty(signError)) { onError?.Invoke(signError); yield break; }
            if (string.IsNullOrEmpty(signedTxn)) { onError?.Invoke("Something went wrong. Please try again."); yield break; }

            SubmitTransaction(signedTxn, result =>
            {
                if (result.success)
                    onTxId?.Invoke(result.txId);
                else
                    onError?.Invoke(result.error ?? "Something went wrong while completing your payment. Please try again.");
            }, onError);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // HTTP HELPERS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the best available auth token for the current identity:
        /// JWT session token for Email/Magic tiers, API key in Editor only.
        /// In player builds, returns empty string if no JWT is available —
        /// the server API key must never be shipped in client builds.
        /// </summary>
        /// True when a request would carry real backend auth (player JWT — or the
        /// dev API key in the editor). Guests get false in builds: use this to skip
        /// best-effort backend calls that would otherwise just spam 401s.
        public bool HasBackendSession => !string.IsNullOrEmpty(GetSessionToken());

        private string GetSessionToken()
        {
            var identity = BlockmakerAuth.Instance?.Identity;
            if (identity is ServerSignedIdentity ss && !string.IsNullOrEmpty(ss.SessionToken))
                return ss.SessionToken;
            if (identity is MagicIdentity magic && !string.IsNullOrEmpty(magic.SessionToken))
                return magic.SessionToken;
            if (identity is WalletConnectIdentity wc && !string.IsNullOrEmpty(wc.SessionToken))
                return wc.SessionToken;
            if (identity is EvmXChainIdentity evm && !string.IsNullOrEmpty(evm.SessionToken))
                return evm.SessionToken;
    #if UNITY_EDITOR
            return config?.apiKey ?? "";
    #else
            return "";
    #endif
        }

        /// <summary>
        /// Returns the auth token for server-to-server calls (Editor/testing only)
        /// or the session token in player builds.
        /// </summary>
        private string GetAuthHeader()
        {
            var session = GetSessionToken();
            if (!string.IsNullOrEmpty(session))
                return session;
    #if UNITY_EDITOR
            return config?.apiKey ?? "";
    #else
            return "";
    #endif
        }

        private IEnumerator PostJsonAuth<T>(
            string url, object payload, float timeout,
            Action<T> onSuccess, Action<string> onError) where T : class
        {
            if (!RequireReady(onError)) yield break;
            string body = JsonUtility.ToJson(payload);
            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(timeout)
            };
            ApplyCommonHeaders(req, true);
            var token = GetSessionToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        private IEnumerator PostJson<T>(
            string url, object payload, float timeout,
            Action<T> onSuccess, Action<string> onError) where T : class
        {
            if (!RequireReady(onError)) yield break;
            string body = JsonUtility.ToJson(payload);
            using var req = BuildPost(url, body, timeout);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        private IEnumerator GetJson<T>(
            string url, float timeout,
            Action<T> onSuccess, Action<string> onError) where T : class
        {
            if (!RequireReady(onError)) yield break;
            using var req = BuildGet(url, timeout);
            // Override with session token so JWT-auth players work on profile endpoints
            var token = GetSessionToken();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        private static int SafeTimeout(float seconds)
        {
            return Mathf.Max(1, Mathf.RoundToInt(seconds));
        }

        private UnityWebRequest BuildPost(string url, string jsonBody, float timeout, bool includeAuth = true)
        {
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(timeout)
            };
            ApplyCommonHeaders(req, true);
            if (includeAuth)
            {
                var token = GetAuthHeader();
                if (!string.IsNullOrEmpty(token))
                    req.SetRequestHeader("Authorization", $"Bearer {token}");
            }
            return req;
        }

        private UnityWebRequest BuildGet(string url, float timeout)
        {
            var req = UnityWebRequest.Get(url);
            req.timeout = SafeTimeout(timeout);
            ApplyCommonHeaders(req, false);
            var token = GetAuthHeader();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            return req;
        }

        private void ApplyCommonHeaders(UnityWebRequest req, bool jsonContent)
        {
            if (jsonContent) req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(_gameId)) req.SetRequestHeader("X-Blockmaker-Game", _gameId);
        }

        private bool TryBuildApiUrl(string path, out string url)
        {
            url = null;
            if (_baseUrl == null || string.IsNullOrEmpty(path)
                || !path.StartsWith("/v1/", StringComparison.Ordinal)
                || path.IndexOf('#') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0
                || path.IndexOf("..", StringComparison.Ordinal) >= 0
                || path.IndexOf("://", StringComparison.Ordinal) >= 0)
                return false;
            url = _baseUrl + path;
            return true;
        }

        private static long UnixTimeMilliseconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static string SigningIntentKey(string[] unsignedTxnsBase64)
        {
            if (unsignedTxnsBase64 == null || unsignedTxnsBase64.Length == 0) return null;
            var canonical = new StringBuilder();
            foreach (var txn in unsignedTxnsBase64)
            {
                if (string.IsNullOrEmpty(txn)) return null;
                canonical.Append(txn.Length).Append(':').Append(txn).Append(';');
            }
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
        }

        private void RememberSigningIntent(string token, long expiresAt, string[] unsignedTxnsBase64)
        {
            var key = SigningIntentKey(unsignedTxnsBase64);
            var now = UnixTimeMilliseconds();
            if (key == null || string.IsNullOrWhiteSpace(token) || expiresAt <= now) return;
            if (_signingIntents.Count >= 64)
            {
                var expiredKeys = new List<string>();
                foreach (var entry in _signingIntents)
                    if (entry.Value.expiresAt <= now) expiredKeys.Add(entry.Key);
                foreach (var expiredKey in expiredKeys) _signingIntents.Remove(expiredKey);
            }
            if (_signingIntents.Count >= 128)
            {
                string oldestKey = null;
                long oldestExpiry = long.MaxValue;
                foreach (var entry in _signingIntents)
                {
                    if (entry.Value.expiresAt >= oldestExpiry) continue;
                    oldestKey = entry.Key;
                    oldestExpiry = entry.Value.expiresAt;
                }
                if (oldestKey != null) _signingIntents.Remove(oldestKey);
            }
            _signingIntents[key] = new CachedSigningIntent { token = token.Trim(), expiresAt = expiresAt };
        }

        private void TryRememberSigningIntent(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"signingIntent\"", StringComparison.Ordinal) < 0) return;
            try
            {
                var envelope = JsonUtility.FromJson<SigningIntentEnvelope>(json);
                if (envelope == null || string.IsNullOrEmpty(envelope.signingIntent)) return;
                string[] txns = null;
                if (envelope.unsignedTxnsBase64 != null && envelope.unsignedTxnsBase64.Length > 0)
                    txns = envelope.unsignedTxnsBase64;
                else if (envelope.unsignedTxns != null && envelope.unsignedTxns.Length > 0)
                    txns = envelope.unsignedTxns;
                else if (!string.IsNullOrEmpty(envelope.unsignedTxnBase64))
                    txns = new[] { envelope.unsignedTxnBase64 };
                else if (!string.IsNullOrEmpty(envelope.unsignedOptInTxn))
                    txns = new[] { envelope.unsignedOptInTxn };
                RememberSigningIntent(envelope.signingIntent, envelope.signingIntentExpiresAt, txns);
            }
            catch (Exception parseError)
            {
                BlockmakerLog.Warning($"[BlockmakerClient] Could not read transaction authorization: {parseError.Message}");
            }
        }

        private string FindSigningIntent(string[] unsignedTxnsBase64)
        {
            var key = SigningIntentKey(unsignedTxnsBase64);
            if (key == null) return null;
            CachedSigningIntent cached;
            if (!_signingIntents.TryGetValue(key, out cached)) return null;
            if (cached.expiresAt <= UnixTimeMilliseconds())
            {
                _signingIntents.Remove(key);
                return null;
            }
            return cached.token;
        }

        private void ForgetSigningIntent(string[] unsignedTxnsBase64)
        {
            var key = SigningIntentKey(unsignedTxnsBase64);
            if (key != null) _signingIntents.Remove(key);
        }

        private static int RetryAfterSeconds(UnityWebRequest req)
        {
            var value = req.GetResponseHeader("Retry-After");
            int seconds;
            return int.TryParse(value, out seconds) && seconds > 0 ? seconds : 0;
        }

        private void HandleResponse<T>(
            UnityWebRequest req,
            Action<T>       onSuccess,
            Action<string>  onError,
            Action<BlockmakerError> onBlockmakerError = null,
            bool rememberSigningIntent = true) where T : class
        {
            if (req.result != UnityWebRequest.Result.Success)
            {
                string err;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                    err = "Could not reach the server. Please check your connection and try again.";
                else if ((int)req.responseCode == 401 || (int)req.responseCode == 403)
                    err = "Your session has expired. Please sign in again.";
                else if ((int)req.responseCode >= 500)
                    err = "The server is having trouble right now. Please try again in a moment.";
                else
                    err = "Something went wrong. Please try again.";
                string code = req.result == UnityWebRequest.Result.ConnectionError ? "NETWORK" : "HTTP_ERROR";
                string requestId = req.GetResponseHeader("X-Request-ID") ?? "";
                int httpStatus = (int)req.responseCode;
                try
                {
                    var body = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(body))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(body);
                        if (!string.IsNullOrEmpty(parsed.error))
                        {
                            BlockmakerLog.Verbose($"[BlockmakerClient] Server error: {parsed.error}");
                            err = parsed.error;
                        }
                        if (!string.IsNullOrEmpty(parsed.code))
                            code = parsed.code;
                        if (!string.IsNullOrEmpty(parsed.requestId)) requestId = parsed.requestId;
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] HTTP {req.responseCode}: {req.error}");

                if (onBlockmakerError != null)
                    onBlockmakerError.Invoke(new BlockmakerError(code, err, httpStatus, requestId, RetryAfterSeconds(req)));
                onError?.Invoke(err);
                return;
            }
            try
            {
                var body = req.downloadHandler?.text;
                if (string.IsNullOrEmpty(body))
                {
                    onError?.Invoke("Something went wrong. Please try again.");
                    return;
                }
                if (rememberSigningIntent) TryRememberSigningIntent(body);
                onSuccess?.Invoke(JsonUtility.FromJson<T>(body));
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[BlockmakerClient] JSON parse error: {e.Message}");
                onError?.Invoke("Something went wrong. Please try again.");
            }
        }

    }
}
