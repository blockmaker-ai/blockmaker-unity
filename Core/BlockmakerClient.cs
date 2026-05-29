using System;
using System.Collections;
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
            _baseUrl = config.serverUrl.TrimEnd('/');

            if (string.IsNullOrEmpty(_baseUrl))
            {
                BlockmakerLog.Error("[BlockmakerClient] serverUrl is empty in BlockmakerConfig. " +
                               "Set it to your Blockmaker server URL (e.g. https://your-server.up.railway.app).");
                Instance = null;
                enabled = false;
                return;
            }

            if (config.marketplaceTestMode)
                BlockmakerLog.Warning("[BlockmakerClient] Marketplace test mode is ON — returning mock data.");
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
            if (string.IsNullOrEmpty(config.serverUrl))
            {
                BlockmakerLog.Error("[BlockmakerClient] Server URL is empty in BlockmakerConfig. Please set it in the Inspector.");
                return;
            }
            enabled = true;
            if (Instance == null) Instance = this;
            _baseUrl = config.serverUrl.TrimEnd('/');
            BlockmakerLog.Info($"[BlockmakerClient] Initialized from auth — baseUrl: {_baseUrl}");

            if (config.marketplaceTestMode)
                BlockmakerLog.Warning("[BlockmakerClient] Marketplace test mode is ON — returning mock data.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // FLOW RUNNER
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Run a flow against the current identity's address.</summary>
        public void RunFlow(
            string             flowId,
            Action<FlowResult> onSuccess,
            Action<string>     onError   = null,
            string             contextJson = null)
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null || !auth.HasWallet)
            {
                onError?.Invoke("No wallet connected. Please connect a wallet or sign in first.");
                return;
            }
            StartCoroutine(RunFlowRoutine(flowId, auth.Address, contextJson, onSuccess, onError));
        }

        /// <summary>Run a flow with a custom result type (for game-specific flow data).</summary>
        public void RunFlow<T>(
            string         flowId,
            Action<T>      onSuccess,
            Action<string> onError     = null,
            string         contextJson = null) where T : class
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null || !auth.HasWallet)
            {
                onError?.Invoke("No wallet connected. Please connect a wallet or sign in first.");
                return;
            }
            StartCoroutine(RunFlowRoutine(flowId, auth.Address, contextJson, onSuccess, onError));
        }

        /// <summary>Run a flow against an explicit wallet address.</summary>
        public void RunFlowForWallet(
            string             flowId,
            string             walletAddress,
            Action<FlowResult> onSuccess,
            Action<string>     onError     = null,
            string             contextJson = null)
        {
            if (_baseUrl == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); return; }
            StartCoroutine(RunFlowRoutine(flowId, walletAddress, contextJson, onSuccess, onError));
        }

        private IEnumerator RunFlowRoutine<T>(
            string flowId, string walletAddress, string contextJson,
            Action<T> onSuccess, Action<string> onError) where T : class
        {
            string url  = $"{_baseUrl}/v1/flows/{flowId}/run";
            string body = JsonUtility.ToJson(new FlowRunRequest
                { wallet = walletAddress, context = contextJson });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // EMAIL AUTH
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Ask the server to send an OTP to the given email.</summary>
        public IEnumerator RequestEmailOTP(string email, Action onSent, Action<string> onError)
        {
            string url  = $"{_baseUrl}/v1/auth/email/request";
            string body = JsonUtility.ToJson(new EmailOTPRequest { email = email });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds);
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
                            err = parsed.error;
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
            string url  = $"{_baseUrl}/v1/auth/email/verify";
            string body = JsonUtility.ToJson(new EmailVerifyRequest { email = email, otp = otp });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds);
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
            string url  = $"{_baseUrl}/v1/auth/magic/verify";
            string body = JsonUtility.ToJson(new MagicVerifyRequest { didToken = didToken, email = email });

            using var req = BuildPost(url, body, config.defaultTimeoutSeconds);
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
            string url  = $"{_baseUrl}/v1/auth/sign";
            string body = JsonUtility.ToJson(new ServerSignRequest
                { unsignedTxnBase64 = unsignedTxnBase64 });

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.rewardTimeoutSeconds)
            };
            req.SetRequestHeader("Content-Type",  "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {sessionToken}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = "Something went wrong. Please try again.";
                string code = "";
                try
                {
                    var respBody = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(respBody))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(respBody);
                        if (!string.IsNullOrEmpty(parsed.error))
                            err = parsed.error;
                        code = parsed.code ?? "";
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] Sign HTTP {req.responseCode}: {req.error}");
                onBlockmakerError?.Invoke(new BlockmakerError(code, err, (int)req.responseCode));
                onError?.Invoke(err);
                yield break;
            }

            try
            {
                var result = JsonUtility.FromJson<ServerSignResult>(req.downloadHandler.text);
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
            string url  = $"{_baseUrl}/v1/auth/sign";
            string body = JsonUtility.ToJson(new ServerSignRequest
                { unsignedTxnsBase64 = unsignedTxnsBase64 });

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.rewardTimeoutSeconds)
            };
            req.SetRequestHeader("Content-Type",  "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {sessionToken}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = "Something went wrong. Please try again.";
                string code = "";
                try
                {
                    var respBody = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(respBody))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(respBody);
                        if (!string.IsNullOrEmpty(parsed.error))
                            err = parsed.error;
                        code = parsed.code ?? "";
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] Sign HTTP {req.responseCode}: {req.error}");
                onBlockmakerError?.Invoke(new BlockmakerError(code, err, (int)req.responseCode));
                onError?.Invoke(err);
                yield break;
            }

            try
            {
                var result = JsonUtility.FromJson<ServerSignResult>(req.downloadHandler.text);
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
        public void SendReward(
            string               recipientWallet,
            long                 amountMicroAlgo,
            string               reason   = "reward",
            long                 assetId  = 0,
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
                    reason          = reason
                },
                config.rewardTimeoutSeconds,
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
            StartCoroutine(PostJsonAuth<TRes>(
                $"{_baseUrl}{path}", body,
                config.defaultTimeoutSeconds,
                onSuccess, onError
            ));
        }

        /// <summary>GET JSON from a server path (auto-appends wallet). Use for game-specific endpoints.</summary>
        public void Get<TRes>(string path, Action<TRes> onSuccess, Action<string> onError = null) where TRes : class
        {
            string wallet = UnityWebRequest.EscapeURL(BlockmakerAuth.Instance?.Address ?? "");
            string sep = path.Contains("?") ? "&" : "?";
            StartCoroutine(GetJson<TRes>(
                $"{_baseUrl}{path}{sep}wallet={wallet}",
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

        /// <summary>Submit a signed transaction to the Algorand network via the server.</summary>
        public void SubmitTransaction(
            string signedTxnBase64,
            Action<SubmitTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<SubmitTransactionResult>(
                $"{_baseUrl}/v1/transactions/submit",
                new SubmitTransactionRequest { signedTxnBase64 = signedTxnBase64 },
                config.rewardTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>Submit signed group transactions to the Algorand network via the server.</summary>
        public void SubmitTransactions(
            string[] signedTxnsBase64,
            Action<SubmitTransactionResult> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonAuth<SubmitTransactionResult>(
                $"{_baseUrl}/v1/transactions/submit",
                new SubmitTransactionRequest { signedTxnsBase64 = signedTxnsBase64 },
                config.rewardTimeoutSeconds, onSuccess, onError));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // SERVER HEALTH
        // ═══════════════════════════════════════════════════════════════════════════

        public void VerifyConnection(Action<bool> onResult)
        {
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
            StartCoroutine(VerifySessionTokenRoutine(sessionToken, onResult));
        }

        private IEnumerator VerifySessionTokenRoutine(string sessionToken, Action<bool> onResult)
        {
            using var req = new UnityWebRequest($"{_baseUrl}/v1/auth/session", "GET")
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.defaultTimeoutSeconds)
            };
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
            StartCoroutine(RefreshTokenRoutine(refreshToken, onSuccess, onError));
        }

        private IEnumerator RefreshTokenRoutine(string refreshToken, Action<RefreshTokenResult> onSuccess, Action<string> onError)
        {
            var body = JsonUtility.ToJson(new RefreshTokenRequest { refreshToken = refreshToken });
            using var req = new UnityWebRequest($"{_baseUrl}/v1/auth/refresh", "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.defaultTimeoutSeconds)
            };
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        /// <summary>
        /// Invalidate a refresh token on the server (logout).
        /// Fire-and-forget — does not report errors.
        /// </summary>
        public void ServerLogout(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return;
            StartCoroutine(ServerLogoutRoutine(refreshToken));
        }

        private IEnumerator ServerLogoutRoutine(string refreshToken)
        {
            var body = JsonUtility.ToJson(new LogoutRequest { refreshToken = refreshToken });
            using var req = new UnityWebRequest($"{_baseUrl}/v1/auth/logout", "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(config.defaultTimeoutSeconds)
            };
            req.SetRequestHeader("Content-Type", "application/json");
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
                config.rewardTimeoutSeconds,
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
                config.rewardTimeoutSeconds,
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
                config.rewardTimeoutSeconds,
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
                config.rewardTimeoutSeconds,
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
                config.rewardTimeoutSeconds,
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
            string url = ProfileUrl("/v1/profile/image");
            var form   = new WWWForm();
            form.AddBinaryData("image", imageBytes, filename, mimeType);

            using var req = UnityWebRequest.Post(url, form);
            req.timeout = SafeTimeout(config.rewardTimeoutSeconds);
            req.SetRequestHeader("Authorization", $"Bearer {GetSessionToken()}");

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
        private string GetSessionToken()
        {
            var identity = BlockmakerAuth.Instance?.Identity;
            if (identity is ServerSignedIdentity ss && !string.IsNullOrEmpty(ss.SessionToken))
                return ss.SessionToken;
            if (identity is MagicIdentity magic && !string.IsNullOrEmpty(magic.SessionToken))
                return magic.SessionToken;
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
            if (_baseUrl == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
            string body = JsonUtility.ToJson(payload);
            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(timeout)
            };
            req.SetRequestHeader("Content-Type",  "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {GetSessionToken()}");
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        private IEnumerator PostJson<T>(
            string url, object payload, float timeout,
            Action<T> onSuccess, Action<string> onError) where T : class
        {
            if (_baseUrl == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
            string body = JsonUtility.ToJson(payload);
            using var req = BuildPost(url, body, timeout);
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        private IEnumerator GetJson<T>(
            string url, float timeout,
            Action<T> onSuccess, Action<string> onError) where T : class
        {
            if (_baseUrl == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
            using var req = BuildGet(url, timeout);
            // Override with session token so JWT-auth players work on profile endpoints
            req.SetRequestHeader("Authorization", $"Bearer {GetSessionToken()}");
            yield return req.SendWebRequest();
            HandleResponse(req, onSuccess, onError);
        }

        private static int SafeTimeout(float seconds)
        {
            return Mathf.Max(1, Mathf.RoundToInt(seconds));
        }

        private UnityWebRequest BuildPost(string url, string jsonBody, float timeout)
        {
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = SafeTimeout(timeout)
            };
            req.SetRequestHeader("Content-Type",  "application/json");
            var token = GetAuthHeader();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            return req;
        }

        private UnityWebRequest BuildGet(string url, float timeout)
        {
            var req = UnityWebRequest.Get(url);
            req.timeout = SafeTimeout(timeout);
            var token = GetAuthHeader();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            return req;
        }

        private void HandleResponse<T>(
            UnityWebRequest req,
            Action<T>       onSuccess,
            Action<string>  onError,
            Action<BlockmakerError> onBlockmakerError = null) where T : class
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
                string code = "NETWORK";
                int httpStatus = (int)req.responseCode;
                try
                {
                    var body = req.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(body))
                    {
                        var parsed = JsonUtility.FromJson<ServerErrorResponse>(body);
                        if (!string.IsNullOrEmpty(parsed.error))
                            err = parsed.error;
                        if (!string.IsNullOrEmpty(parsed.code))
                            code = parsed.code;
                    }
                }
                catch (Exception parseEx) { BlockmakerLog.Warning($"[BlockmakerClient] Error response parse failed: {parseEx.Message}"); }
                BlockmakerLog.Error($"[BlockmakerClient] HTTP {req.responseCode}: {req.error}");

                if (onBlockmakerError != null)
                    onBlockmakerError.Invoke(new BlockmakerError(code, err, httpStatus));
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