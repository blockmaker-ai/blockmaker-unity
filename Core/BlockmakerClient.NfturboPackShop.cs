using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Blockmaker
{
    /// <summary>
    /// NFTURBO-only Pack-Shop authentication and HTTP transport.
    ///
    /// The credential held here is intentionally memory-only, has no refresh token,
    /// is never returned to game code, and is attached only to the exact
    /// <c>/v1/pack-shop</c> route family. Generic Blockmaker requests continue to use
    /// the normal player session and can never observe or fall back to this token.
    /// </summary>
    public partial class BlockmakerClient
    {
        private const string NfturboPackShopRootPath = "/v1/pack-shop";
        private const string NfturboPackShopPublicInfoPath = "/v1/pack-shop/info";
        private const string NfturboPackShopChallengePath =
            "/v1/auth/wallet/nfturbo-pack-shop/challenge";
        private const string NfturboPackShopVerifyPath =
            "/v1/auth/wallet/nfturbo-pack-shop/verify";
        private const long NfturboPackShopExpiryMarginMilliseconds = 5_000;

        private sealed class NfturboPackShopSession
        {
            public string token;
            public string walletAddress;
            public string providerId;
            public string gameId;
            public string serverOrigin;
            public long expiresAt;
        }

        private sealed class NfturboPackShopAuthWaiter
        {
            public Action onSuccess;
            public Action<string> onError;
        }

        private NfturboPackShopSession _nfturboPackShopSession;
        private readonly List<NfturboPackShopAuthWaiter> _nfturboPackShopAuthWaiters =
            new List<NfturboPackShopAuthWaiter>();
        private bool _nfturboPackShopAuthInFlight;
        private int _nfturboPackShopAuthGeneration;
        private Coroutine _nfturboPackShopAuthCoroutine;
        private string _nfturboPackShopAuthWallet;
        private string _nfturboPackShopAuthProvider;
        private bool _nfturboPackShopWalletSignInFlight;
        private bool _nfturboPackShopLutePrimeReserved;
        private IBlockmakerIdentity _nfturboPackShopLutePrimeIdentity;

        /// <summary>
        /// True only when the current Pera/Lute identity has a still-live, exact
        /// NFTURBO Pack-Shop credential. This does not affect
        /// <see cref="HasBackendSession"/> and does not make other API routes usable.
        /// </summary>
        public bool HasNfturboPackShopSession
        {
            get
            {
                string ignored;
                return TryGetNfturboPackShopToken(out ignored);
            }
        }

        /// <summary>
        /// Reserve Lute's browser approval window directly from a player click.
        /// Pera needs no reservation and returns true. Call this before asynchronous
        /// Shop preparation when the current provider is Lute.
        /// </summary>
        public bool PrimeNfturboPackShopApprovalWindow()
        {
            WalletConnectIdentity identity;
            string providerId;
            string ignored;
            if (_nfturboPackShopLutePrimeReserved)
            {
                identity = BlockmakerAuth.Instance?.Identity as WalletConnectIdentity;
                if (TryNfturboPackShopProvider(identity, out providerId) &&
                    providerId == "lute" &&
                    identity == _nfturboPackShopLutePrimeIdentity) return true;
                CancelNfturboPackShopLutePrime();
            }
            if (!TryGetNfturboPackShopIdentity(out identity, out providerId, out ignored))
                return false;
            if (providerId != "lute") return true;
            bool primed = BlockmakerAuth.Instance != null &&
                BlockmakerAuth.Instance.PrimeWalletApprovalWindow();
            if (primed)
            {
                _nfturboPackShopLutePrimeReserved = true;
                _nfturboPackShopLutePrimeIdentity = identity;
            }
            return primed;
        }

        /// <summary>
        /// Atomically consume the approval-window reservation owned by the exact
        /// current Pera/Lute identity immediately before a game-owned
        /// <see cref="IBlockmakerIdentity.SignTransaction"/> or
        /// <see cref="IBlockmakerIdentity.SignTransactions"/> call. Pera is a
        /// successful no-op. For Lute this clears only the SDK marker; the browser
        /// window stays open for the sign that follows. A false result means the
        /// identity changed or no Lute window was reserved by a player click.
        /// </summary>
        public bool TryConsumeNfturboPackShopApprovalWindow(
            IBlockmakerIdentity expectedIdentity)
        {
            var current = BlockmakerAuth.Instance?.Identity as WalletConnectIdentity;
            string providerId;
            if (expectedIdentity == null || current != expectedIdentity ||
                !TryNfturboPackShopProvider(current, out providerId)) return false;
            if (providerId != "lute") return true;
            if (!_nfturboPackShopLutePrimeReserved ||
                _nfturboPackShopLutePrimeIdentity != expectedIdentity) return false;
            _nfturboPackShopLutePrimeReserved = false;
            _nfturboPackShopLutePrimeIdentity = null;
            return true;
        }

        /// <summary>
        /// Cancel an unused approval-window reservation owned by the exact current
        /// Pera/Lute identity. Pera is a successful no-op. For Lute this clears the
        /// marker and closes the reserved browser window; it never cancels a window
        /// already consumed by a signing call.
        /// </summary>
        public bool CancelUnusedNfturboPackShopApprovalWindow(
            IBlockmakerIdentity expectedIdentity)
        {
            var auth = BlockmakerAuth.Instance;
            var current = auth?.Identity as WalletConnectIdentity;
            string providerId;
            if (expectedIdentity == null || current != expectedIdentity ||
                !TryNfturboPackShopProvider(current, out providerId)) return false;
            if (providerId != "lute") return true;
            if (!_nfturboPackShopLutePrimeReserved)
                return _nfturboPackShopLutePrimeIdentity == null;
            if (_nfturboPackShopLutePrimeIdentity != expectedIdentity) return false;
            var owner = _nfturboPackShopLutePrimeIdentity;
            if (owner == null) return false;
            _nfturboPackShopLutePrimeReserved = false;
            _nfturboPackShopLutePrimeIdentity = null;
            auth.CancelLuteWalletApprovalWindow(owner);
            return true;
        }

        /// <summary>
        /// Acquire a short-lived Pack-Shop-only credential by signing the exact
        /// harmless transaction returned by the scoped challenge endpoint. Pera and
        /// Lute sign those returned bytes directly; this method never calls the
        /// generic transaction builder.
        /// </summary>
        public void EnsureNfturboPackShopSession(
            Action onSuccess,
            Action<string> onError = null)
        {
            if (!RequireReady(onError)) return;
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                SafeInvokeNfturboPackShop(onError,
                    "NFTURBO Store wallet access is available in the WebGL game.");
                return;
            }

            string existingToken;
            if (TryGetNfturboPackShopToken(out existingToken))
            {
                SafeInvokeNfturboPackShop(onSuccess);
                return;
            }

            WalletConnectIdentity identity;
            string providerId;
            string identityError;
            if (!TryGetNfturboPackShopIdentity(out identity, out providerId, out identityError))
            {
                SafeInvokeNfturboPackShop(onError, identityError);
                return;
            }
            string wallet = identity.Address.Trim().ToUpperInvariant();

            if (_nfturboPackShopAuthInFlight)
            {
                if (string.Equals(_nfturboPackShopAuthWallet, wallet, StringComparison.Ordinal) &&
                    string.Equals(_nfturboPackShopAuthProvider, providerId, StringComparison.Ordinal))
                {
                    _nfturboPackShopAuthWaiters.Add(new NfturboPackShopAuthWaiter
                        { onSuccess = onSuccess, onError = onError });
                }
                else
                {
                    SafeInvokeNfturboPackShop(onError,
                        "A different wallet is already preparing NFTURBO Store access. Please wait, then try again.");
                }
                return;
            }

            var auth = BlockmakerAuth.Instance;
            if (auth == null || auth.Identity != identity)
            {
                SafeInvokeNfturboPackShop(onError,
                    "Reconnect Pera or Lute before opening the NFTURBO Store.");
                return;
            }
            if (!auth.PrepareNfturboPackShopSigning(identity, out identityError))
            {
                SafeInvokeNfturboPackShop(onError, identityError);
                return;
            }
            if (providerId == "lute" && !PrimeNfturboPackShopApprovalWindow())
            {
                SafeInvokeNfturboPackShop(onError,
                    "Your browser blocked Lute. Allow popups for this game, then try the NFTURBO Store again.");
                return;
            }

            ResetNfturboPackShopSession();
            _nfturboPackShopAuthInFlight = true;
            _nfturboPackShopAuthWallet = wallet;
            _nfturboPackShopAuthProvider = providerId;
            _nfturboPackShopAuthWaiters.Add(new NfturboPackShopAuthWaiter
                { onSuccess = onSuccess, onError = onError });
            int generation = ++_nfturboPackShopAuthGeneration;
            _nfturboPackShopAuthCoroutine = StartCoroutine(
                AcquireNfturboPackShopSession(identity, wallet, providerId, generation));
        }

        /// <summary>
        /// Forget the scoped credential and cancel only a scoped authentication
        /// attempt. The normal Blockmaker player session and wallet connection remain
        /// untouched.
        /// </summary>
        public void ClearNfturboPackShopSession()
        {
            ResetNfturboPackShopSession();
            if (!_nfturboPackShopAuthInFlight)
            {
                CancelNfturboPackShopLutePrime();
                return;
            }

            _nfturboPackShopAuthGeneration++;
            var auth = BlockmakerAuth.Instance;
            if (_nfturboPackShopWalletSignInFlight)
                auth?.CancelNfturboPackShopSigning();
            CancelNfturboPackShopLutePrime();
            var coroutine = _nfturboPackShopAuthCoroutine;
            _nfturboPackShopAuthCoroutine = null;
            _nfturboPackShopAuthInFlight = false;
            _nfturboPackShopAuthWallet = null;
            _nfturboPackShopAuthProvider = null;
            _nfturboPackShopWalletSignInFlight = false;
            if (coroutine != null) StopCoroutine(coroutine);
            CompleteNfturboPackShopAuthWaiters(false,
                "NFTURBO Store sign-in was cancelled because the connected wallet changed.");
        }

        /// <summary>
        /// POST to an exact production Pack-Shop route with only the scoped Shop
        /// credential. No normal player token or Editor API key fallback is allowed.
        /// </summary>
        public void PostNfturboPackShop<TReq, TRes>(
            string path,
            TReq body,
            Action<TRes> onSuccess = null,
            Action<string> onError = null) where TRes : class
        {
            PostNfturboPackShop(path, body, config.defaultTimeoutSeconds, onSuccess, onError);
        }

        /// <summary>POST to an exact production Pack-Shop route with an explicit timeout.</summary>
        public void PostNfturboPackShop<TReq, TRes>(
            string path,
            TReq body,
            float timeoutSeconds,
            Action<TRes> onSuccess = null,
            Action<string> onError = null) where TRes : class
        {
            string url;
            string token;
            if (!TryPrepareNfturboPackShopRequest(path, out url, out token, onError)) return;
            StartCoroutine(PostNfturboPackShopJson(
                url, body, token, timeoutSeconds, onSuccess, onError));
        }

        /// <summary>
        /// GET an exact production Pack-Shop route with only the scoped Shop
        /// credential. The caller supplies any wallet query explicitly; unlike the
        /// generic SDK GET helper this method never rewrites the path.
        /// </summary>
        public void GetNfturboPackShop<TRes>(
            string path,
            Action<TRes> onSuccess,
            Action<string> onError = null) where TRes : class
        {
            string url;
            string token;
            if (!TryPrepareNfturboPackShopRequest(path, out url, out token, onError)) return;
            StartCoroutine(GetNfturboPackShopJson(
                url, token, config.defaultTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>
        /// Read the one public NFTURBO Pack-Shop catalogue endpoint without any
        /// generic player credential, scoped credential, refresh token, or Editor
        /// API-key fallback. Redirects are disabled so even future header additions
        /// cannot be replayed off-origin. No other route is accepted.
        /// </summary>
        public void GetNfturboPackShopPublic<TRes>(
            string path,
            Action<TRes> onSuccess,
            Action<string> onError = null) where TRes : class
        {
            string url;
            if (!RequireReady(onError)) return;
            if (!string.Equals(path, NfturboPackShopPublicInfoPath,
                    StringComparison.Ordinal) || !TryBuildApiUrl(path, out url))
            {
                SafeInvokeNfturboPackShop(onError,
                    "Only the public NFTURBO Store catalogue can be requested without Store access.");
                return;
            }
            StartCoroutine(GetNfturboPackShopPublicJson(
                url, config.defaultTimeoutSeconds, onSuccess, onError));
        }

        /// <summary>
        /// Prepare the still-required, commit-bound ASA opt-ins for a paid NFTURBO
        /// Pack-Shop purchase. The scoped token supplies the wallet; it is never sent
        /// in this body and cannot be used with the generic transaction builder.
        /// </summary>
        public void PrepareNfturboPackShopOptIns(
            string commitId,
            long[] assetIds,
            Action<NfturboPackShopOptInPrepareResult> onSuccess,
            Action<string> onError = null)
        {
            PostNfturboPackShop<NfturboPackShopOptInPrepareRequest,
                NfturboPackShopOptInPrepareResult>(
                "/v1/pack-shop/opt-ins/prepare",
                new NfturboPackShopOptInPrepareRequest
                {
                    commitId = commitId,
                    assetIds = assetIds,
                },
                onSuccess,
                onError);
        }

        /// <summary>
        /// Submit one exact signed opt-in group prepared by
        /// <see cref="PrepareNfturboPackShopOptIns"/>. The opaque intent binds the
        /// bytes to the same commit, scoped wallet, and short-lived preparation.
        /// </summary>
        public void SubmitNfturboPackShopOptIns(
            string commitId,
            string[] signedTxnsBase64,
            string optInIntent,
            Action<NfturboPackShopOptInSubmitResult> onSuccess,
            Action<string> onError = null)
        {
            PostNfturboPackShop<NfturboPackShopOptInSubmitRequest,
                NfturboPackShopOptInSubmitResult>(
                "/v1/pack-shop/opt-ins/submit",
                new NfturboPackShopOptInSubmitRequest
                {
                    commitId = commitId,
                    signedTxnsBase64 = signedTxnsBase64,
                    optInIntent = optInIntent,
                },
                config.longRequestTimeoutSeconds,
                onSuccess,
                onError);
        }

        private IEnumerator AcquireNfturboPackShopSession(
            WalletConnectIdentity identity,
            string wallet,
            string providerId,
            int generation)
        {
            bool acquired = false;
            string failure = "NFTURBO Store sign-in could not be completed. Please try again.";
            try
            {
                var challengeRequest = new NfturboPackShopChallengeRequest
                {
                    walletAddress = wallet,
                    gameId = _gameId,
                    network = NfturboPackShopAuthContract.Network,
                    purpose = NfturboPackShopAuthContract.Purpose,
                    providerId = providerId,
                };
                NfturboPackShopChallengeResult challenge = null;
                string challengeError = null;
                using (var request = BuildNfturboPackShopAuthPost(
                    _baseUrl + NfturboPackShopChallengePath,
                    JsonUtility.ToJson(challengeRequest),
                    config.defaultTimeoutSeconds))
                {
                    yield return request.SendWebRequest();
                    if (!NfturboPackShopAuthContextIsCurrent(identity, wallet, providerId, generation))
                        yield break;
                    HandleResponse<NfturboPackShopChallengeResult>(request,
                        value => challenge = value,
                        value => challengeError = value,
                        null,
                        false);
                }
                if (challenge == null || !string.IsNullOrEmpty(challengeError) ||
                    !ValidateNfturboPackShopChallenge(
                        challenge, wallet, providerId, _gameId, out failure))
                {
                    if (!string.IsNullOrEmpty(challengeError)) failure = challengeError;
                    yield break;
                }

                string signedTransaction = null;
                string signingError = null;
                bool signingCallback = false;
                if (!TryConsumeNfturboPackShopApprovalWindow(identity))
                {
                    failure = "NFTURBO Store approval was not opened from the current player action. Please try again.";
                    yield break;
                }
                _nfturboPackShopWalletSignInFlight = true;
                yield return BlockmakerAuth.Instance.SignNfturboPackShopTransaction(
                    identity,
                    challenge.unsignedTxnBase64,
                    value => { signedTransaction = value; signingCallback = true; },
                    value => { signingError = value; signingCallback = true; });
                _nfturboPackShopWalletSignInFlight = false;
                if (!NfturboPackShopAuthContextIsCurrent(identity, wallet, providerId, generation))
                    yield break;
                if (!signingCallback || !string.IsNullOrEmpty(signingError) ||
                    !IsCanonicalNfturboPackShopBase64(signedTransaction, 4_096))
                {
                    failure = !string.IsNullOrEmpty(signingError)
                        ? signingError
                        : "The NFTURBO Store sign-in request was not approved in your wallet.";
                    yield break;
                }

                var verifyRequest = new NfturboPackShopVerifyRequest
                {
                    walletAddress = wallet,
                    gameId = _gameId,
                    network = NfturboPackShopAuthContract.Network,
                    purpose = NfturboPackShopAuthContract.Purpose,
                    providerId = providerId,
                    nonce = challenge.nonce,
                    signedTxn = signedTransaction,
                };
                NfturboPackShopVerifyResult verified = null;
                string verifyError = null;
                using (var request = BuildNfturboPackShopAuthPost(
                    _baseUrl + NfturboPackShopVerifyPath,
                    JsonUtility.ToJson(verifyRequest),
                    config.defaultTimeoutSeconds))
                {
                    yield return request.SendWebRequest();
                    if (!NfturboPackShopAuthContextIsCurrent(identity, wallet, providerId, generation))
                        yield break;
                    HandleResponse<NfturboPackShopVerifyResult>(request,
                        value => verified = value,
                        value => verifyError = value,
                        null,
                        false);
                }
                if (verified == null || !string.IsNullOrEmpty(verifyError) ||
                    !ValidateNfturboPackShopVerification(
                        verified, wallet, providerId, _gameId, out failure))
                {
                    if (!string.IsNullOrEmpty(verifyError)) failure = verifyError;
                    yield break;
                }

                _nfturboPackShopSession = new NfturboPackShopSession
                {
                    token = verified.sessionToken,
                    walletAddress = wallet,
                    providerId = providerId,
                    gameId = _gameId,
                    serverOrigin = _baseUrl,
                    expiresAt = UnixTimeMilliseconds() +
                        verified.expiresInSeconds * 1_000L,
                };
                acquired = true;
            }
            finally
            {
                if (generation == _nfturboPackShopAuthGeneration)
                {
                    _nfturboPackShopWalletSignInFlight = false;
                    if (!acquired) CancelNfturboPackShopLutePrime();
                    FinishNfturboPackShopAuthentication(acquired, failure);
                }
            }
        }

        private IEnumerator PostNfturboPackShopJson<TReq, TRes>(
            string url,
            TReq payload,
            string token,
            float timeout,
            Action<TRes> onSuccess,
            Action<string> onError) where TRes : class
        {
            using (var request = BuildNfturboPackShopPost(
                url, JsonUtility.ToJson(payload), timeout, token))
            {
                yield return request.SendWebRequest();
                if (!NfturboPackShopTokenIsCurrent(token))
                {
                    SafeInvokeNfturboPackShop(onError,
                        "NFTURBO Store access changed with the connected wallet. Please try again.");
                    yield break;
                }
                if (request.responseCode == 401 || request.responseCode == 403)
                    InvalidateNfturboPackShopToken(token);
                HandleResponse(request, onSuccess, onError, null, false);
            }
        }

        private IEnumerator GetNfturboPackShopJson<TRes>(
            string url,
            string token,
            float timeout,
            Action<TRes> onSuccess,
            Action<string> onError) where TRes : class
        {
            using (var request = BuildNfturboPackShopGet(url, timeout, token))
            {
                yield return request.SendWebRequest();
                if (!NfturboPackShopTokenIsCurrent(token))
                {
                    SafeInvokeNfturboPackShop(onError,
                        "NFTURBO Store access changed with the connected wallet. Please try again.");
                    yield break;
                }
                if (request.responseCode == 401 || request.responseCode == 403)
                    InvalidateNfturboPackShopToken(token);
                HandleResponse(request, onSuccess, onError, null, false);
            }
        }

        private IEnumerator GetNfturboPackShopPublicJson<TRes>(
            string url,
            float timeout,
            Action<TRes> onSuccess,
            Action<string> onError) where TRes : class
        {
            using (var request = BuildNfturboPackShopPublicGet(url, timeout))
            {
                yield return request.SendWebRequest();
                HandleResponse(request, onSuccess, onError, null, false);
            }
        }

        private bool TryPrepareNfturboPackShopRequest(
            string path,
            out string url,
            out string token,
            Action<string> onError)
        {
            url = null;
            token = null;
            if (!RequireReady(onError)) return false;
            if (!IsExactNfturboPackShopPath(path) || !TryBuildApiUrl(path, out url))
            {
                SafeInvokeNfturboPackShop(onError,
                    "NFTURBO Store credentials can be used only with /v1/pack-shop routes.");
                return false;
            }
            if (!TryGetNfturboPackShopToken(out token))
            {
                SafeInvokeNfturboPackShop(onError,
                    "Approve NFTURBO Store access with Pera or Lute, then try again.");
                return false;
            }
            return true;
        }

        private UnityWebRequest BuildNfturboPackShopAuthPost(
            string url,
            string jsonBody,
            float timeout)
        {
            var request = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = SafeTimeout(timeout),
                redirectLimit = 0,
            };
            ApplyCommonHeaders(request, true);
            request.SetRequestHeader("X-Blockmaker-Client",
                NfturboPackShopAuthContract.ClientHeader);
            return request;
        }

        private UnityWebRequest BuildNfturboPackShopPost(
            string url,
            string jsonBody,
            float timeout,
            string token)
        {
            var request = BuildNfturboPackShopAuthPost(url, jsonBody, timeout);
            request.SetRequestHeader("Authorization", "Bearer " + token);
            return request;
        }

        private UnityWebRequest BuildNfturboPackShopGet(
            string url,
            float timeout,
            string token)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = SafeTimeout(timeout);
            request.redirectLimit = 0;
            ApplyCommonHeaders(request, false);
            request.SetRequestHeader("X-Blockmaker-Client",
                NfturboPackShopAuthContract.ClientHeader);
            request.SetRequestHeader("Authorization", "Bearer " + token);
            return request;
        }

        private UnityWebRequest BuildNfturboPackShopPublicGet(
            string url,
            float timeout)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = SafeTimeout(timeout);
            request.redirectLimit = 0;
            ApplyCommonHeaders(request, false);
            request.SetRequestHeader("X-Blockmaker-Client",
                NfturboPackShopAuthContract.ClientHeader);
            return request;
        }

        private bool NfturboPackShopAuthContextIsCurrent(
            WalletConnectIdentity identity,
            string wallet,
            string providerId,
            int generation)
        {
            if (!_nfturboPackShopAuthInFlight ||
                generation != _nfturboPackShopAuthGeneration ||
                BlockmakerAuth.Instance == null ||
                BlockmakerAuth.Instance.Identity != identity)
                return false;
            string currentProvider;
            return TryNfturboPackShopProvider(identity, out currentProvider) &&
                string.Equals(currentProvider, providerId, StringComparison.Ordinal) &&
                string.Equals(identity.Address.Trim().ToUpperInvariant(), wallet,
                    StringComparison.Ordinal);
        }

        private bool TryGetNfturboPackShopToken(out string token)
        {
            token = null;
            var session = _nfturboPackShopSession;
            if (session == null) return false;
            var auth = BlockmakerAuth.Instance;
            var identity = auth?.Identity as WalletConnectIdentity;
            string providerId;
            if (identity == null || !TryNfturboPackShopProvider(identity, out providerId) ||
                session.expiresAt <= UnixTimeMilliseconds() +
                    NfturboPackShopExpiryMarginMilliseconds ||
                !string.Equals(session.walletAddress,
                    identity.Address.Trim().ToUpperInvariant(), StringComparison.Ordinal) ||
                !string.Equals(session.providerId, providerId, StringComparison.Ordinal) ||
                !string.Equals(session.gameId, _gameId, StringComparison.Ordinal) ||
                !string.Equals(session.serverOrigin, _baseUrl, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(session.token))
            {
                ResetNfturboPackShopSession();
                return false;
            }
            token = session.token;
            return true;
        }

        private bool NfturboPackShopTokenIsCurrent(string token)
        {
            string current;
            return !string.IsNullOrEmpty(token) &&
                TryGetNfturboPackShopToken(out current) &&
                string.Equals(current, token, StringComparison.Ordinal);
        }

        private void InvalidateNfturboPackShopToken(string token)
        {
            if (_nfturboPackShopSession != null &&
                string.Equals(_nfturboPackShopSession.token, token, StringComparison.Ordinal))
                ResetNfturboPackShopSession();
        }

        private void ResetNfturboPackShopSession()
        {
            _nfturboPackShopSession = null;
        }

        private void CancelNfturboPackShopLutePrime()
        {
            if (!_nfturboPackShopLutePrimeReserved) return;
            var owner = _nfturboPackShopLutePrimeIdentity;
            _nfturboPackShopLutePrimeReserved = false;
            _nfturboPackShopLutePrimeIdentity = null;
            BlockmakerAuth.Instance?.CancelLuteWalletApprovalWindow(owner);
        }

        private static bool TryGetNfturboPackShopIdentity(
            out WalletConnectIdentity identity,
            out string providerId,
            out string error)
        {
            identity = BlockmakerAuth.Instance?.Identity as WalletConnectIdentity;
            providerId = null;
            error = null;
            if (identity == null || !TryNfturboPackShopProvider(identity, out providerId))
            {
                error = "Connect an Algorand account through Pera or Lute before opening the NFTURBO Store.";
                return false;
            }
            if (!identity.CanSign)
            {
                error = "Reconnect your Pera or Lute wallet before opening the NFTURBO Store.";
                return false;
            }
            return true;
        }

        private static bool TryNfturboPackShopProvider(
            WalletConnectIdentity identity,
            out string providerId)
        {
            if (identity is PeraIdentity &&
                string.Equals(identity.ProviderName, BlockmakerAuth.ProviderPera,
                    StringComparison.Ordinal))
            {
                providerId = "pera";
                return true;
            }
            if (identity is LuteIdentity &&
                string.Equals(identity.ProviderName, BlockmakerAuth.ProviderLute,
                    StringComparison.Ordinal))
            {
                providerId = "lute";
                return true;
            }
            providerId = null;
            return false;
        }

        private void FinishNfturboPackShopAuthentication(bool success, string error)
        {
            _nfturboPackShopAuthInFlight = false;
            _nfturboPackShopAuthCoroutine = null;
            _nfturboPackShopAuthWallet = null;
            _nfturboPackShopAuthProvider = null;
            CompleteNfturboPackShopAuthWaiters(success, error);
        }

        private void CompleteNfturboPackShopAuthWaiters(bool success, string error)
        {
            var waiters = _nfturboPackShopAuthWaiters.ToArray();
            _nfturboPackShopAuthWaiters.Clear();
            foreach (var waiter in waiters)
            {
                if (success) SafeInvokeNfturboPackShop(waiter.onSuccess);
                else SafeInvokeNfturboPackShop(waiter.onError,
                    error ?? "NFTURBO Store sign-in could not be completed. Please try again.");
            }
        }

        private static void SafeInvokeNfturboPackShop(Action callback)
        {
            if (callback == null) return;
            try { callback.Invoke(); }
            catch (Exception error)
            {
                BlockmakerLog.Warning(
                    "[BlockmakerClient] NFTURBO Store callback failed: " + error.Message);
            }
        }

        private static void SafeInvokeNfturboPackShop<T>(Action<T> callback, T value)
        {
            if (callback == null) return;
            try { callback.Invoke(value); }
            catch (Exception error)
            {
                BlockmakerLog.Warning(
                    "[BlockmakerClient] NFTURBO Store callback failed: " + error.Message);
            }
        }

        private static bool IsExactNfturboPackShopPath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                path.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                path.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                path.IndexOf('\\') >= 0 || path.IndexOf('\r') >= 0 ||
                path.IndexOf('\n') >= 0 || path.IndexOf('#') >= 0) return false;
            int query = path.IndexOf('?');
            string route = query >= 0 ? path.Substring(0, query) : path;
            // Do not let an HTTP stack normalize an encoded slash/dot or a control
            // character after this route-family decision. Query values may remain
            // percent-encoded; the scoped credential is still sent to the same route.
            if (route.IndexOf('%') >= 0) return false;
            for (int i = 0; i < route.Length; i++)
                if (route[i] <= 0x20 || route[i] == 0x7f) return false;
            return string.Equals(route, NfturboPackShopRootPath,
                       StringComparison.Ordinal) ||
                route.StartsWith(NfturboPackShopRootPath + "/",
                    StringComparison.Ordinal);
        }

        private static bool ValidateNfturboPackShopChallenge(
            NfturboPackShopChallengeResult value,
            string wallet,
            string providerId,
            string gameId,
            out string error)
        {
            error = "NFTURBO Store returned an invalid sign-in request. Nothing was signed.";
            if (value == null || !value.success ||
                value.challengeVersion != NfturboPackShopAuthContract.ChallengeVersion ||
                value.purpose != NfturboPackShopAuthContract.Purpose ||
                value.network != NfturboPackShopAuthContract.Network ||
                value.clientKind != NfturboPackShopAuthContract.ClientKind ||
                value.gameId != gameId || value.providerId != providerId ||
                value.walletAddress != wallet ||
                !IsNfturboPackShopNonce(value.nonce) ||
                !IsAlgorandTransactionId(value.txId) ||
                value.firstValidRound <= 0 ||
                value.lastValidRound < value.firstValidRound ||
                value.lastValidRound - value.firstValidRound > 40 ||
                value.expiresAt <= 0 ||
                !IsCanonicalNfturboPackShopBase64(value.unsignedTxnBase64, 2_048))
                return false;

            long now = UnixTimeMilliseconds();
            if (value.expiresAt < now - 30_000 || value.expiresAt > now + 10 * 60_000)
                return false;
            if (!NfturboPackShopMessageMatches(value, wallet, providerId, gameId))
                return false;
            return NfturboPackShopTransactionValidator.IsExactChallengeTransaction(
                value.unsignedTxnBase64,
                wallet,
                value.message,
                value.firstValidRound,
                value.lastValidRound,
                value.txId);
        }

        private static bool ValidateNfturboPackShopVerification(
            NfturboPackShopVerifyResult value,
            string wallet,
            string providerId,
            string gameId,
            out string error)
        {
            error = "NFTURBO Store returned an invalid scoped session. Please try again.";
            return value != null && value.success &&
                value.sessionScope == NfturboPackShopAuthContract.Purpose &&
                value.network == NfturboPackShopAuthContract.Network &&
                value.gameId == gameId && value.walletAddress == wallet &&
                value.authProvider == providerId &&
                value.accountKind == "algorand_wallet" &&
                value.expiresInSeconds > 0 &&
                value.expiresInSeconds <= NfturboPackShopAuthContract.MaximumTtlSeconds &&
                IsPlausibleNfturboPackShopToken(value.sessionToken);
        }

        private static bool NfturboPackShopMessageMatches(
            NfturboPackShopChallengeResult value,
            string wallet,
            string providerId,
            string gameId)
        {
            if (string.IsNullOrEmpty(value.message) || value.message.Length > 1_024 ||
                string.IsNullOrEmpty(value.origin)) return false;
            Uri origin;
            if (!Uri.TryCreate(value.origin, UriKind.Absolute, out origin) ||
                origin.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(origin.UserInfo) ||
                (origin.AbsolutePath != "/" && origin.AbsolutePath != "") ||
                !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
                return false;

            string[] lines = value.message.Split('\n');
            if (lines.Length != 11 ||
                lines[0] != "Blockmaker NFTURBO Pack Shop sign-in" ||
                lines[1] != "purpose: " + NfturboPackShopAuthContract.Purpose ||
                lines[2] != "developer: " + gameId ||
                lines[3] != "network: " + NfturboPackShopAuthContract.Network ||
                lines[4] != "provider: " + providerId ||
                lines[5] != "address: " + wallet ||
                !IsLowerHexSha256Line(lines[6], "policy: ") ||
                lines[7] != "origin: " + value.origin ||
                lines[8] != "client: " + NfturboPackShopAuthContract.ClientKind ||
                lines[9] != "nonce: " + value.nonce ||
                lines[10] != "expires: " +
                    DateTimeOffset.FromUnixTimeMilliseconds(value.expiresAt)
                        .UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                            CultureInfo.InvariantCulture))
                return false;
            return true;
        }

        private static bool IsLowerHexSha256Line(string line, string prefix)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith(prefix, StringComparison.Ordinal) ||
                line.Length != prefix.Length + 64) return false;
            for (int i = prefix.Length; i < line.Length; i++)
            {
                char character = line[i];
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f')) return false;
            }
            return true;
        }

        private static bool IsNfturboPackShopNonce(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if ((character < 'a' || character > 'z') &&
                    (character < 'A' || character > 'Z') &&
                    (character < '0' || character > '9') &&
                    character != '-' && character != '_') return false;
            }
            return true;
        }

        private static bool IsAlgorandTransactionId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 52) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if ((character < 'A' || character > 'Z') &&
                    (character < '2' || character > '7')) return false;
            }
            return true;
        }

        private static bool IsCanonicalNfturboPackShopBase64(string value, int maximumBytes)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumBytes * 2) return false;
            try
            {
                byte[] decoded = Convert.FromBase64String(value);
                return decoded.Length > 0 && decoded.Length <= maximumBytes &&
                    Convert.ToBase64String(decoded) == value;
            }
            catch { return false; }
        }

        private static bool IsPlausibleNfturboPackShopToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 64 || value.Length > 4_096)
                return false;
            int dots = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == '.') { dots++; continue; }
                if ((character < 'a' || character > 'z') &&
                    (character < 'A' || character > 'Z') &&
                    (character < '0' || character > '9') &&
                    character != '-' && character != '_') return false;
            }
            return dots == 2;
        }
    }
}
