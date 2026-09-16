// Blockmaker Unity client v0.1 — Unity 2020.2+
// Safe for player builds: public gameId + short-lived player sessions only.
// There is deliberately no server-key field or constructor argument.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Blockmaker
{
    [Serializable]
    internal sealed class BlockmakerEnvelope
    {
        public bool success = false;
        public string code = null;
        public string error = null;
        public string requestId = null;
        public int retryAfterSeconds = 0;
    }

    [Serializable]
    public sealed class BlockmakerAuthResponse
    {
        public bool success;
        public string walletAddress;
        public string displayName;
        public string sessionToken;
        public string refreshToken;
        public string accountKind;
        public string authProvider;
    }

    [Serializable]
    public sealed class BlockmakerWalletChallenge
    {
        public bool success;
        public int challengeVersion;
        public string purpose;
        public string chain;
        public string gameId;
        public string providerId;
        public string walletAddress;
        public string evmAddress;
        public string nonce;
        public string message;
        public long expiresAt;
    }

    [Serializable]
    public sealed class BlockmakerSimulationMarker
    {
        public bool enabled;
        public string fixture;
        public string gameId;
        public string economicNetwork;
        public string publicLedgerReads;
        public bool nonBroadcastable;
        public bool realPaymentsRequired;
        public string message;
    }

    [Serializable]
    public sealed class BlockmakerIntegrationGame
    {
        public string id;
        public string name;
        public string gameType;
    }

    [Serializable]
    public sealed class BlockmakerSdkIntegrity
    {
        public string unity;
        public string unityTransactionVerifier;
        public string unityWeb3AuthAvmWebGl;
        public string unityWeb3AuthAvmWebGlBridge;
    }

    [Serializable]
    public sealed class BlockmakerIntegrationConfigResponse
    {
        public bool success;
        public string apiVersion;
        public BlockmakerIntegrationGame game;
        // Non-null only for an explicitly isolated local simulator. A game
        // should show this state before offering any economic action.
        public BlockmakerSimulationMarker simulation;
        public string apiUrl;
        public string unitySdkUrl;
        public string unityTransactionVerifierUrl;
        public string unityWeb3AuthAvmWebGlSdkUrl;
        public string unityWeb3AuthAvmWebGlBridgeUrl;
        public BlockmakerSdkIntegrity sdkIntegrity;
    }

    [Serializable]
    public sealed class BlockmakerUsernameRegistrationReadiness
    {
        // ready | owner_setup_required | paused | unavailable
        public string state;
        public string reason;
        public string ownerAction;
        // enabled | hidden. Existing names and NFT pictures remain available.
        public string registrationUi;
        public bool existingUsernamesAvailable;
        public bool nftProfilePicturesAvailable;
    }

    [Serializable]
    public sealed class BlockmakerGameProfile
    {
        public string walletAddress;
        public string username;
        public string displayName;
        public string nfdName;
        public string usernameScope;
        public string usernameSource;
        // Shared owner-wallet appearance, independent of game progress.
        public string profileScope;
        public long profileRevision;
        public string profileImageUrl;
        public long profilePicAssetId;
        public string profilePicNetwork;
        // none | verified | refreshing | verification_delayed | not_owned | unsupported
        public string profilePicStatus;
        public long profilePicVerifiedAt;
        public long profilePicCheckedAt;
        public long createdAt;
        public long updatedAt;
    }

    [Serializable]
    public sealed class BlockmakerGameProfileResponse
    {
        public bool success;
        public string gameId;
        public string walletAddress;
        public BlockmakerUsernameRegistrationReadiness usernameRegistration;
        public BlockmakerGameProfile profile;
    }

    [Serializable]
    public sealed class BlockmakerGameProfileBatchResponse
    {
        public bool success;
        public string gameId;
        public BlockmakerUsernameRegistrationReadiness usernameRegistration;
        public BlockmakerGameProfile[] profiles;
    }

    [Serializable]
    public sealed class BlockmakerOwnedNft
    {
        public long assetId;
        public string name;
        public string unitName;
        public string imageUrl;
        public long totalSupply;
        public string eligibility;
        public bool eligible;
        public string eligibilityReason;
    }

    [Serializable]
    public sealed class BlockmakerNftIndexProgress
    {
        public string status;
        public int indexed;
        public int total;
    }

    [Serializable]
    public sealed class BlockmakerOwnedNftSearchResponse
    {
        public bool success;
        public BlockmakerOwnedNft[] assets;
        public string nextCursor;
        public bool hasMore;
        public BlockmakerNftIndexProgress index;
    }

    [Serializable]
    public sealed class BlockmakerOnrampSession
    {
        public bool success;
        public string orderId;
        public string provider;
        public string widgetUrl;
        public long expiresAt;
        public string walletAddress;
        public string fiatCurrency;
        public float fiatAmount;
        public string cryptoCurrency;
        public string network;
        public bool destinationLocked;
    }

    [Serializable]
    public sealed class BlockmakerMarketplaceBuild
    {
        public bool success;
        public string action;
        public long assetId;
        public long priceMicroalgo;
        public float priceAlgo;
        public string seller;
        public bool optInIncluded;
        public long depositMicroalgo;
        public string network;
        public long appId;
        public long networkFeeMicroalgo;
        public long amountDueNowMicroalgo;
        public int transactionCount;
        public string[] unsignedTxnsBase64;
        public string signingIntent;
        public long signingIntentExpiresAt;
    }

    [Serializable]
    public sealed class BlockmakerStoreItem
    {
        public int slot;
        public long assetId;
        public long amountAtomic;
        public string name;
        public string unitName;
        public string txId;
        public string classKey;
        public string classLabel;
        public string imageUrl;
        // Legacy NFTURBO presentation aliases. Generic games should use the
        // provider-neutral name/class fields above.
        public string collection;
        public string part;
        public string rarity;
    }

    [Serializable]
    public sealed class BlockmakerStoreOpenCommit
    {
        public string commitId;
        public string status;
        public int packs;
        public string expectedTxId;
        public string paymentState;
        public bool separateUnsignedIntent;
    }

    [Serializable]
    public sealed class BlockmakerStoreOpenCommitsResponse
    {
        public bool success;
        public BlockmakerStoreOpenCommit[] commits;
        public BlockmakerStoreOpenCommit[] unpaidIntents;
    }

    [Serializable]
    public sealed class BlockmakerStoreDeliveryResponse
    {
        public bool success;
        public string code;
        public string error;
        public string commitId;
        public string txId;
        public string status;
        public string phase;
        public string message;
        public int retryAfterSeconds;
        public long[] assetIds;
        public long[] pendingAssetIds;
        public BlockmakerStoreItem[] items;
        public BlockmakerStoreItem[] parts;
        public BlockmakerStoreItem[] deliveredParts;
    }

    [Serializable]
    public sealed class BlockmakerStoreRevenueRecipient
    {
        public int ordinal;
        public string destinationId;
        public string label;
        public string address;
        public int bps;
        public long amountMicroalgo;
        public bool isRemainder;
        public string expectedTxId;
    }

    [Serializable]
    public sealed class BlockmakerStorePurchasePreparation
    {
        public bool success;
        public int storeApiVersion;
        public string storeKind;
        public string catalogueProvider;
        public string commitId;
        public int packs;
        public int partsPerPack;
        public long amountAtomic;
        public long amountMicro;
        public string expectedTxId;
        public int paymentPlanVersion;
        public int transactionCount;
        public string[] expectedTransactionIds;
        public string network;
        public long networkFeeMicroalgo;
        public string paymentGroupId;
        public string paymentGroupCommitment;
        public int storeRevenuePolicyRevision;
        public string storeRevenuePolicyHash;
        public BlockmakerStoreRevenueRecipient[] revenueRecipients;
        public string[] unsignedTxnsBase64;
        public long firstValidRound;
        public long lastValidRound;
        public bool replayed;
    }

    [Serializable]
    public sealed class BlockmakerStoreAssetAcceptancePreparation
    {
        public bool success;
        public string[] unsignedTxnsBase64;
        public string txType;
        public string from;
        public long[] assetIds;
    }

    public sealed class BlockmakerResponse
    {
        public bool Success { get; internal set; }
        public long Status { get; internal set; }
        public string Code { get; internal set; }
        public string Error { get; internal set; }
        public string RequestId { get; internal set; }
        public int? RetryAfterSeconds { get; internal set; }
        public string Json { get; internal set; }

        public T Parse<T>() where T : class
        {
            return string.IsNullOrEmpty(Json) ? null : JsonUtility.FromJson<T>(Json);
        }
    }

    /// <summary>
    /// Result of one authenticated player request that is never refreshed or
    /// retried automatically. Once transmission starts, a client cannot prove
    /// that a timeout or error happened before the server applied the request,
    /// so MayHaveApplied remains conservatively true.
    /// </summary>
    public sealed class BlockmakerPlayerRequestOnceResult
    {
        public BlockmakerResponse Response { get; internal set; }
        public bool TransmissionAttempted { get; internal set; }
        public bool MayHaveApplied { get; internal set; }
    }

    /// <summary>
    /// Result of one typed Store submission. This distinct surface keeps Store
    /// validation and authorization policy explicit at the call site.
    /// </summary>
    public sealed class BlockmakerStoreSubmissionOnceResult
    {
        public BlockmakerResponse Response { get; internal set; }
        public bool TransmissionAttempted { get; internal set; }
        public bool MayHaveApplied { get; internal set; }
    }

    [Serializable] internal sealed class EmailRequest { public string email; public string gameId; }
    [Serializable] internal sealed class EmailVerifyRequest { public string email; public string otp; public string gameId; }
    [Serializable] internal sealed class MagicVerifyRequest { public string didToken; public string email; public string gameId; }
    [Serializable] internal sealed class RefreshRequest { public string refreshToken; public string gameId; }
    [Serializable] internal sealed class OnrampSessionRequest { public string fiatCurrency; public float fiatAmount; }
    [Serializable] internal sealed class GateEvaluationRequest { public string[] gateKeys; }
    [Serializable] internal sealed class GameUsernameRequest { public string username; }
    [Serializable] internal sealed class UniversalUsernameCompleteRequest
    {
        public string reservationId; public string[] signedTxnsBase64; public string signingIntent;
    }
    [Serializable] internal sealed class NftAssetRequest { public long assetId; }
    [Serializable] internal sealed class NftSearchRequest { public string search; public string cursor; public int limit; }
    [Serializable] internal sealed class MarketplaceListRequest { public long assetId; public long priceMicroalgo; }
    [Serializable] internal sealed class MarketplaceAssetRequest { public long assetId; }
    [Serializable] internal sealed class StoreCommitRequest
    {
        public string commitId; public string txId;
    }
    [Serializable] internal sealed class StorePurchaseRequest
    {
        public int packs;
        public int[] supportedPaymentPlanVersions;
        public string expectedStoreRevenuePolicyHash;
    }
    [Serializable] internal sealed class StorePaymentSubmissionRequest
    {
        public string commitId;
        public string[] signedTxnsBase64;
    }
    [Serializable] internal sealed class StoreAssetAcceptanceRequest
    {
        public long[] assetIds;
    }
    [Serializable] internal sealed class StoreAssetAcceptanceSubmissionRequest
    {
        public string[] signedTxnsBase64;
    }
    [Serializable] internal sealed class YieldClaimRequest
    {
        public string idempotencyKey; public long amountBaseUnits;
    }
    [Serializable] internal sealed class MarketplaceSignRequest
    {
        public string[] unsignedTxnsBase64; public string signingIntent; public string gameId;
    }
    [Serializable] internal sealed class MarketplaceSubmitRequest
    {
        public string[] signedTxnsBase64; public string signingIntent;
    }
    [Serializable] internal sealed class WalletChallengeRequest
    {
        public string walletAddress; public string chain; public string evmAddress; public string providerId; public string gameId;
    }
    [Serializable] internal sealed class WalletVerifyRequest
    {
        public string walletAddress; public string chain; public string nonce; public string signature;
        public string signedTxn; public string evmAddress; public string providerId; public string gameId;
    }

    // Stable IDs returned by the game-specific public manifest. Prefer reading
    // the enabled list from GetConfig rather than hard-coding a wallet picker.
    public static class BlockmakerWalletProviders
    {
        public const string Pera = "pera";
        public const string TxnLabWeb3Auth = "txnlab_web3auth";
        public const string Lute = "lute";
        public const string Web3AuthAvmEmail = "web3auth_avm_email";
        public const string MagicEmail = "magic_email";
        public const string XChain = "xchain";
        public const string Defly = "defly";
        public const string DeflyWeb = "defly_web";
    }

    [Serializable]
    public sealed class BlockmakerPlaySession
    {
        public string id;
        public long startedAt;
        public long lastSeenAt;
        public long activeSeconds;
        public long endedAt;
    }

    [Serializable]
    public sealed class BlockmakerPlaySessionResponse
    {
        public bool success;
        public string gameId;
        public long creditedSeconds;
        public bool alreadyEnded;
        public BlockmakerPlaySession session;
    }

    public sealed class BlockmakerClient
    {
        private readonly Uri apiOrigin;
        private readonly string gameId;
        private bool refreshInProgress;
        private BlockmakerResponse sharedRefreshResult;

        public string BaseUrl { get { return apiOrigin.GetLeftPart(UriPartial.Authority); } }
        public string GameId { get { return gameId; } }
        public string SessionToken { get; private set; }
        public string RefreshToken { get; private set; }
        public string WalletAddress { get; private set; }
        public string AccountKind { get; private set; }
        public string AuthProvider { get; private set; }
        internal event Action<string, string> PlayerSessionRefreshed;
        /// <summary>
        /// True for the email-derived AVM identity. This session authenticates
        /// the player but is never an economic signer or funding destination.
        /// </summary>
        public bool IsAuthenticationOnlyPlayerSession
        {
            get {
                return AccountKind == "auth_only_email_wallet"
                    || AuthProvider == BlockmakerWalletProviders.Web3AuthAvmEmail;
            }
        }
        public int TimeoutSeconds { get; set; } = 15;

        public BlockmakerClient(string baseUrl, string publicGameId)
        {
            Uri parsed;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(parsed.UserInfo)
                || (parsed.AbsolutePath != "/" && parsed.AbsolutePath != "")
                || !string.IsNullOrEmpty(parsed.Query)
                || !string.IsNullOrEmpty(parsed.Fragment))
                throw new ArgumentException("Blockmaker baseUrl must be an exact http(s) origin with no path or credentials.", nameof(baseUrl));

            var loopback = parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || parsed.Host == "127.0.0.1" || parsed.Host == "[::1]" || parsed.Host == "::1";
            if (parsed.Scheme != Uri.UriSchemeHttps && !loopback)
                throw new ArgumentException("Blockmaker baseUrl must use HTTPS (HTTP is allowed only for localhost development).", nameof(baseUrl));

            publicGameId = (publicGameId ?? "").Trim();
            if (publicGameId.Length == 0 || publicGameId.StartsWith("sk_", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the public Blockmaker game ID, never a server key.", nameof(publicGameId));

            apiOrigin = new Uri(parsed.GetLeftPart(UriPartial.Authority));
            gameId = publicGameId;
        }

        // Restore only from an OS secure credential store (Keychain/Keystore), never PlayerPrefs.
        public void SetPlayerSession(
            string sessionToken,
            string refreshToken = null,
            string walletAddress = null,
            string accountKind = null,
            string authProvider = null)
        {
            var nextSession = string.IsNullOrWhiteSpace(sessionToken) ? null : sessionToken.Trim();
            var nextRefresh = string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken.Trim();
            if ((nextSession != null && nextSession.StartsWith("sk_", StringComparison.OrdinalIgnoreCase))
                || (nextRefresh != null && nextRefresh.StartsWith("sk_", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A Blockmaker server key cannot be used as a Unity player session.", nameof(sessionToken));
            var nextAccountKind = SessionClassifier(accountKind, nameof(accountKind));
            var nextAuthProvider = SessionClassifier(authProvider, nameof(authProvider));
            var emailKind = nextAccountKind == "auth_only_email_wallet";
            var emailProvider = nextAuthProvider == BlockmakerWalletProviders.Web3AuthAvmEmail;
            if (emailKind != emailProvider)
                throw new ArgumentException(
                    "The authentication-only email account kind and provider must be supplied together.",
                    nameof(accountKind));
            SessionToken = nextSession;
            RefreshToken = nextRefresh;
            WalletAddress = string.IsNullOrWhiteSpace(walletAddress) ? null : walletAddress.Trim();
            AccountKind = nextAccountKind;
            AuthProvider = nextAuthProvider;
        }

        public void ClearPlayerSession()
        {
            SessionToken = null;
            RefreshToken = null;
            WalletAddress = null;
            AccountKind = null;
            AuthProvider = null;
        }

        private static string SessionClassifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length > 64
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    normalized, "^[a-z][a-z0-9_]{0,63}$"))
                throw new ArgumentException(
                    "Player session classifiers must be canonical public identifiers.",
                    parameterName);
            return normalized;
        }

        public IEnumerator GetConfig(Action<BlockmakerResponse> done)
        {
            return Request("/v1/integrations/config", "GET", null, false, done);
        }

        public IEnumerator GetManifest(Action<BlockmakerResponse> done)
        {
            return Request("/v1/integrations/manifest", "GET", null, false, done);
        }

        // One explicit gameplay session. Blockmaker derives active time only
        // from capped server-timestamped heartbeat gaps; Unity never submits a
        // duration, IP address, device fingerprint or arbitrary event payload.
        // Call StartAnalyticsSession when real gameplay begins, send a heartbeat
        // every 30 seconds while active, and call EndAnalyticsSession on exit or
        // game over. All three calls may run before or after player sign-in.
        public IEnumerator StartAnalyticsSession(Action<BlockmakerResponse> done)
        {
            return Request("/v1/analytics/session/start", "POST", null, true, done);
        }

        public IEnumerator HeartbeatAnalyticsSession(string sessionId, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(sessionId)
                || !System.Text.RegularExpressions.Regex.IsMatch(sessionId.Trim(), "^[a-f0-9]{48}$"))
                throw new ArgumentException("Use the play session id returned by StartAnalyticsSession.", nameof(sessionId));
            return Request("/v1/analytics/session/" + UnityWebRequest.EscapeURL(sessionId.Trim()) + "/heartbeat",
                "POST", null, true, done);
        }

        public IEnumerator EndAnalyticsSession(string sessionId, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(sessionId)
                || !System.Text.RegularExpressions.Regex.IsMatch(sessionId.Trim(), "^[a-f0-9]{48}$"))
                throw new ArgumentException("Use the play session id returned by StartAnalyticsSession.", nameof(sessionId));
            return Request("/v1/analytics/session/" + UnityWebRequest.EscapeURL(sessionId.Trim()) + "/end",
                "POST", null, true, done);
        }

        public IEnumerator RequestEmail(string email, Action<BlockmakerResponse> done)
        {
            return Request("/v1/auth/email/request", "POST",
                JsonUtility.ToJson(new EmailRequest { email = email, gameId = gameId }), false, done);
        }

        public IEnumerator VerifyEmail(string email, string otp, Action<BlockmakerResponse> done)
        {
            BlockmakerResponse result = null;
            yield return Request("/v1/auth/email/verify", "POST",
                JsonUtility.ToJson(new EmailVerifyRequest { email = email, otp = otp, gameId = gameId }),
                false, response => result = response);
            AcceptLogin(result);
            done?.Invoke(result);
        }

        // The game obtains didToken client-side from Magic's Algorand extension.
        // Blockmaker verifies it with Magic before accepting the wallet address.
        public IEnumerator VerifyMagic(string didToken, string email, Action<BlockmakerResponse> done)
        {
            BlockmakerResponse result = null;
            yield return Request("/v1/auth/magic/verify", "POST",
                JsonUtility.ToJson(new MagicVerifyRequest { didToken = didToken, email = email, gameId = gameId }),
                false, response => result = response);
            AcceptLogin(result);
            done?.Invoke(result);
        }

        public IEnumerator RequestWalletChallenge(string walletAddress, string chain, string evmAddress, Action<BlockmakerResponse> done)
        {
            // Compatibility overload for games created before provider-bound
            // policies. New integrations should pass the manifest provider ID.
            return RequestWalletChallenge(walletAddress, chain, evmAddress, null, done);
        }

        public IEnumerator RequestWalletChallenge(string walletAddress, string chain, string evmAddress, string providerId, Action<BlockmakerResponse> done)
        {
            var body = new WalletChallengeRequest {
                walletAddress = walletAddress, chain = chain, evmAddress = evmAddress,
                providerId = providerId, gameId = gameId
            };
            return Request("/v1/auth/wallet/challenge", "POST", JsonUtility.ToJson(body), false, done);
        }

        public IEnumerator RequestAlgorandWalletChallenge(string walletAddress, string providerId, Action<BlockmakerResponse> done)
        {
            return RequestWalletChallenge(walletAddress, "algorand", null, providerId, done);
        }

        // EVM/xChain callers send only the EVM account. Blockmaker derives the
        // linked Algorand address and returns it in BlockmakerWalletChallenge.
        public IEnumerator RequestEvmWalletChallenge(string evmAddress, Action<BlockmakerResponse> done)
        {
            return RequestEvmWalletChallenge(evmAddress, BlockmakerWalletProviders.XChain, done);
        }

        public IEnumerator RequestEvmWalletChallenge(string evmAddress, string providerId, Action<BlockmakerResponse> done)
        {
            return RequestWalletChallenge(null, "evm", evmAddress, providerId, done);
        }

        private static bool IsCanonicalAlgorandAddress(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 58) return false;
            foreach (var character in value)
                if (!((character >= 'A' && character <= 'Z') || (character >= '2' && character <= '7'))) return false;
            return true;
        }

        private static bool IsEvmAddress(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 42 || !value.StartsWith("0x", StringComparison.Ordinal)) return false;
            for (var index = 2; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static bool IsEvmSignature(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 132 || !value.StartsWith("0x", StringComparison.Ordinal)) return false;
            for (var index = 2; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'))) return false;
            }
            return true;
        }

        private static bool IsBase64UrlNonce(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            foreach (var character in value)
                if (!(char.IsLetterOrDigit(character) || character == '-' || character == '_')) return false;
            return true;
        }

        // Call this before opening personal_sign. It prevents a host integration
        // from asking the wallet to sign altered, stale, cross-game challenge
        // text even though final server verification would reject that proof.
        public bool ValidateEvmWalletChallenge(BlockmakerWalletChallenge challenge, string expectedEvmAddress, out string error)
        {
            error = "Blockmaker returned an invalid EVM sign-in challenge. Nothing should be signed.";
            var expectedEvm = (expectedEvmAddress ?? "").Trim().ToLowerInvariant();
            if (challenge == null || (challenge.challengeVersion != 1 && challenge.challengeVersion != 2) || challenge.purpose != "player-sign-in"
                || challenge.chain != "evm" || challenge.gameId != gameId
                || !IsCanonicalAlgorandAddress(challenge.walletAddress)
                || !IsEvmAddress(challenge.evmAddress) || challenge.evmAddress != expectedEvm
                || !IsBase64UrlNonce(challenge.nonce)) return false;

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = (long)(DateTime.UtcNow - epoch).TotalMilliseconds;
            if (challenge.expiresAt <= now || challenge.expiresAt > now + 10 * 60 * 1000) return false;
            var expires = epoch.AddMilliseconds(challenge.expiresAt).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            if (challenge.challengeVersion == 2
                && challenge.providerId != BlockmakerWalletProviders.XChain
                && challenge.providerId != BlockmakerWalletProviders.MagicEmail) return false;
            if (challenge.challengeVersion == 1 && !string.IsNullOrEmpty(challenge.providerId)) return false;
            var providerLine = challenge.challengeVersion == 2 ? "provider: " + challenge.providerId + "\n" : "";
            var expectedMessage = "Blockmaker player sign-in\n"
                + "developer: " + gameId + "\n" + providerLine
                + "algorand: " + challenge.walletAddress + "\n"
                + "evm: " + challenge.evmAddress + "\n"
                + "nonce: " + challenge.nonce + "\n"
                + "expires: " + expires;
            if (string.IsNullOrEmpty(challenge.message) || challenge.message.Length > 1000
                || challenge.message != expectedMessage) return false;

            error = null;
            return true;
        }

        // Sign the exact challenge message outside this class, then submit the proof.
        // Algorand accepts signature (signData) or signedTxn (Pera transaction proof).
        public IEnumerator VerifyWallet(
            string walletAddress, string chain, string nonce, string signature, string signedTxn,
            string evmAddress, Action<BlockmakerResponse> done)
        {
            // Compatibility overload for legacy, unlabelled wallet adapters.
            return VerifyWallet(walletAddress, chain, nonce, signature, signedTxn, evmAddress, null, done);
        }

        public IEnumerator VerifyWallet(
            string walletAddress, string chain, string nonce, string signature, string signedTxn,
            string evmAddress, string providerId, Action<BlockmakerResponse> done)
        {
            var body = new WalletVerifyRequest {
                walletAddress = walletAddress, chain = chain, nonce = nonce, signature = signature,
                signedTxn = signedTxn, evmAddress = evmAddress, providerId = providerId, gameId = gameId
            };
            BlockmakerResponse result = null;
            yield return Request("/v1/auth/wallet/verify", "POST", JsonUtility.ToJson(body), false, response => result = response);
            AcceptLogin(result);
            done?.Invoke(result);
        }

        // Submit exactly the server-returned EVM challenge identity. The host
        // wallet signs challenge.message with personal_sign; no transaction,
        // gas payment, or network switch is involved.
        public IEnumerator VerifyEvmWallet(BlockmakerWalletChallenge challenge, string signature, Action<BlockmakerResponse> done)
        {
            string challengeError;
            if (!ValidateEvmWalletChallenge(challenge, challenge == null ? null : challenge.evmAddress, out challengeError))
                throw new ArgumentException(challengeError, nameof(challenge));
            if (!IsEvmSignature(signature))
                throw new ArgumentException("The EVM wallet must return one 65-byte personal_sign signature.", nameof(signature));
            return VerifyWallet(challenge.walletAddress, "evm", challenge.nonce, signature, null, challenge.evmAddress, challenge.providerId, done);
        }

        // Historical full profile response. Kept so existing games do not
        // break; both profile methods read the same shared name and appearance.
        [Obsolete("Use GetGameProfile for the shared player identity and compatibility response envelope.")]
        public IEnumerator GetProfile(Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile", "GET", null, true, done);
        }

        public IEnumerator GetGameProfile(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-profile", "GET", null, true, done);
        }

        public IEnumerator CheckGameUsername(string username, Action<BlockmakerResponse> done)
        {
            return CheckUniversalUsername(username, done);
        }

        public IEnumerator CheckUniversalUsername(string username, Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile/username/check", "POST",
                JsonUtility.ToJson(new GameUsernameRequest { username = username }), true, done);
        }

        // Returns the exact two-transaction Algorand group, price breakdown and
        // short-lived signing intent. The game must show the price and have the
        // player's wallet sign the returned bytes without changing them.
        public IEnumerator PrepareUniversalUsername(string username, Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile/username/claim/prepare", "POST",
                JsonUtility.ToJson(new GameUsernameRequest { username = username }), true, done);
        }

        // Call only with the exact reservation, signed group and intent returned
        // by PrepareUniversalUsername. Blockmaker reconciles uncertain submissions
        // and will never ask the player to approve a replacement payment blindly.
        public IEnumerator CompleteUniversalUsername(string reservationId, string[] signedTxnsBase64,
            string signingIntent, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(reservationId))
                throw new ArgumentException("reservationId is required.", nameof(reservationId));
            if (signedTxnsBase64 == null || signedTxnsBase64.Length != 2)
                throw new ArgumentException("Both signed username transactions are required.", nameof(signedTxnsBase64));
            return RequestPlayerOnce("/v1/profile/username/claim/complete", "POST",
                JsonUtility.ToJson(new UniversalUsernameCompleteRequest {
                    reservationId = reservationId,
                    signedTxnsBase64 = signedTxnsBase64,
                    signingIntent = signingIntent
                }), outcome => done?.Invoke(outcome.Response));
        }

        /// <summary>Reconcile the saved registration ID without submitting or signing again.</summary>
        public IEnumerator GetUniversalUsernameRequest(string reservationId, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(reservationId)) throw new ArgumentException("A reservation ID is required.");
            return Request("/v1/profile/username/claim/requests/" + Uri.EscapeDataString(reservationId), "GET", null, true, done);
        }

        public IEnumerator GetUniversalUsernames(Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile/username/names", "GET", null, true, done);
        }

        public IEnumerator SetPrimaryUniversalUsername(string username, Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile/username/primary", "POST",
                JsonUtility.ToJson(new GameUsernameRequest { username = username }), true, done);
        }

        [Obsolete("Free game-local usernames were retired. Use PrepareUniversalUsername and CompleteUniversalUsername.")]
        public IEnumerator SetGameUsername(string username, Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-profile/username", "PUT",
                JsonUtility.ToJson(new GameUsernameRequest { username = username }), true, done);
        }

        [Obsolete("A game cannot remove a player-owned universal username.")]
        public IEnumerator ClearGameUsername(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-profile/username", "DELETE", null, true, done);
        }

        public IEnumerator SearchOwnedNfts(string search, Action<BlockmakerResponse> done)
        {
            return SearchOwnedNfts(search, null, 24, done);
        }

        public IEnumerator SearchOwnedNfts(string search, string cursor, int limit, Action<BlockmakerResponse> done)
        {
            if (limit < 1 || limit > 50) throw new ArgumentException("limit must be between 1 and 50.", nameof(limit));
            return Request("/v1/profile/wallet/nfts", "POST",
                JsonUtility.ToJson(new NftSearchRequest { search = search, cursor = cursor, limit = limit }), true, done);
        }

        public IEnumerator PreviewOwnedNft(long assetId, Action<BlockmakerResponse> done)
        {
            if (assetId <= 0) throw new ArgumentException("assetId must be positive.", nameof(assetId));
            return Request("/v1/profile/nft/image", "POST",
                JsonUtility.ToJson(new NftAssetRequest { assetId = assetId }), true, done);
        }

        // Compatibility name: changes the owner wallet's shared picture in every game.
        public IEnumerator SetGameProfileNft(long assetId, Action<BlockmakerResponse> done)
        {
            if (assetId <= 0) throw new ArgumentException("assetId must be positive.", nameof(assetId));
            return Request("/v1/game-profile/pfp", "PUT",
                JsonUtility.ToJson(new NftAssetRequest { assetId = assetId }), true, done);
        }

        public IEnumerator RefreshGameProfileNft(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-profile/pfp/refresh", "POST", "{}", true, done);
        }

        // An explicit shared clear. Loading a profile must never invoke this.
        public IEnumerator ClearGameProfileNft(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-profile/pfp", "DELETE", null, true, done);
        }

        public IEnumerator GetGameData(Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile/game-data", "GET", null, true, done);
        }

        // dataJson is the COMPLETE cloud-save snapshot and replaces the prior object.
        // Read/merge first when applying a partial change. It must match Admin → Player Data.
        public IEnumerator SaveGameData(string dataJson, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(dataJson) || !dataJson.TrimStart().StartsWith("{"))
                throw new ArgumentException("dataJson must be a JSON object.", nameof(dataJson));
            return Request("/v1/profile/game-data", "POST", "{\"data\":" + dataJson + "}", true, done);
        }

        public IEnumerator GetProfileSchema(Action<BlockmakerResponse> done)
        {
            return Request("/v1/profile/schema", "GET", null, true, done);
        }

        public IEnumerator GetGatingRules(Action<BlockmakerResponse> done)
        {
            return Request("/v1/gating/rules", "GET", null, false, done);
        }

        // ── Randomized ASA Store ──────────────────────────────────────────
        // These low-level, player-session-bound methods never connect a wallet,
        // request a signature or build a replacement transaction. Only the
        // explicit submission methods transmit an already-signed, reviewed
        // group. A game verifies any server-authored payment group with the
        // published transaction verifier before opening its wallet bridge.
        // Store v1's legacy poolSize/packsAvailable JSON keys are coarse
        // compatibility capacity sentinels derived from the largest configured
        // bundle when payments are ready (zero otherwise), not live inventory.
        // Use enabled/inStock/readiness only as the player availability gate;
        // exact stock is intentionally owner-only to prevent draw inference.
        public IEnumerator GetStoreInfo(Action<BlockmakerResponse> done)
        {
            return Request("/v1/pack-shop/info", "GET", null, true, done);
        }

        public IEnumerator GetOpenStoreCommits(Action<BlockmakerResponse> done)
        {
            return Request("/v1/pack-shop/commits", "GET", null, true, done);
        }

        // Prepare one exact payment-plan-3 Store group. This does not sign or
        // submit anything. Pin policyHash in the shipped game configuration,
        // parse BlockmakerStorePurchasePreparation, then verify the ORIGINAL
        // unsignedTxnsBase64 with VerifyStoreRevenueDirectSplit* before giving
        // those unchanged bytes to the game's wallet bridge.
        public IEnumerator PrepareStorePurchase(
            int packs,
            string policyHash,
            Action<BlockmakerResponse> done)
        {
            if (packs < 1 || packs > 10)
                throw new ArgumentException("packs must match one saved Store choice from 1 to 10.", nameof(packs));
            var exactPolicyHash = string.IsNullOrWhiteSpace(policyHash) ? "" : policyHash.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(exactPolicyHash, "^[a-f0-9]{64}$"))
                throw new ArgumentException("Use the exact lowercase owner-reviewed Store Revenue policy hash.", nameof(policyHash));
            return RequestWithTimeout("/v1/pack-shop/commit", "POST",
                JsonUtility.ToJson(new StorePurchaseRequest {
                    packs = packs,
                    supportedPaymentPlanVersions = new[] { 3 },
                    expectedStoreRevenuePolicyHash = exactPolicyHash,
                }), true, Math.Max(35, TimeoutSeconds), done);
        }

        // Submit one wallet-signed copy of the exact reviewed payment group.
        // A retry must reuse these identical signed bytes and commit ID; never
        // call PrepareStorePurchase to recover an uncertain submission. This
        // compatibility method retains the general 401-refresh behavior; new
        // economic integrations should use SubmitStorePaymentOnce below.
        public IEnumerator SubmitStorePayment(
            string commitId,
            string[] signedTxnsBase64,
            Action<BlockmakerResponse> done)
        {
            return RequestWithTimeout("/v1/pack-shop/submit", "POST",
                JsonUtility.ToJson(new StorePaymentSubmissionRequest {
                    commitId = ValidateStoreCommitId(commitId),
                    signedTxnsBase64 = ValidateStoreSignedTransactions(signedTxnsBase64, 4),
                }), true, Math.Max(35, TimeoutSeconds), done);
        }

        // Preferred economic-submission boundary. Inputs are validated before
        // the coroutine exists, and a valid call uses the existing player
        // session for at most one transmission. It never refreshes or retries.
        public IEnumerator SubmitStorePaymentOnce(
            string commitId,
            string[] signedTxnsBase64,
            Action<BlockmakerStoreSubmissionOnceResult> done)
        {
            var exactCommitId = ValidateStoreCommitId(commitId);
            var exactSignedTransactions = ValidateStoreSignedTransactions(signedTxnsBase64, 4);
            return SendPlayerRequestOnce("/v1/pack-shop/submit", "POST",
                JsonUtility.ToJson(new StorePaymentSubmissionRequest {
                    commitId = exactCommitId,
                    signedTxnsBase64 = exactSignedTransactions,
                }), Math.Max(35, TimeoutSeconds), value =>
                    done?.Invoke(StoreSubmissionOnceResult(value)));
        }

        // Build one exact atomic group for the asset IDs returned by
        // SHOP_ASSET_OPT_IN_REQUIRED. This is a separate wallet action and can
        // never pay for, replace, or cancel the recorded Store order.
        public IEnumerator BuildStoreAssetAcceptance(
            long[] assetIds,
            Action<BlockmakerResponse> done)
        {
            if (assetIds == null || assetIds.Length < 1 || assetIds.Length > 16)
                throw new ArgumentException("assetIds must contain between 1 and 16 exact Store asset IDs.", nameof(assetIds));
            var seen = new System.Collections.Generic.HashSet<long>();
            foreach (var assetId in assetIds)
                if (assetId <= 0 || !seen.Add(assetId))
                    throw new ArgumentException("assetIds must be unique positive Store asset IDs.", nameof(assetIds));
            return RequestWithTimeout("/v1/transactions/build-optin-group", "POST",
                JsonUtility.ToJson(new StoreAssetAcceptanceRequest { assetIds = assetIds }),
                true, Math.Max(35, TimeoutSeconds), done);
        }

        // Submit one signed copy of the exact asset-acceptance group.
        // Retry only the same signed bytes after an uncertain response, then
        // resume WatchStoreDelivery; never ask the player to sign replacements.
        // This compatibility method retains the general 401-refresh behavior;
        // new economic integrations should use SubmitStoreAssetAcceptanceOnce.
        public IEnumerator SubmitStoreAssetAcceptance(
            string[] signedTxnsBase64,
            Action<BlockmakerResponse> done)
        {
            return RequestWithTimeout("/v1/transactions/submit", "POST",
                JsonUtility.ToJson(new StoreAssetAcceptanceSubmissionRequest {
                    signedTxnsBase64 = ValidateStoreSignedTransactions(signedTxnsBase64, 16),
                }), true, Math.Max(35, TimeoutSeconds), done);
        }

        // Preferred one-transmission boundary for the separately reviewed
        // Store asset-acceptance group. Validation happens before any send.
        public IEnumerator SubmitStoreAssetAcceptanceOnce(
            string[] signedTxnsBase64,
            Action<BlockmakerStoreSubmissionOnceResult> done)
        {
            var exactSignedTransactions = ValidateStoreSignedTransactions(signedTxnsBase64, 16);
            return SendPlayerRequestOnce("/v1/transactions/submit", "POST",
                JsonUtility.ToJson(new StoreAssetAcceptanceSubmissionRequest {
                    signedTxnsBase64 = exactSignedTransactions,
                }), Math.Max(35, TimeoutSeconds), value =>
                    done?.Invoke(StoreSubmissionOnceResult(value)));
        }

        public IEnumerator ConfirmStoreCommit(string commitId, Action<BlockmakerResponse> done)
        {
            return ConfirmStoreCommit(commitId, null, done);
        }

        public IEnumerator ConfirmStoreCommit(string commitId, string txId, Action<BlockmakerResponse> done)
        {
            var exactCommitId = ValidateStoreCommitId(commitId);
            var exactTxId = string.IsNullOrWhiteSpace(txId) ? null : txId.Trim().ToUpperInvariant();
            if (exactTxId != null
                && !System.Text.RegularExpressions.Regex.IsMatch(exactTxId, "^[A-Z2-7]{52}$"))
                throw new ArgumentException("Use the exact existing Algorand payment transaction ID.", nameof(txId));
            return RequestWithTimeout("/v1/pack-shop/confirm-commit", "POST",
                JsonUtility.ToJson(new StoreCommitRequest { commitId = exactCommitId, txId = exactTxId }),
                true, Math.Max(35, TimeoutSeconds), done);
        }

        public IEnumerator RevealStoreCommit(string commitId, Action<BlockmakerResponse> done)
        {
            return RequestWithTimeout("/v1/pack-shop/reveal", "POST",
                JsonUtility.ToJson(new StoreCommitRequest {
                    commitId = ValidateStoreCommitId(commitId), txId = null,
                }), true, Math.Max(35, TimeoutSeconds), done);
        }

        // Reconciles one already-created Store order until it is distributed,
        // blocked, or needs an explicit asset-acceptance wallet gesture. This
        // watcher never prepares, signs, submits, replaces, or retries a
        // payment. Call it after the exact payment has crossed the signing
        // boundary; when awaiting_opt_in is returned, let the player approve
        // that separate action and then call this watcher again.
        public IEnumerator WatchStoreDelivery(
            string commitId,
            Action<BlockmakerResponse> progress,
            Action<BlockmakerResponse> done)
        {
            return WatchStoreDelivery(commitId, null, progress, done);
        }

        // cancelled is checked locally while waiting. Stopping this coroutine
        // never cancels the recorded order or changes anything on Algorand.
        public IEnumerator WatchStoreDelivery(
            string commitId,
            Func<bool> cancelled,
            Action<BlockmakerResponse> progress,
            Action<BlockmakerResponse> done)
        {
            var exactCommitId = ValidateStoreCommitId(commitId);
            BlockmakerResponse open = null;
            while (true)
            {
                if (StoreWatchIsCancelled(cancelled))
                {
                    done?.Invoke(StoreWatchLocalFailure("SHOP_WATCH_CANCELLED", "The local Store delivery watch was stopped. The recorded order was not cancelled."));
                    yield break;
                }
                yield return GetOpenStoreCommits(response => open = response);
                if (open != null && open.Success) break;
                if (!StoreWatchIsTransient(open))
                {
                    done?.Invoke(open ?? StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "Blockmaker did not return the existing Store orders."));
                    yield break;
                }
                StoreWatchNotify(progress, open);
                yield return StoreWatchDelay(StoreWatchRetrySeconds(open, null), cancelled);
                if (StoreWatchIsCancelled(cancelled))
                {
                    done?.Invoke(StoreWatchLocalFailure("SHOP_WATCH_CANCELLED", "The local Store delivery watch was stopped. The recorded order was not cancelled."));
                    yield break;
                }
            }
            if (StoreWatchIsCancelled(cancelled))
            {
                done?.Invoke(StoreWatchLocalFailure("SHOP_WATCH_CANCELLED", "The local Store delivery watch was stopped. The recorded order was not cancelled."));
                yield break;
            }
            var openBody = StoreWatchParse<BlockmakerStoreOpenCommitsResponse>(open);
            if (openBody == null || openBody.commits == null || openBody.unpaidIntents == null)
            {
                done?.Invoke(StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "Blockmaker returned an invalid Store order list."));
                yield break;
            }
            BlockmakerStoreOpenCommit paid = null;
            BlockmakerStoreOpenCommit unpaid = null;
            var matches = 0;
            foreach (var candidate in openBody.commits)
            {
                if (candidate != null && candidate.commitId == exactCommitId)
                {
                    paid = candidate;
                    matches++;
                }
            }
            foreach (var candidate in openBody.unpaidIntents)
            {
                if (candidate != null && candidate.commitId == exactCommitId)
                {
                    unpaid = candidate;
                    matches++;
                }
            }
            if (matches > 1)
            {
                done?.Invoke(StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "Blockmaker returned the Store order more than once."));
                yield break;
            }
            if (unpaid != null)
            {
                done?.Invoke(StoreWatchLocalFailure("SHOP_UNSIGNED_INTENT", "This Store intent has no observed signature or submission attempt. Nothing needs delivery; start a new purchase when ready.", 409));
                yield break;
            }

            var expectedTxId = paid != null ? paid.expectedTxId : null;
            if (!string.IsNullOrEmpty(expectedTxId)
                && !System.Text.RegularExpressions.Regex.IsMatch(expectedTxId, "^[A-Z2-7]{52}$"))
            {
                done?.Invoke(StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "The existing Store payment transaction ID is invalid."));
                yield break;
            }
            var needsConfirmation = paid != null && paid.status == "pending";

            while (true)
            {
                if (StoreWatchIsCancelled(cancelled))
                {
                    done?.Invoke(StoreWatchLocalFailure("SHOP_WATCH_CANCELLED", "The local Store delivery watch was stopped. The recorded order was not cancelled."));
                    yield break;
                }

                if (needsConfirmation)
                {
                    BlockmakerResponse confirmation = null;
                    yield return ConfirmStoreCommit(exactCommitId, expectedTxId, response => confirmation = response);
                    if (StoreWatchIsCancelled(cancelled))
                    {
                        done?.Invoke(StoreWatchLocalFailure("SHOP_WATCH_CANCELLED", "The local Store delivery watch was stopped. The recorded order was not cancelled."));
                        yield break;
                    }
                    var confirmationBody = StoreWatchParse<BlockmakerStoreDeliveryResponse>(confirmation);
                    if (confirmation != null && confirmation.Code == "SHOP_CONFIRMATION_PENDING" && confirmation.Status == 202)
                    {
                        StoreWatchNotify(progress, confirmation);
                        yield return StoreWatchDelay(StoreWatchRetrySeconds(confirmation, confirmationBody), cancelled);
                        continue;
                    }
                    if (StoreWatchIsTransient(confirmation))
                    {
                        StoreWatchNotify(progress, confirmation);
                        yield return StoreWatchDelay(StoreWatchRetrySeconds(confirmation, confirmationBody), cancelled);
                        continue;
                    }
                    if (confirmation == null || !confirmation.Success)
                    {
                        done?.Invoke(confirmation ?? StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "Blockmaker did not return a Store confirmation."));
                        yield break;
                    }
                    if (confirmationBody == null || confirmationBody.commitId != exactCommitId
                        || !System.Text.RegularExpressions.Regex.IsMatch(confirmationBody.txId ?? "", "^[A-Z2-7]{52}$")
                        || (!string.IsNullOrEmpty(expectedTxId) && confirmationBody.txId != expectedTxId))
                    {
                        done?.Invoke(StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "Blockmaker did not confirm the exact existing Store payment."));
                        yield break;
                    }
                    expectedTxId = confirmationBody.txId;
                    needsConfirmation = false;
                }

                BlockmakerResponse reveal = null;
                yield return RevealStoreCommit(exactCommitId, response => reveal = response);
                if (StoreWatchIsCancelled(cancelled))
                {
                    done?.Invoke(StoreWatchLocalFailure("SHOP_WATCH_CANCELLED", "The local Store delivery watch was stopped. The recorded order was not cancelled."));
                    yield break;
                }
                var revealBody = StoreWatchParse<BlockmakerStoreDeliveryResponse>(reveal);
                if (reveal != null && reveal.Code == "SHOP_CONFIRMATION_PENDING" && reveal.Status == 202)
                {
                    StoreWatchNotify(progress, reveal);
                    needsConfirmation = true;
                    yield return StoreWatchDelay(StoreWatchRetrySeconds(reveal, revealBody), cancelled);
                    continue;
                }
                if (StoreWatchIsTransient(reveal))
                {
                    StoreWatchNotify(progress, reveal);
                    yield return StoreWatchDelay(StoreWatchRetrySeconds(reveal, revealBody), cancelled);
                    continue;
                }
                if (revealBody != null && reveal.Success && revealBody.status == "distributed"
                    && revealBody.items != null && revealBody.parts != null)
                {
                    done?.Invoke(reveal);
                    yield break;
                }
                if (revealBody != null && reveal.Success
                    && (revealBody.status == "delivering" || revealBody.status == "confirming_delivery")
                    && revealBody.items != null && revealBody.parts != null && revealBody.pendingAssetIds != null)
                {
                    StoreWatchNotify(progress, reveal);
                    yield return StoreWatchDelay(StoreWatchRetrySeconds(reveal, revealBody), cancelled);
                    continue;
                }
                if (revealBody != null && !reveal.Success
                    && revealBody.status == "awaiting_opt_in"
                    && reveal.Code == "SHOP_ASSET_OPT_IN_REQUIRED"
                    && revealBody.assetIds != null && revealBody.items != null && revealBody.deliveredParts != null)
                {
                    done?.Invoke(reveal);
                    yield break;
                }
                if (revealBody != null && !reveal.Success && revealBody.status == "blocked"
                    && revealBody.items != null && revealBody.deliveredParts != null)
                {
                    done?.Invoke(reveal);
                    yield break;
                }
                done?.Invoke(reveal != null && !reveal.Success
                    ? reveal
                    : StoreWatchLocalFailure("SHOP_RECOVERY_RESPONSE_INVALID", "Blockmaker returned an unknown Store delivery state."));
                yield break;
            }
        }

        private static T StoreWatchParse<T>(BlockmakerResponse response) where T : class
        {
            if (response == null) return null;
            try { return response.Parse<T>(); }
            catch { return null; }
        }

        private static bool StoreWatchIsTransient(BlockmakerResponse response)
        {
            if (response == null) return false;
            if (response.Status == 408 || response.Status == 425
                || response.Status == 429 || response.Status == 502
                || response.Status == 504) return true;
            return response.Code == "NETWORK_ERROR"
                || response.Code == "TIMEOUT"
                || (response.Code == "REQUEST_FAILED"
                    && (response.Status == 0 || response.Status >= 500))
                || response.Code == "SHOP_SERVICE_UNAVAILABLE"
                || response.Code == "SHOP_CONFIRMATION_UNAVAILABLE";
        }

        private static void StoreWatchNotify(Action<BlockmakerResponse> progress, BlockmakerResponse response)
        {
            if (progress == null) return;
            try { progress(response); }
            catch { /* Observer errors never alter payment reconciliation. */ }
        }

        private static bool StoreWatchIsCancelled(Func<bool> cancelled)
        {
            if (cancelled == null) return false;
            try { return cancelled(); }
            catch { return true; }
        }

        private static int StoreWatchRetrySeconds(BlockmakerResponse response, BlockmakerStoreDeliveryResponse body)
        {
            var requested = response != null && response.RetryAfterSeconds.HasValue
                ? response.RetryAfterSeconds.Value
                : body != null ? body.retryAfterSeconds : 3;
            return Math.Max(2, Math.Min(30, requested > 0 ? requested : 3));
        }

        private static IEnumerator StoreWatchDelay(int seconds, Func<bool> cancelled)
        {
            var until = Time.realtimeSinceStartup + Math.Max(2, Math.Min(30, seconds));
            while (Time.realtimeSinceStartup < until)
            {
                if (StoreWatchIsCancelled(cancelled)) yield break;
                yield return null;
            }
        }

        private static BlockmakerResponse StoreWatchLocalFailure(string code, string error, long status = 0)
        {
            return new BlockmakerResponse { Success = false, Status = status, Code = code, Error = error };
        }

        private static string ValidateStoreCommitId(string value)
        {
            var commitId = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                commitId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$"))
                throw new ArgumentException("Use the existing Store commit ID returned by Blockmaker.", nameof(value));
            return commitId;
        }

        private static string[] ValidateStoreSignedTransactions(string[] values, int maximum)
        {
            if (values == null || values.Length < 1 || values.Length > maximum)
                throw new ArgumentException("Use the complete signed Store transaction group returned by the wallet.", nameof(values));
            var result = new string[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                if (string.IsNullOrEmpty(value) || value.Length > 10000 || value != value.Trim())
                    throw new ArgumentException("One signed Store transaction is empty or too large.", nameof(values));
                byte[] bytes;
                try { bytes = Convert.FromBase64String(value); }
                catch { throw new ArgumentException("One signed Store transaction is not canonical base64.", nameof(values)); }
                if (bytes.Length == 0 || Convert.ToBase64String(bytes) != value)
                    throw new ArgumentException("One signed Store transaction is not canonical base64.", nameof(values));
                result[index] = value;
            }
            return result;
        }

        // ── Game-owned NFT marketplace ─────────────────────────────────────
        // These methods never sign or hold a key. Blockmaker builds the exact
        // atomic group; the game's Pera/Defly/Lute/Magic/xChain bridge signs it;
        // SubmitMarketplaceTransactions broadcasts the already-signed bytes.
        public IEnumerator GetMarketplaceConfig(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-marketplace/config", "GET", null, false, done);
        }

        public IEnumerator GetMarketplaceListings(string collection, string search, string sort, Action<BlockmakerResponse> done)
        {
            var path = "/v1/game-marketplace/listings";
            var separator = "?";
            if (!string.IsNullOrWhiteSpace(collection)) { path += separator + "collection=" + UnityWebRequest.EscapeURL(collection.Trim()); separator = "&"; }
            if (!string.IsNullOrWhiteSpace(search)) { path += separator + "q=" + UnityWebRequest.EscapeURL(search.Trim()); separator = "&"; }
            if (!string.IsNullOrWhiteSpace(sort)) path += separator + "sort=" + UnityWebRequest.EscapeURL(sort.Trim());
            return Request(path, "GET", null, false, done);
        }

        public IEnumerator GetMarketplaceListing(long assetId, Action<BlockmakerResponse> done)
        {
            if (assetId <= 0) throw new ArgumentException("assetId must be positive.", nameof(assetId));
            return Request("/v1/game-marketplace/listing/" + assetId, "GET", null, false, done);
        }

        public IEnumerator GetMarketplaceInventory(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-marketplace/inventory", "GET", null, true, done);
        }

        public IEnumerator BuildMarketplaceList(long assetId, long priceMicroalgo, Action<BlockmakerResponse> done)
        {
            if (assetId <= 0) throw new ArgumentException("assetId must be positive.", nameof(assetId));
            if (priceMicroalgo <= 0) throw new ArgumentException("priceMicroalgo must be positive.", nameof(priceMicroalgo));
            return Request("/v1/game-marketplace/list/build", "POST",
                JsonUtility.ToJson(new MarketplaceListRequest { assetId = assetId, priceMicroalgo = priceMicroalgo }), true, done);
        }

        public IEnumerator BuildMarketplaceBuy(long assetId, Action<BlockmakerResponse> done)
        {
            if (assetId <= 0) throw new ArgumentException("assetId must be positive.", nameof(assetId));
            return Request("/v1/game-marketplace/buy/build", "POST",
                JsonUtility.ToJson(new MarketplaceAssetRequest { assetId = assetId }), true, done);
        }

        public IEnumerator BuildMarketplaceCancel(long assetId, Action<BlockmakerResponse> done)
        {
            if (assetId <= 0) throw new ArgumentException("assetId must be positive.", nameof(assetId));
            return Request("/v1/game-marketplace/cancel/build", "POST",
                JsonUtility.ToJson(new MarketplaceAssetRequest { assetId = assetId }), true, done);
        }

        // Managed Blockmaker email wallets can sign the exact HMAC-authorized
        // builder output server-side. Magic and xChain wallets instead use their
        // own injected signing bridge and should skip this method.
        public IEnumerator SignManagedMarketplaceTransactions(
            string[] unsignedTxnsBase64, string signingIntent, Action<BlockmakerResponse> done)
        {
            if (unsignedTxnsBase64 == null || unsignedTxnsBase64.Length == 0 || string.IsNullOrWhiteSpace(signingIntent))
                throw new ArgumentException("Use the exact transactions and signing intent returned by a marketplace build method.");
            return Request("/v1/auth/sign", "POST", JsonUtility.ToJson(new MarketplaceSignRequest {
                unsignedTxnsBase64 = unsignedTxnsBase64, signingIntent = signingIntent, gameId = gameId
            }), true, done);
        }

        public IEnumerator SubmitMarketplaceTransactions(
            string[] signedTxnsBase64, string signingIntent, Action<BlockmakerResponse> done)
        {
            if (signedTxnsBase64 == null || signedTxnsBase64.Length == 0 || string.IsNullOrWhiteSpace(signingIntent))
                throw new ArgumentException("Use the signed transactions and signing intent from the same marketplace build response.");
            return Request("/v1/game-marketplace/submit", "POST",
                JsonUtility.ToJson(new MarketplaceSubmitRequest {
                    signedTxnsBase64 = signedTxnsBase64, signingIntent = signingIntent
                }), true, done);
        }

        public IEnumerator ConfirmMarketplaceTransaction(string txId, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(txId)) throw new ArgumentException("txId is required.", nameof(txId));
            return Request("/v1/game-marketplace/confirm/" + UnityWebRequest.EscapeURL(txId.Trim().ToUpperInvariant()), "GET", null, true, done);
        }

        // Returns Blockmaker's tenant-bound on-chain verdict. Do not query an indexer
        // directly or recreate token/NFT collection matching inside the game build.
        public IEnumerator EvaluateGates(string[] gateKeys, Action<BlockmakerResponse> done)
        {
            if (gateKeys == null || gateKeys.Length == 0)
                throw new ArgumentException("At least one in-game gate key is required.", nameof(gateKeys));
            return Request("/v1/integrations/gating/evaluate", "POST",
                JsonUtility.ToJson(new GateEvaluationRequest { gateKeys = gateKeys }), true, done);
        }

        public IEnumerator GetLeaderboard(Action<BlockmakerResponse> done)
        {
            return Request("/v1/leaderboard", "GET", null, true, done);
        }

        // Generic game-scoped leaderboards. Competitive result submission is
        // intentionally absent from the Unity client; send verified results from
        // a trusted backend with the private Blockmaker server client.
        public IEnumerator GetGameLeaderboards(Action<BlockmakerResponse> done)
        {
            return Request("/v1/game-leaderboards/boards", "GET", null, true, done);
        }

        public IEnumerator GetGameLeaderboard(
            string boardKey, string window, int limit, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(boardKey))
                throw new ArgumentException("boardKey is required.", nameof(boardKey));
            string key = boardKey.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9][a-z0-9_-]{0,63}$"))
                throw new ArgumentException("boardKey is invalid.", nameof(boardKey));
            string selectedWindow = string.IsNullOrWhiteSpace(window) ? "all" : window.Trim().ToLowerInvariant();
            if (selectedWindow != "daily" && selectedWindow != "weekly" && selectedWindow != "seasonal" && selectedWindow != "all")
                throw new ArgumentException("window must be daily, weekly, seasonal or all.", nameof(window));
            int safeLimit = Math.Max(1, Math.Min(limit, 100));
            return Request(
                "/v1/game-leaderboards/" + UnityWebRequest.EscapeURL(key)
                + "?window=" + UnityWebRequest.EscapeURL(selectedWindow)
                + "&limit=" + safeLimit,
                "GET", null, true, done);
        }

        // Calendar campaigns, milestone progress and reward entitlements. Activity
        // writes and external reward fulfilment intentionally stay on a trusted
        // backend; a Unity/player build can only read public or player-bound data.
        public IEnumerator GetPublicCampaigns(Action<BlockmakerResponse> done)
        {
            return Request("/v1/campaigns/public", "GET", null, false, done);
        }

        public IEnumerator GetPublicCampaign(
            string campaignKey, string period, int limit, Action<BlockmakerResponse> done)
        {
            return GetCampaignInternal(campaignKey, period, limit, true, done);
        }

        public IEnumerator GetCampaign(
            string campaignKey, string period, int limit, Action<BlockmakerResponse> done)
        {
            return GetCampaignInternal(campaignKey, period, limit, false, done);
        }

        private IEnumerator GetCampaignInternal(
            string campaignKey, string period, int limit, bool publicRead, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(campaignKey))
                throw new ArgumentException("campaignKey is required.", nameof(campaignKey));
            string key = campaignKey.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9][a-z0-9_-]{0,63}$"))
                throw new ArgumentException("campaignKey is invalid.", nameof(campaignKey));
            string selectedPeriod = string.IsNullOrWhiteSpace(period) ? "current" : period.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(selectedPeriod, "^[A-Za-z0-9:._-]{1,80}$"))
                throw new ArgumentException("period is invalid.", nameof(period));
            int safeLimit = Math.Max(1, Math.Min(limit, 100));
            string prefix = publicRead ? "/v1/campaigns/public/" : "/v1/campaigns/";
            return Request(
                prefix + UnityWebRequest.EscapeURL(key)
                + "?period=" + UnityWebRequest.EscapeURL(selectedPeriod)
                + "&limit=" + safeLimit,
                "GET", null, !publicRead, done);
        }

        public IEnumerator GetCampaignEntitlements(
            string status, int limit, Action<BlockmakerResponse> done)
        {
            return GetCampaignEntitlements(status, limit, null, null, done);
        }

        public IEnumerator GetCampaignEntitlements(
            string status, int limit, string after, string campaignKey, Action<BlockmakerResponse> done)
        {
            string path = "/v1/campaigns/entitlements?limit=" + Math.Max(1, Math.Min(limit, 200));
            if (!string.IsNullOrWhiteSpace(status))
                path += "&status=" + UnityWebRequest.EscapeURL(status.Trim().ToLowerInvariant());
            if (!string.IsNullOrEmpty(after)) path += "&after=" + UnityWebRequest.EscapeURL(after);
            if (!string.IsNullOrEmpty(campaignKey)) path += "&campaignKey=" + UnityWebRequest.EscapeURL(campaignKey);
            return Request(path, "GET", null, true, done);
        }

        // Time-based token yield. Position ownership/rate writes and external
        // fulfilment are intentionally absent from this player build; those
        // operations require the private trusted-server client.
        public IEnumerator GetPublicYieldPrograms(Action<BlockmakerResponse> done)
        {
            return Request("/v1/yield/public", "GET", null, false, done);
        }

        public IEnumerator GetYieldPrograms(Action<BlockmakerResponse> done)
        {
            return Request("/v1/yield/programs", "GET", null, true, done);
        }

        public IEnumerator GetYieldBalance(string programKey, Action<BlockmakerResponse> done)
        {
            string key = ValidateYieldProgramKey(programKey);
            return Request(
                "/v1/yield/programs/" + UnityWebRequest.EscapeURL(key) + "/balance",
                "GET", null, true, done);
        }

        public IEnumerator CreateYieldClaim(
            string programKey, long amountBaseUnits, string idempotencyKey, Action<BlockmakerResponse> done)
        {
            string key = ValidateYieldProgramKey(programKey);
            if (amountBaseUnits <= 0)
                throw new ArgumentException("amountBaseUnits must be positive.", nameof(amountBaseUnits));
            if (string.IsNullOrWhiteSpace(idempotencyKey)
                || !System.Text.RegularExpressions.Regex.IsMatch(idempotencyKey.Trim(), "^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$"))
                throw new ArgumentException("idempotencyKey must be one stable 1-128 character identifier.", nameof(idempotencyKey));
            return Request(
                "/v1/yield/programs/" + UnityWebRequest.EscapeURL(key) + "/claims",
                "POST",
                JsonUtility.ToJson(new YieldClaimRequest {
                    amountBaseUnits = amountBaseUnits,
                    idempotencyKey = idempotencyKey.Trim()
                }),
                true, done);
        }

        public IEnumerator GetYieldClaims(
            string programKey, string status, int limit, Action<BlockmakerResponse> done)
        {
            string path = "/v1/yield/claims?limit=" + Math.Max(1, Math.Min(limit, 200));
            if (!string.IsNullOrWhiteSpace(programKey))
                path += "&programKey=" + UnityWebRequest.EscapeURL(ValidateYieldProgramKey(programKey));
            if (!string.IsNullOrWhiteSpace(status))
                path += "&status=" + UnityWebRequest.EscapeURL(status.Trim().ToLowerInvariant());
            return Request(path, "GET", null, true, done);
        }

        private static string ValidateYieldProgramKey(string programKey)
        {
            if (string.IsNullOrWhiteSpace(programKey))
                throw new ArgumentException("programKey is required.", nameof(programKey));
            string key = programKey.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9][a-z0-9_-]{0,63}$"))
                throw new ArgumentException("programKey is invalid.", nameof(programKey));
            return key;
        }

        public IEnumerator GetRewardInfo(Action<BlockmakerResponse> done)
        {
            return Request("/v1/rewards/info", "GET", null, true, done);
        }

        public IEnumerator GetEarnings(Action<BlockmakerResponse> done)
        {
            return Request("/v1/rewards/earnings", "GET", null, true, done);
        }

        // Public feature state for the optional beginner Get ALGO guide. The
        // response contains only fixed official Pera links and no player data.
        public IEnumerator GetFundingGuideConfig(Action<BlockmakerResponse> done)
        {
            return Request("/v1/funding-guide/config", "GET", null, false, done);
        }

        // Player-bound wallet address, account type, and current ALGO balance for
        // a game-owned funding-help screen. This is educational data only: it
        // neither creates a payment nor proves that a purchase completed.
        public IEnumerator GetFundingGuide(Action<BlockmakerResponse> done)
        {
            return Request("/v1/funding-guide/me", "GET", null, true, done);
        }

        public IEnumerator GetOnrampConfig(Action<BlockmakerResponse> done)
        {
            return Request("/v1/onramp/config", "GET", null, false, done);
        }

        // Returns a single-use (five-minute) hosted checkout URL locked to the
        // authenticated player's Algorand wallet. This release supports browser
        // and Unity WebGL checkout only; native apps need a separately approved
        // package integration. Closing the widget is never payment success.
        public IEnumerator CreateOnrampSession(string fiatCurrency, float fiatAmount, Action<BlockmakerResponse> done)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            done?.Invoke(new BlockmakerResponse {
                Success = false, Status = 0, Code = "ONRAMP_BROWSER_REQUIRED",
                Error = "Adding ALGO by card currently requires a browser or Unity WebGL build. Native apps need a separately approved provider package integration."
            });
            yield break;
#else
            if (string.IsNullOrWhiteSpace(fiatCurrency) || fiatCurrency.Trim().Length != 3)
                throw new ArgumentException("fiatCurrency must be a three-letter code such as USD, GBP, or EUR.", nameof(fiatCurrency));
            if (fiatAmount <= 0)
                throw new ArgumentException("fiatAmount must be positive.", nameof(fiatAmount));
            return Request("/v1/onramp/session", "POST",
                JsonUtility.ToJson(new OnrampSessionRequest { fiatCurrency = fiatCurrency.Trim().ToUpperInvariant(), fiatAmount = fiatAmount }),
                true, done);
#endif
        }

        public IEnumerator GetOnrampOrder(string orderId, Action<BlockmakerResponse> done)
        {
            if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("orderId is required.", nameof(orderId));
            return Request("/v1/onramp/orders/" + UnityWebRequest.EscapeURL(orderId.Trim()), "GET", null, true, done);
        }

        public IEnumerator Logout(Action<BlockmakerResponse> done = null)
        {
            BlockmakerResponse result = null;
            if (!string.IsNullOrEmpty(RefreshToken))
            {
                yield return Request("/v1/auth/logout", "POST",
                    JsonUtility.ToJson(new RefreshRequest { refreshToken = RefreshToken, gameId = gameId }),
                    false, response => result = response);
            }
            ClearPlayerSession();
            done?.Invoke(result ?? new BlockmakerResponse { Success = true, Status = 200, Json = "{\"success\":true}" });
        }

        /// <summary>
        /// Authenticated one-transmission transport for a caller-validated game
        /// route. It preserves the supplied method and body, requires an
        /// existing player session, and never refreshes, retries, or mutates
        /// that session. Prefer the typed Store wrappers for Store routes.
        /// </summary>
        public IEnumerator RequestPlayerOnce(
            string path,
            string method,
            string bodyJson,
            Action<BlockmakerPlayerRequestOnceResult> done)
        {
            RejectTypedStorePaymentRoute(path);
            return SendPlayerRequestOnce(path, method, bodyJson,
                Math.Max(35, TimeoutSeconds), done);
        }

        private void RejectTypedStorePaymentRoute(string path)
        {
            var resolved = BuildUrl(path);
            var normalized = resolved.AbsolutePath;
            try
            {
                for (var pass = 0; pass < 3; pass++)
                {
                    var decoded = Uri.UnescapeDataString(normalized);
                    if (decoded == normalized) break;
                    normalized = decoded;
                }
            }
            catch
            {
                throw new ArgumentException("Blockmaker request paths must use valid URL encoding.", nameof(path));
            }

            var segments = new System.Collections.Generic.List<string>();
            foreach (var segment in normalized.Split('/'))
            {
                if (string.IsNullOrEmpty(segment) || segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }
            normalized = "/" + string.Join("/", segments.ToArray());
            if (string.Equals(normalized, "/v1/pack-shop/submit", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Use SubmitStorePaymentOnce for the typed Store payment route.", nameof(path));
        }

        private IEnumerator SendPlayerRequestOnce(
            string path,
            string method,
            string bodyJson,
            int requestTimeoutSeconds,
            Action<BlockmakerPlayerRequestOnceResult> done)
        {
            // Run the same path/origin validation even when session preflight
            // fails, and retain SendOnce's game query/header binding below.
            BuildUrl(path);

            var sessionTokenAtStart = SessionToken;
            if (string.IsNullOrWhiteSpace(sessionTokenAtStart))
            {
                done?.Invoke(new BlockmakerPlayerRequestOnceResult {
                    Response = new BlockmakerResponse {
                        Success = false,
                        Status = 401,
                        Code = "AUTH_MISSING",
                        Error = "An existing Blockmaker player session is required."
                    },
                    TransmissionAttempted = false,
                    MayHaveApplied = false
                });
                yield break;
            }

            BlockmakerResponse response = null;
            yield return SendOnce(path, method, bodyJson, true,
                value => response = CopyResponse(value),
                Math.Max(35, requestTimeoutSeconds), sessionTokenAtStart);
            done?.Invoke(new BlockmakerPlayerRequestOnceResult {
                Response = response ?? new BlockmakerResponse {
                    Success = false,
                    Status = 0,
                    Code = "REQUEST_RESULT_MISSING",
                    Error = "The request completed without a response result."
                },
                TransmissionAttempted = true,
                MayHaveApplied = true
            });
        }

        private static BlockmakerStoreSubmissionOnceResult StoreSubmissionOnceResult(
            BlockmakerPlayerRequestOnceResult source)
        {
            return new BlockmakerStoreSubmissionOnceResult {
                Response = source == null ? null : source.Response,
                TransmissionAttempted = source != null && source.TransmissionAttempted,
                MayHaveApplied = source != null && source.MayHaveApplied
            };
        }

        private static BlockmakerResponse CopyResponse(BlockmakerResponse source)
        {
            if (source == null) return null;
            return new BlockmakerResponse {
                Success = source.Success,
                Status = source.Status,
                Code = source.Code,
                Error = source.Error,
                RequestId = source.RequestId,
                RetryAfterSeconds = source.RetryAfterSeconds,
                Json = source.Json
            };
        }

        // Advanced escape hatch. Paths are origin-locked so a session can never be
        // redirected to an absolute/foreign URL. A 401 refreshes once and retries.
        public IEnumerator Request(string path, string method, string bodyJson, bool playerAuth, Action<BlockmakerResponse> done)
        {
            return RequestWithTimeout(path, method, bodyJson, playerAuth, null, done);
        }

        private IEnumerator RequestWithTimeout(
            string path,
            string method,
            string bodyJson,
            bool playerAuth,
            int? requestTimeoutSeconds,
            Action<BlockmakerResponse> done)
        {
            BlockmakerResponse result = null;
            yield return SendOnce(path, method, bodyJson, playerAuth,
                response => result = response, requestTimeoutSeconds);

            if (playerAuth && result != null && result.Status == 401 && !string.IsNullOrEmpty(RefreshToken))
            {
                var failedRefreshToken = RefreshToken;
                BlockmakerResponse refresh = null;
                yield return RefreshSession(response => refresh = response);
                if (refresh != null && refresh.Success)
                    yield return SendOnce(path, method, bodyJson, true,
                        response => result = response, requestTimeoutSeconds);
                else if (RefreshToken == failedRefreshToken)
                    ClearPlayerSession();
            }
            done?.Invoke(result);
        }

        public IEnumerator RefreshSession(Action<BlockmakerResponse> done)
        {
            // Refresh tokens rotate on use. Concurrent Unity coroutines must wait
            // for one shared refresh instead of replaying the same token and
            // triggering server-side replay protection.
            if (refreshInProgress)
            {
                while (refreshInProgress) yield return null;
                done?.Invoke(sharedRefreshResult);
                yield break;
            }
            if (string.IsNullOrEmpty(RefreshToken))
            {
                done?.Invoke(new BlockmakerResponse { Success = false, Status = 401, Code = "AUTH_MISSING", Error = "No refresh token is available." });
                yield break;
            }

            var refreshTokenAtStart = RefreshToken;
            var sessionTokenAtStart = SessionToken;
            refreshInProgress = true;
            BlockmakerResponse result = null;
            yield return SendOnce("/v1/auth/refresh", "POST",
                JsonUtility.ToJson(new RefreshRequest { refreshToken = refreshTokenAtStart, gameId = gameId }),
                false, response => result = response);
            if (RefreshToken == refreshTokenAtStart) {
                AcceptLogin(result);
                if (result != null && result.Success)
                    PlayerSessionRefreshed?.Invoke(sessionTokenAtStart, refreshTokenAtStart);
            }
            sharedRefreshResult = result;
            refreshInProgress = false;
            done?.Invoke(result);
        }

        private void AcceptLogin(BlockmakerResponse response)
        {
            if (response == null || !response.Success) return;
            var auth = response.Parse<BlockmakerAuthResponse>();
            if (auth == null || string.IsNullOrEmpty(auth.sessionToken)) return;
            if (auth.sessionToken.StartsWith("sk_", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(auth.refreshToken) && auth.refreshToken.StartsWith("sk_", StringComparison.OrdinalIgnoreCase)))
                return;
            var nextRefresh = !string.IsNullOrEmpty(auth.refreshToken)
                ? auth.refreshToken : RefreshToken;
            var nextAddress = !string.IsNullOrEmpty(auth.walletAddress)
                ? auth.walletAddress : WalletAddress;
            var nextAccountKind = !string.IsNullOrEmpty(auth.accountKind)
                ? auth.accountKind : AccountKind;
            var nextAuthProvider = !string.IsNullOrEmpty(auth.authProvider)
                ? auth.authProvider : AuthProvider;
            try {
                SetPlayerSession(auth.sessionToken, nextRefresh, nextAddress,
                    nextAccountKind, nextAuthProvider);
            } catch {
                ClearPlayerSession();
            }
        }

        private Uri BuildUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/") || path.StartsWith("//")
                || path.IndexOf('#') >= 0)
                throw new ArgumentException("Blockmaker request paths must start with one / and cannot be absolute URLs or contain fragments.", nameof(path));
            Uri resolved;
            if (!Uri.TryCreate(apiOrigin, path, out resolved)
                || resolved.GetLeftPart(UriPartial.Authority) != apiOrigin.GetLeftPart(UriPartial.Authority))
                throw new ArgumentException("Blockmaker requests cannot leave the configured API origin.", nameof(path));
            if (HasQueryParameter(resolved.Query, "gameId"))
                throw new ArgumentException("Blockmaker request paths cannot supply gameId; the client binds it exactly once.", nameof(path));
            var separator = string.IsNullOrEmpty(resolved.Query) ? "?" : "&";
            return new Uri(resolved.AbsoluteUri + separator + "gameId=" + Uri.EscapeDataString(gameId));
        }

        private static bool HasQueryParameter(string query, string expectedName)
        {
            if (string.IsNullOrEmpty(query)) return false;
            foreach (var field in query.Substring(1).Split('&'))
            {
                var equals = field.IndexOf('=');
                var encodedName = equals >= 0 ? field.Substring(0, equals) : field;
                string name;
                try { name = Uri.UnescapeDataString(encodedName.Replace("+", " ")); }
                catch { return true; }
                if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private IEnumerator SendOnce(
            string path,
            string method,
            string bodyJson,
            bool playerAuth,
            Action<BlockmakerResponse> done,
            int? requestTimeoutSeconds = null,
            string playerSessionToken = null)
        {
            var url = BuildUrl(path);
            using (var request = new UnityWebRequest(url.AbsoluteUri, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Math.Max(1, requestTimeoutSeconds ?? TimeoutSeconds);
                request.redirectLimit = 0;
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("X-Blockmaker-Game", gameId);
#if UNITY_WEBGL && !UNITY_EDITOR
                request.SetRequestHeader("X-Blockmaker-Client", "unity-webgl");
#else
                request.SetRequestHeader("X-Blockmaker-Client", "unity-native");
#endif
                if (!string.IsNullOrEmpty(bodyJson))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                var authToken = playerSessionToken ?? SessionToken;
                if (playerAuth && !string.IsNullOrEmpty(authToken))
                    request.SetRequestHeader("Authorization", "Bearer " + authToken);

                yield return request.SendWebRequest();

                var json = request.downloadHandler != null ? request.downloadHandler.text : "";
                BlockmakerEnvelope envelope = null;
                try { if (!string.IsNullOrEmpty(json)) envelope = JsonUtility.FromJson<BlockmakerEnvelope>(json); }
                catch { /* non-JSON response is represented below */ }
                int retryAfter;
                var retryHeader = request.GetResponseHeader("Retry-After");
                var response = new BlockmakerResponse {
                    Success = request.responseCode >= 200 && request.responseCode < 300 && (envelope == null || envelope.success),
                    Status = request.responseCode,
                    Code = envelope != null && !string.IsNullOrEmpty(envelope.code) ? envelope.code : (request.result == UnityWebRequest.Result.ConnectionError ? "NETWORK_ERROR" : "REQUEST_FAILED"),
                    Error = envelope != null && !string.IsNullOrEmpty(envelope.error) ? envelope.error : request.error,
                    RequestId = envelope != null && !string.IsNullOrEmpty(envelope.requestId) ? envelope.requestId : request.GetResponseHeader("X-Request-ID"),
                    RetryAfterSeconds = int.TryParse(retryHeader, out retryAfter)
                        ? retryAfter
                        : envelope != null && envelope.retryAfterSeconds > 0 ? envelope.retryAfterSeconds : (int?)null,
                    Json = json,
                };
                done?.Invoke(response);
            }
        }
    }
}
