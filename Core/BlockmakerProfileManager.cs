using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Blockmaker
{

    /// <summary>
    /// Singleton that owns the current player's profile state and orchestrates
    /// all profile-mutating flows (claim/change username, onboarding steps).
    ///
    /// Subscribe to the static events to react to profile changes:
    ///   BlockmakerProfileManager.OnProfileLoaded
    ///   BlockmakerProfileManager.OnProfileUpdated
    ///   BlockmakerProfileManager.OnOnboardingStatusLoaded
    ///
    /// Read identity-derived display info via the static accessors:
    ///   BlockmakerProfileManager.Profile
    ///   BlockmakerProfileManager.DisplayName
    ///   BlockmakerProfileManager.ProfileImageUrl
    ///   BlockmakerProfileManager.HasUsername
    ///   BlockmakerProfileManager.WalletAddress
    ///   BlockmakerProfileManager.IsEmailTier
    ///   BlockmakerProfileManager.OnboardingStep
    /// </summary>
    public class BlockmakerProfileManager : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────────
        public static BlockmakerProfileManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            _profile = null;
            _onboarding = null;
            IsProfileLoading = false;
            OnProfileLoaded = null;
            OnProfileUpdated = null;
            OnOnboardingStatusLoaded = null;
        }

        // ── Events ─────────────────────────────────────────────────────────────────
        public static event Action<BlockmakerProfile>  OnProfileLoaded;
        public static event Action<BlockmakerProfile>  OnProfileUpdated;
        public static event Action<OnboardingStatus>   OnOnboardingStatusLoaded;

        // ── Cached state ───────────────────────────────────────────────────────────
        private static BlockmakerProfile _profile;
        private static OnboardingStatus  _onboarding;
        private Texture2D                _profileTexture;

        // ── Static accessors ───────────────────────────────────────────────────────
        public static BlockmakerProfile Profile          => _profile;
        public static OnboardingStatus  Onboarding       => _onboarding;
        public static bool IsProfileLoaded  => _profile != null;
        public static bool IsProfileLoading { get; private set; }

        public static string DisplayName =>
            _profile?.displayName
            ?? BlockmakerAuth.Instance?.DisplayName
            ?? "Guest";

        public static string ProfileImageUrl =>
            _profile?.profileImageUrl ?? string.Empty;

        /// <summary>Asset ID of the NFT set as the player's profile picture. 0 = not set.</summary>
        public static long ProfilePicAssetId =>
            _profile?.profilePicAssetId ?? 0;

        public static bool HasUsername =>
            !string.IsNullOrEmpty(_profile?.username);

        public static string WalletAddress =>
            _profile?.walletAddress
            ?? BlockmakerAuth.Instance?.Address
            ?? string.Empty;

        public static bool IsEmailTier =>
            string.Equals(_profile?.tier, "email", StringComparison.OrdinalIgnoreCase);

        public static string OnboardingStep =>
            _profile?.onboardingStep ?? "none";

        private static readonly List<TokenBalance> _emptyBalances = new List<TokenBalance>();
        public static List<TokenBalance> TokenBalances =>
            _onboarding?.tokenBalances ?? _emptyBalances;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
        }

        private void OnDestroy()
        {
            if (_profileTexture != null) { Destroy(_profileTexture); _profileTexture = null; }

            if (Instance == this)
            {
                _profile    = null;
                _onboarding = null;
                Instance = null;
            }
        }

        // ── Identity change ────────────────────────────────────────────────────────

        private int _generation;
        private Coroutine _profileCoroutine;
        private Coroutine _onboardingCoroutine;

        private void HandleIdentityChanged(IBlockmakerIdentity identity)
        {
            if (Instance != this) return;
            if (_profileCoroutine != null) { StopCoroutine(_profileCoroutine); _profileCoroutine = null; }
            if (_onboardingCoroutine != null) { StopCoroutine(_onboardingCoroutine); _onboardingCoroutine = null; }

            _generation++;
            _profile    = null;
            _onboarding = null;
            IsProfileLoading = false;
            _loadingTexture = false;
            _pendingTextureCallbacks.Clear();
            if (_profileTexture != null) { Destroy(_profileTexture); _profileTexture = null; }

            if (identity == null || identity.Tier == IdentityTier.Guest) return;
            if (!identity.HasWallet) return;
            if (!isActiveAndEnabled) return;

            _profileCoroutine    = StartCoroutine(LoadProfileRoutine(_generation));
            _onboardingCoroutine = StartCoroutine(LoadOnboardingStatusRoutine(_generation));
        }

        // ── Public refresh methods ─────────────────────────────────────────────────

        public void RefreshProfile()
        {
            if (BlockmakerAuth.Instance == null || !BlockmakerAuth.Instance.HasWallet) return;
            if (_profileCoroutine != null) StopCoroutine(_profileCoroutine);
            _profileCoroutine = StartCoroutine(LoadProfileRoutine(_generation));
        }

        public void RefreshOnboardingStatus()
        {
            if (BlockmakerAuth.Instance == null || !BlockmakerAuth.Instance.HasWallet) return;
            if (_onboardingCoroutine != null) StopCoroutine(_onboardingCoroutine);
            _onboardingCoroutine = StartCoroutine(LoadOnboardingStatusRoutine(_generation));
        }

        // ── Profile load ───────────────────────────────────────────────────────────

        private const float SERVER_TIMEOUT = 30f;

        private IEnumerator LoadProfileRoutine(int gen)
        {
            if (BlockmakerClient.Instance == null) { IsProfileLoading = false; yield break; }

            IsProfileLoading = true;
            bool done = false;

            BlockmakerClient.Instance.GetProfile(
                result =>
                {
                    if (gen != _generation) { done = true; return; }
                    if (result?.success == true && result.profile != null)
                    {
                        bool isFirst = (_profile == null);
                        _profile = result.profile;

                        if (isFirst) SafeInvoke(OnProfileLoaded, _profile);
                        else         SafeInvoke(OnProfileUpdated, _profile);
                    }
                    done = true;
                },
                err => { BlockmakerLog.Info($"[BlockmakerProfileManager] Profile load failed: {err}"); done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < SERVER_TIMEOUT)
            {
                if (gen != _generation) { IsProfileLoading = false; yield break; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            IsProfileLoading = false;
        }

        private IEnumerator LoadOnboardingStatusRoutine(int gen)
        {
            if (BlockmakerClient.Instance == null) yield break;

            bool done = false;

            BlockmakerClient.Instance.GetOnboardingStatus(
                result =>
                {
                    if (gen != _generation) { done = true; return; }
                    if (result?.success == true)
                    {
                        _onboarding = result;
                        SafeInvoke(OnOnboardingStatusLoaded, _onboarding);
                    }
                    done = true;
                },
                err => { BlockmakerLog.Info($"[BlockmakerProfileManager] Onboarding status load failed: {err}"); done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < SERVER_TIMEOUT)
            {
                if (gen != _generation) yield break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // ── Profile texture ────────────────────────────────────────────────────────

        private bool _loadingTexture;
        private readonly List<Action<Texture2D>> _pendingTextureCallbacks = new List<Action<Texture2D>>();

        /// <summary>
        /// Load the profile image as a Texture2D. Cached after first load.
        /// Callback receives null if no image is set or the load fails.
        /// </summary>
        public void LoadProfileTexture(Action<Texture2D> callback)
        {
            if (_profileTexture != null) { callback?.Invoke(_profileTexture); return; }

            if (_loadingTexture)
            {
                if (callback != null) _pendingTextureCallbacks.Add(callback);
                return;
            }

            string url = ProfileImageUrl;
            if (string.IsNullOrEmpty(url)) { callback?.Invoke(null); return; }

            if (callback != null) _pendingTextureCallbacks.Add(callback);
            _loadingTexture = true;
            StartCoroutine(LoadTextureRoutine(url, tex =>
            {
                _loadingTexture = false;
                if (url != ProfileImageUrl)
                {
                    if (tex != null) Destroy(tex);
                    _pendingTextureCallbacks.Clear();
                    return;
                }
                _profileTexture = tex;
                foreach (var cb in _pendingTextureCallbacks)
                    cb?.Invoke(tex);
                _pendingTextureCallbacks.Clear();
            }));
        }

        private IEnumerator LoadTextureRoutine(string url, Action<Texture2D> callback)
        {
            using var req = UnityWebRequestTexture.GetTexture(url);
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
                callback?.Invoke(DownloadHandlerTexture.GetContent(req));
            else
                callback?.Invoke(null);
        }

        // ── Username flow ──────────────────────────────────────────────────────────

        /// <summary>
        /// Full claim flow: prepare (server) → sign (wallet) → complete (server) → refresh.
        /// Requires BlockmakerAuth.Instance.CanSign == true.
        /// </summary>
        public void ClaimUsername(
            string             username,
            Action<BlockmakerProfile> onSuccess,
            Action<string>     onError = null)
        {
            StartCoroutine(ClaimUsernameRoutine(username, onSuccess, onError));
        }

        private IEnumerator ClaimUsernameRoutine(
            string                    username,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError)
        {
            int gen = _generation;

            var auth = BlockmakerAuth.Instance;
            if (auth == null || !auth.CanSign)
            {
                onError?.Invoke("Please connect a wallet to continue.");
                yield break;
            }
            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            // ── Step 1: prepare ────────────────────────────────────────────────────
            UsernamePrepareResult prepared = null;
            string prepareError = null;
            bool   prepDone     = false;

            BlockmakerClient.Instance.PrepareUsernameClaim(username,
                r => { prepared     = r;   prepDone = true; },
                e => { prepareError = e;   prepDone = true; }
            );

            float elapsed = 0f;
            while (!prepDone && elapsed < SERVER_TIMEOUT)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!prepDone) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) { onError?.Invoke("Your account changed during this action. Please try again."); yield break; }

            if (prepareError != null || prepared?.success != true)
            {
                onError?.Invoke(prepareError ?? prepared?.error ?? "We couldn't set up that username. Please try again.");
                yield break;
            }

            // ── Step 2: sign group ────────────────────────────────────────────────
            string[] signedTxns = null;
            string   signError  = null;
            bool     signDone   = false;

            var txnsToSign = (prepared.unsignedTxnsBase64 != null && prepared.unsignedTxnsBase64.Length > 0)
                ? prepared.unsignedTxnsBase64
                : new[] { prepared.unsignedTxnBase64 };
            yield return auth.Identity.SignTransactions(
                txnsToSign,
                s => { signedTxns = s; signDone = true; },
                e => { signError  = e; signDone = true; }
            );

            if (!signDone)
            {
                elapsed = 0f;
                while (!signDone && elapsed < 120f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            if (!signDone) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) { onError?.Invoke("Your account changed during this action. Please try again."); yield break; }

            if (signError != null || signedTxns == null || signedTxns.Length == 0)
            {
                onError?.Invoke(signError ?? "You cancelled the request. Please try again when you're ready.");
                yield break;
            }

            // ── Step 3: complete ───────────────────────────────────────────────────
            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            UsernameClaimResult claimResult = null;
            string claimError = null;
            bool   claimDone  = false;

            BlockmakerClient.Instance.CompleteUsernameClaim(
                prepared.reservationId,
                signedTxns,
                r => { claimResult = r;   claimDone = true; },
                e => { claimError  = e;   claimDone = true; }
            );

            elapsed = 0f;
            while (!claimDone && elapsed < SERVER_TIMEOUT)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!claimDone) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) { onError?.Invoke("Your account changed during this action. Please try again."); yield break; }

            if (claimError != null || claimResult?.success != true)
            {
                onError?.Invoke(claimError ?? claimResult?.error ?? "We couldn't finish claiming that username. Please try again.");
                yield break;
            }

            // ── Refresh and notify ─────────────────────────────────────────────────
            if (claimResult.profile != null)
            {
                _profile = claimResult.profile;
                SafeInvoke(OnProfileUpdated, _profile);
            }

            onSuccess?.Invoke(_profile ?? claimResult.profile);
        }

        /// <summary>
        /// Full change flow: prepare → sign → complete → refresh.
        /// Player must already have a username.
        /// </summary>
        public void ChangeUsername(
            string                    newUsername,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError = null)
        {
            StartCoroutine(ChangeUsernameRoutine(newUsername, onSuccess, onError));
        }

        private IEnumerator ChangeUsernameRoutine(
            string                    newUsername,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError)
        {
            int gen = _generation;

            var auth = BlockmakerAuth.Instance;
            if (auth == null || !auth.CanSign)
            {
                onError?.Invoke("Please connect a wallet to continue.");
                yield break;
            }
            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            // ── Step 1: prepare ────────────────────────────────────────────────────
            UsernamePrepareResult prepared = null;
            string prepareError = null;
            bool   prepDone     = false;

            BlockmakerClient.Instance.PrepareUsernameChange(newUsername,
                r => { prepared     = r; prepDone = true; },
                e => { prepareError = e; prepDone = true; }
            );

            float elapsed = 0f;
            while (!prepDone && elapsed < SERVER_TIMEOUT)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!prepDone) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) { onError?.Invoke("Your account changed during this action. Please try again."); yield break; }

            if (prepareError != null || prepared?.success != true)
            {
                onError?.Invoke(prepareError ?? prepared?.error ?? "We couldn't set up the username change. Please try again.");
                yield break;
            }

            // ── Step 2: sign group ────────────────────────────────────────────────
            string[] signedTxns = null;
            string   signError  = null;
            bool     signDone   = false;

            var txnsToSign = (prepared.unsignedTxnsBase64 != null && prepared.unsignedTxnsBase64.Length > 0)
                ? prepared.unsignedTxnsBase64
                : new[] { prepared.unsignedTxnBase64 };
            yield return auth.Identity.SignTransactions(
                txnsToSign,
                s => { signedTxns = s; signDone = true; },
                e => { signError  = e; signDone = true; }
            );

            if (!signDone)
            {
                elapsed = 0f;
                while (!signDone && elapsed < 120f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            if (!signDone) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) { onError?.Invoke("Your account changed during this action. Please try again."); yield break; }

            if (signError != null || signedTxns == null || signedTxns.Length == 0)
            {
                onError?.Invoke(signError ?? "You cancelled the request. Please try again when you're ready.");
                yield break;
            }

            // ── Step 3: complete ───────────────────────────────────────────────────
            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            UsernameClaimResult changeResult = null;
            string changeError = null;
            bool   changeDone  = false;

            BlockmakerClient.Instance.CompleteUsernameChange(
                prepared.reservationId,
                signedTxns,
                r => { changeResult = r;    changeDone = true; },
                e => { changeError  = e;    changeDone = true; }
            );

            elapsed = 0f;
            while (!changeDone && elapsed < SERVER_TIMEOUT)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!changeDone) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) { onError?.Invoke("Your account changed during this action. Please try again."); yield break; }

            if (changeError != null || changeResult?.success != true)
            {
                onError?.Invoke(changeError ?? changeResult?.error ?? "We couldn't finish changing your username. Please try again.");
                yield break;
            }

            if (changeResult.profile != null)
            {
                _profile = changeResult.profile;
                SafeInvoke(OnProfileUpdated, _profile);
            }

            onSuccess?.Invoke(_profile ?? changeResult.profile);
        }

        // ── Profile picture NFT ────────────────────────────────────────────────────

        /// <summary>
        /// Set the player's profile picture to an NFT they own.
        /// The server verifies ownership via the Algorand indexer, resolves the
        /// image URL from ARC metadata, and persists it on the profile.
        /// On success the cached texture is cleared so the new image loads fresh.
        /// </summary>
        public void SetProfilePicNft(
            long                      assetId,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError = null)
        {
            StartCoroutine(SetProfilePicNftRoutine(assetId, onSuccess, onError));
        }

        private IEnumerator SetProfilePicNftRoutine(
            long                      assetId,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError)
        {
            int gen = _generation;

            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            SetProfilePicResult result = null;
            string              error  = null;
            bool                done   = false;

            BlockmakerClient.Instance.SetProfilePicNft(assetId,
                r => { result = r; done = true; },
                e => { error  = e; done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < SERVER_TIMEOUT)
            {
                if (gen != _generation) yield break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!done) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) yield break;

            if (error != null || result?.success != true)
            {
                onError?.Invoke(error ?? result?.error ?? "We couldn't update your profile picture. Please try again.");
                yield break;
            }

            if (result.profile != null)
            {
                _profile        = result.profile;
                if (_profileTexture != null) { Destroy(_profileTexture); _profileTexture = null; }
                SafeInvoke(OnProfileUpdated, _profile);
            }

            onSuccess?.Invoke(_profile ?? result.profile);
        }

        // ── Default avatar ──────────────────────────────────────────────────────────

        public void SetDefaultAvatar(
            string                    avatarId,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError = null)
        {
            StartCoroutine(SetDefaultAvatarRoutine(avatarId, onSuccess, onError));
        }

        private IEnumerator SetDefaultAvatarRoutine(
            string                    avatarId,
            Action<BlockmakerProfile> onSuccess,
            Action<string>            onError)
        {
            int gen = _generation;

            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            SetProfilePicResult result = null;
            string              error  = null;
            bool                done   = false;

            BlockmakerClient.Instance.SetDefaultAvatar(avatarId,
                r => { result = r; done = true; },
                e => { error  = e; done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < SERVER_TIMEOUT)
            {
                if (gen != _generation) yield break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!done) { onError?.Invoke("The request timed out. Please check your connection and try again."); yield break; }
            if (gen != _generation) yield break;

            if (error != null || result?.success != true)
            {
                onError?.Invoke(error ?? result?.error ?? "We couldn't update your avatar. Please try again.");
                yield break;
            }

            if (result.profile != null)
            {
                _profile = result.profile;
                if (_profileTexture != null) { Destroy(_profileTexture); _profileTexture = null; }
                SafeInvoke(OnProfileUpdated, _profile);
            }

            onSuccess?.Invoke(_profile ?? result.profile);
        }

        // ── Safe event helpers ──────────────────────────────────────────────────────

        private static void SafeInvoke<T>(Action<T> handler, T arg)
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<T>)d).Invoke(arg); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        // ── Onboarding helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the current player should see an onboarding nudge.
        /// Only true for non-guest players who haven't completed onboarding.
        /// </summary>
        public static bool ShouldShowOnboardingNudge()
        {
            if (BlockmakerAuth.Instance == null) return false;
            if (BlockmakerAuth.Instance.Tier == IdentityTier.Guest) return false;
            var step = OnboardingStep;
            return step != "complete";
        }

        public static bool ShouldShowUsernameNudge()
        {
            var cfg = BlockmakerAuth.Instance?.blockmakerConfig;
            if (cfg != null && (!cfg.enableOnboardingNudges || !cfg.enableUsernames)) return false;
            if (BlockmakerAuth.Instance == null || BlockmakerAuth.Instance.Tier == IdentityTier.Guest) return false;
            return !HasUsername;
        }

        public static bool ShouldShowProfilePicNudge()
        {
            var cfg = BlockmakerAuth.Instance?.blockmakerConfig;
            if (cfg != null && (!cfg.enableOnboardingNudges || !cfg.enableProfilePictures)) return false;
            if (BlockmakerAuth.Instance == null || BlockmakerAuth.Instance.Tier == IdentityTier.Guest) return false;
            return ProfilePicAssetId == 0 && string.IsNullOrEmpty(ProfileImageUrl);
        }
    }

}