using System;
using System.Collections;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Scripting;

namespace Blockmaker
{

    /// <summary>
    /// Central auth manager. Holds the current IBlockmakerIdentity and manages
    /// the full identity lifecycle: guest start, login, session restore, upgrade,
    /// and logout.
    ///
    /// All game code accesses identity through here:
    ///   BlockmakerAuth.Instance.Identity
    ///   BlockmakerAuth.Instance.Address
    ///   BlockmakerAuth.Instance.CanSign
    ///
    /// Subscribe to OnIdentityChanged to react to login/logout/upgrade events.
    ///
    /// Wallet connection:
    ///   Pera — uses a native WalletConnect v1 client (WalletConnectV1Client)
    ///   that connects directly to Pera's bridge servers, no external SDK needed.
    ///   Defly — uses the Reown SDK (WalletConnect v2).
    ///   Lute — uses Lute's official browser/extension adapter in WebGL.
    ///   Pera and Defly generate a QR code via OnWalletQRReady for display in the UI;
    ///   Lute opens its own protected approval window.
    ///   After the user scans and approves, OnIdentityChanged fires with the address.
    ///
    ///   Requires a WalletConnect Project ID for Defly/EVM — get one free at:
    ///   https://cloud.walletconnect.com
    /// </summary>
    public class BlockmakerAuth : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────────
        public static BlockmakerAuth Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            OnIdentityChanged = null;
            OnIdentityUpgraded = null;
            OnAuthError = null;
            OnWalletQRReady = null;
            OnWalletAddressChanged = null;
            SessionRestoreSettled = false;
            OnSessionRestoreSettled = null;
            BlockmakerPrefs.InvalidatePrefix();
        }

        // ── Provider constants ─────────────────────────────────────────────────────
        public const string ProviderPera  = "Pera";
        public const string ProviderDefly = "Defly";
        public const string ProviderLute  = "Lute";

        // ── Events ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the current identity changes (login, logout, upgrade).
        /// <para><b>Warning:</b> This is a static event. Subscribers must unsubscribe in
        /// OnDestroy() to avoid leaks across scene loads.</para>
        /// </summary>
        public static event Action<IBlockmakerIdentity>              OnIdentityChanged;

        /// <summary>
        /// Fired when the player upgrades from a lower tier to a higher one.
        /// <para><b>Warning:</b> This is a static event. Subscribers must unsubscribe in
        /// OnDestroy() to avoid leaks across scene loads.</para>
        /// </summary>
        public static event Action<IBlockmakerIdentity, IdentityTier> OnIdentityUpgraded;

        /// <summary>
        /// Fired when an auth error occurs (wallet connection failed, session expired, etc.).
        /// <para><b>Warning:</b> This is a static event. Subscribers must unsubscribe in
        /// OnDestroy() to avoid leaks across scene loads.</para>
        /// </summary>
        public static event Action<string>                           OnAuthError;

        /// Non-error progress messages for the auth UI (e.g. "approve the sign-in
        /// request in your wallet") — fired at moments where the user must act
        /// somewhere OTHER than the game and would otherwise see nothing happen.
        public static event Action<string>                           OnAuthStatus;

        /// <summary>
        /// Fired when the QR code is ready to display.
        /// <para><b>Warning:</b> This is a static event. Subscribers must unsubscribe in
        /// OnDestroy() to avoid leaks across scene loads.</para>
        /// </summary>
        public static event Action<WalletQREventArgs> OnWalletQRReady;

        /// <summary>
        /// Fired when signing in causes a wallet address change.
        /// Subscribe to show a warning that assets at the old address
        /// won't be accessible through the new sign-in method.
        /// <para><b>Warning:</b> This is a static event. Subscribers must unsubscribe in
        /// OnDestroy() to avoid leaks across scene loads.</para>
        /// </summary>
        public static event Action<WalletAddressChangedEventArgs> OnWalletAddressChanged;

        // ── Boot-time session-restore resolution (additive; nothing in the SDK gates on it) ──

        /// <summary>
        /// True once the boot-time session-restore attempt has RESOLVED: the stored session's
        /// token was refreshed/verified (signed in), or a fresh user sign-in is required, or
        /// there was no stored session at all. Until this flips, the restored identity is a
        /// GUESS — UI should render a neutral "resolving" state rather than a stale identity
        /// (which may be logged out a moment later) or a premature SIGN IN button.
        /// A late-arriving success after this settles still fires OnIdentityChanged as usual.
        /// </summary>
        public static bool SessionRestoreSettled { get; private set; }

        /// <summary>
        /// Fired ONCE, when <see cref="SessionRestoreSettled"/> flips true. Subscribers that
        /// attach late should read the flag first — it may already be settled.
        /// <para><b>Warning:</b> This is a static event. Subscribers must unsubscribe in
        /// OnDestroy() to avoid leaks across scene loads.</para>
        /// </summary>
        public static event Action OnSessionRestoreSettled;

        private static void SettleSessionRestore()
        {
            if (SessionRestoreSettled) return;
            SessionRestoreSettled = true;
            var handler = OnSessionRestoreSettled;
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action)d).Invoke(); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        /// <summary>
        /// True when Magic email login is available on this platform and configured.
        /// </summary>
        public static bool IsMagicAvailable
        {
            get
            {
    #if UNITY_WEBGL && !UNITY_EDITOR
                var cfg = Instance?.blockmakerConfig;
                return cfg != null && cfg.enableMagicEmail && !string.IsNullOrEmpty(cfg.magicPublishableKey);
    #else
                return false;
    #endif
            }
        }

        /// <summary>
        /// Wallet sign timeout sourced from config, with a safe fallback.
        /// Used by identity classes AND game-side sign watchdogs (which set their outer
        /// deadline to this + a margin) instead of hardcoded constants.
        /// </summary>
        public static float WalletSignTimeout =>
            Instance?.blockmakerConfig?.walletSignTimeoutSeconds ?? 120f;

        // ── Public identity accessors ──────────────────────────────────────────────
        public bool                 IsAuthenticating { get; private set; }
        public IBlockmakerIdentity Identity     { get; private set; }
        public string               Address     => Identity?.Address     ?? string.Empty;
        public string               DisplayName => Identity?.DisplayName ?? "Guest";
        public bool                 HasWallet   => Identity?.HasWallet   ?? false;
        public bool                 CanSign     => Identity?.CanSign     ?? false;
        public IdentityTier         Tier        => Identity?.Tier        ?? IdentityTier.Guest;

        /// <summary>True when the player has an active session (Email or SelfCustody tier).</summary>
        public bool IsLoggedIn => Identity != null && Identity.Tier >= IdentityTier.Email;

        /// <summary>
        /// Returns the player's email address if signed in via email or Magic, null otherwise.
        /// </summary>
        public string GetUserEmail()
        {
            if (Identity is EmailIdentity e) return e.Email;
            if (Identity is MagicIdentity m) return m.Email;
            return null;
        }

        /// <summary>
        /// Check whether the current session token is still valid.
        /// Useful before expensive operations or after app resume.
        /// Returns false for Guest identities.
        /// </summary>
        public void VerifySession(Action<bool> onResult)
        {
            if (Identity == null || Identity.Tier == IdentityTier.Guest)
            {
                onResult?.Invoke(false);
                return;
            }

            string token = null;
            if (Identity is EmailIdentity e) token = e.SessionToken;
            else if (Identity is MagicIdentity m) token = m.SessionToken;

            if (string.IsNullOrEmpty(token))
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // Browser wallets do not use the native Reown/WCv1 connection state when
                // signing in WebGL. EVM/xChain signs through the injected EIP-1193 provider
                // and Algorand wallets through the WebGL bridge; both transports validate
                // their live provider again when the actual signature is requested. A fresh
                // browser login has therefore already proved the provider is available, while
                // checking Reown below would always reject it as "not connected".
                if (Identity is WalletConnectIdentity || Identity is EvmXChainIdentity)
                {
                    onResult?.Invoke(Identity.CanSign);
                    return;
                }
#endif
                if (Identity is WalletConnectIdentity wc)
                {
                    var wcv1 = wc.OwnWCv1Client;
                    bool wcv1Connected = wcv1 != null && !wcv1.IsDisposed && wcv1.IsConnected;
                    var connector = ReownWalletConnector.Instance;
                    bool reownConnected = connector != null && connector.IsConnected;
                    onResult?.Invoke(wcv1Connected || reownConnected);
                }
                else if (Identity is EvmXChainIdentity)
                {
                    var connector = ReownWalletConnector.Instance;
                    onResult?.Invoke(connector != null && connector.IsConnected);
                }
                else
                {
                    onResult?.Invoke(false);
                }
                return;
            }

            if (BlockmakerClient.Instance == null)
            {
                onResult?.Invoke(false);
                return;
            }

            BlockmakerClient.Instance.VerifySessionToken(token, onResult);
        }

        // ── Pending JS sign callbacks ──────────────────────────────────────────────
        internal string   PendingSignedTxn  { get; private set; }
        internal string[] PendingSignedTxns { get; private set; }
        internal string   PendingSignError  { get; private set; }
        private int _signGeneration;
        private int _pendingSignGeneration;

        internal string   ConsumePendingSignedTxn()  { var v = PendingSignedTxn;  PendingSignedTxn  = null; _signAwaiting = false; return v; }
        internal string[] ConsumePendingSignedTxns() { var v = PendingSignedTxns; PendingSignedTxns = null; _signAwaiting = false; return v; }
        internal string   ConsumePendingSignError()  { var v = PendingSignError;  PendingSignError  = null; _signAwaiting = false; return v; }

        /// <summary>
        /// Clear pending sign state and return a generation token.
        /// If the generation changes before your result arrives, another sign
        /// request has started and yours should abort.
        /// </summary>
        internal int BeginPendingSign()
        {
            PendingSignedTxn  = null;
            PendingSignedTxns = null;
            PendingSignError  = null;
            _pendingSignGeneration = ++_signGeneration;
            _signAwaiting = true;
            return _signGeneration;
        }

        internal bool IsSignGenerationCurrent(int gen) => _signGeneration == gen;

        // True from BeginPendingSign() until the result is consumed (ConsumePending*). Covers
        // both "sign awaiting in JS" and "result landed but not yet read", so deferring on it
        // can't clobber an unconsumed result. Used to defer an auto wallet-login sign so it
        // doesn't bump the sign generation out from under a user-initiated sign (which would
        // otherwise make the loser die "interrupted").
        internal bool IsWebGLSignInFlight => _signAwaiting;

        private bool _signAwaiting;

        // ── Inspector ──────────────────────────────────────────────────────────────
        [Header("Blockmaker")]
        [Tooltip("Drag your BlockmakerConfig asset here. Required if no BlockmakerClient exists in the scene.")]
        public BlockmakerConfig blockmakerConfig;

        [Header("WalletConnect (legacy — use BlockmakerConfig instead)")]
        [Tooltip("Deprecated: set WalletConnect Project ID on BlockmakerConfig instead. This field is used as a fallback if the config field is empty.")]
        [System.Obsolete("Use BlockmakerConfig.walletConnectProjectId instead")]
        [HideInInspector]
        public string walletConnectProjectId = "";

        private string ResolvedWalletConnectProjectId
        {
            get
            {
                if (!string.IsNullOrEmpty(blockmakerConfig?.walletConnectProjectId))
                    return blockmakerConfig.walletConnectProjectId;
                if (!string.IsNullOrEmpty(walletConnectProjectId))
                    return walletConnectProjectId;
                return BlockmakerConfig.DefaultWalletConnectProjectId;
            }
        }

        // ── Proactive token refresh ─────────────────────────────────────────────
        private Coroutine _tokenRefreshCoroutine;
        private bool _isRefreshing;
        private const float TOKEN_REFRESH_INTERVAL = 50f * 60f; // refresh ~10 min before 1h expiry

        // ── In-flight coroutine tracking ──────────────────────────────────────────
        private Coroutine _magicLoginCoroutine;
        private Coroutine _emailOtpCoroutine;
        private Coroutine _peraConnectCoroutine;
        private Coroutine _webglTimeoutCoroutine;
        private bool      _isWalletConnecting;

        // Guards against concurrent wallet-signature Login() coroutines. The existing
        // SessionToken check is a no-op during the async login window because the token
        // isn't set until the coroutine's last line, so connect+restore+reconnect events
        // could each start a Login() (duplicate signature prompts / torn SaveSession).
        private bool      _walletLoginInFlight;

        // Monotonically-increasing id of the CURRENT wallet-login attempt. Every attempt
        // captures the value at start; RetryWalletLogin / CancelWalletLogin / Logout bump it,
        // which invalidates the old attempt's callbacks (a late success/error whose captured
        // seq no longer matches is logged and ignored — it must not clear the new attempt's
        // in-flight flag or double-fire OnIdentityChanged).
        private int       _walletLoginSeq;
        // Live RunWalletLogin coroutine, so retry/cancel can hard-stop the old attempt
        // (kills the nested Login() wait too — StopCoroutine disposes the iterator chain,
        // running RunWalletLogin's finally, which is seq-guarded).
        private Coroutine _walletLoginCoroutine;

        // ── Native WC v1 client for Pera ──────────────────────────────────────────
        private Texture2D _peraQRTexture;
        private WalletConnectV1Client _wcv1Client;
        internal WalletConnectV1Client WCv1Client => _wcv1Client;
        private volatile string _wcv1Address;
        private volatile string _wcv1Error;
        private volatile bool   _wcv1Done;

        private ReownWalletConnector _connector;

        // ── Pending connection callbacks ──────────────────────────────────────────
        private Action<IBlockmakerIdentity> _pendingConnectSuccess;
        private Action<string>              _pendingConnectError;
        private Action<IBlockmakerIdentity> _pendingMagicSuccess;
        private Action<string>              _pendingMagicError;
        private Action<IBlockmakerIdentity> _pendingEvmSuccess;
        private Action<string>              _pendingEvmError;
        private IBlockmakerIdentity         _evmRestoreCapturedIdentity;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BlockmakerPrefs.InvalidatePrefix();
            EnsureBlockmakerClient();
        }

        private void EnsureBlockmakerClient()
        {
            if (BlockmakerClient.Instance != null) return;

            var existing = GetComponent<BlockmakerClient>();
            if (existing != null)
            {
                if (existing.config == null && blockmakerConfig != null)
                {
                    existing.config = blockmakerConfig;
                    existing.InitFromAuth();
                }
                return;
            }

            BlockmakerConfig cfg = blockmakerConfig;
            if (cfg == null)
            {
                var configs = Resources.FindObjectsOfTypeAll<BlockmakerConfig>();
                if (configs.Length > 0) cfg = configs[0];
            }

            if (cfg == null)
            {
                BlockmakerLog.Warning("[BlockmakerAuth] No BlockmakerConfig found. Create one via Assets > Create > Blockmaker > Config.");
                return;
            }

            var client = gameObject.AddComponent<BlockmakerClient>();
            client.config = cfg;
            client.InitFromAuth();

            if (BlockmakerProfileManager.Instance == null && GetComponent<BlockmakerProfileManager>() == null)
                gameObject.AddComponent<BlockmakerProfileManager>();
        }

        private void OnDestroy()
        {
            if (_connector != null) _connector.OnInitialized -= OnReownInitialized;
            CleanupWCv1();
            StopTokenRefreshTimer();
            CancelWebGLTimeout();

            if (Instance == this)
                Instance = null;
        }

        private float _lastRefreshTime;

        private void OnApplicationPause(bool paused)
        {
            if (paused) return;
            float elapsed = Time.realtimeSinceStartup - _lastRefreshTime;
            if (elapsed < 300f) return;
            TryImmediateRefresh();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            float elapsed = Time.realtimeSinceStartup - _lastRefreshTime;
            if (elapsed < 300f) return;
            TryImmediateRefresh();
        }

        private void TryImmediateRefresh()
        {
            string refreshToken = null;
            if (Identity is ServerSignedIdentity e) refreshToken = e.RefreshToken;
            else if (Identity is MagicIdentity m) refreshToken = m.RefreshToken;
            else if (Identity is WalletConnectIdentity wc) refreshToken = wc.RefreshToken;
            else if (Identity is EvmXChainIdentity evm) refreshToken = evm.RefreshToken;

            if (string.IsNullOrEmpty(refreshToken) || BlockmakerClient.Instance == null || _isRefreshing) return;

            _isRefreshing = true;
            _lastRefreshTime = Time.realtimeSinceStartup;
            var capturedIdentity = Identity;
            BlockmakerClient.Instance.RefreshToken(refreshToken, result =>
            {
                _isRefreshing = false;
                if (this == null || Identity != capturedIdentity) return;
                if (result == null || string.IsNullOrEmpty(result.sessionToken)) return;

                if (capturedIdentity is ServerSignedIdentity ce) ce.UpdateTokens(result.sessionToken, result.refreshToken);
                else if (capturedIdentity is MagicIdentity cm) cm.UpdateTokens(result.sessionToken, result.refreshToken);
                else if (capturedIdentity is WalletConnectIdentity cwc) cwc.UpdateTokens(result.sessionToken, result.refreshToken);
                else if (capturedIdentity is EvmXChainIdentity cevm) cevm.UpdateTokens(result.sessionToken, result.refreshToken);
                capturedIdentity.SaveSession();
                BlockmakerLog.Info("[BlockmakerAuth] Token refreshed after app resume.");
            }, err =>
            {
                _isRefreshing = false;
                BlockmakerLog.Warning($"[BlockmakerAuth] Resume token refresh failed: {err}");
                SafeInvoke(OnAuthError, "Your session may have expired. Please sign in again if you experience issues.");
            });
        }

        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Warm the official Lute adapter before the player clicks it. Lute's
            // approval window must be opened synchronously from that click, so the
            // click path never waits on a CDN import. NFTURBO normally satisfies
            // this from its vendored adapter; this is the generic SDK fallback.
            BlockmakerWalletBridge.LuteJsPrepare();
#endif
            if (!TryRestoreSession())
            {
                SetIdentity(new GuestIdentity());
                SettleSessionRestore();   // no stored session — the auth state is KNOWN immediately
            }
            else
            {
                VerifyRestoredSession();
            }

            _connector = GetComponent<ReownWalletConnector>();
            if (_connector == null) _connector = gameObject.AddComponent<ReownWalletConnector>();
            var wcProjectId = ResolvedWalletConnectProjectId;
            if (!string.IsNullOrEmpty(wcProjectId))
            {
                BlockmakerLog.Info($"[BlockmakerAuth] Initializing Reown with project ID: {wcProjectId[..8]}...");
                _connector.OnInitialized += OnReownInitialized;
                _connector.Initialize(wcProjectId);
            }
            else
            {
                BlockmakerLog.Warning("[BlockmakerAuth] No WalletConnect Project ID — Defly and X-Chain will not be available.");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Restore browser-held wallet sessions (Pera JS etc.) NOW, independent of
            // Reown init — a Reown failure/hang must not strand a returning Pera player's
            // signing (their JWT restores but the Pera JS session never re-attached).
            TryReconnectBrowserWalletSessions();
#endif
        }

        private void OnReownInitialized()
        {
            if (_connector != null) _connector.OnInitialized -= OnReownInitialized;

            TryReconnectWalletSessions();
        }

        // ── Session restore ────────────────────────────────────────────────────────

        private bool TryRestoreSession()
        {
            var evmData = EvmXChainIdentity.TryLoadSessionData();
            if (evmData != null)
            {
                var evmIdentity = new EvmXChainIdentity(evmData.algorandAddress, evmData.evmAddress);
                evmIdentity.UpdateTokens(evmData.sessionToken, evmData.refreshToken);
                SetIdentity(evmIdentity);
                BlockmakerLog.Info($"[BlockmakerAuth] EVM xChain session restored: {evmData.evmAddress}");
                // Do NOT TriggerWalletLogin inline here — the relay/connection isn't live yet.
                // VerifyRestoredSession() refreshes/verifies any existing token; a fresh sign-in
                // login (no-token case) is triggered later from TryReconnectWalletSessions once
                // the connection is ready.
                return true;
            }

            foreach (var provider in new[] { ProviderPera, ProviderDefly, ProviderLute })
            {
                var data = WalletConnectIdentity.TryLoadSessionData(provider);
                if (data != null)
                {
                    var restoredIdentity = CreateWalletIdentity(provider, data.address);
                    if (restoredIdentity is WalletConnectIdentity restoredWc)
                        restoredWc.UpdateTokens(data.sessionToken, data.refreshToken);
                    SetIdentity(restoredIdentity);
                    BlockmakerLog.Info($"[BlockmakerAuth] {provider} session restored: {data.address}");

                    var wcv1Session = WalletConnectIdentity.TryLoadWCv1Session(provider);
                    if (wcv1Session != null)
                    {
                        CleanupWCv1();
                        _wcv1Client = WalletConnectV1Client.FromSession(wcv1Session);
                        if (restoredIdentity is WalletConnectIdentity wcIdentity)
                            wcIdentity.OwnWCv1Client = _wcv1Client;
                        _wcv1Client.Reconnect().ContinueWith(t =>
                        {
                            if (t.IsFaulted) BlockmakerLog.Warning($"[BlockmakerAuth] WCv1 reconnect failed: {t.Exception?.InnerException?.Message}");
                        }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                        BlockmakerLog.Info($"[BlockmakerAuth] WCv1 client restored and reconnecting for {provider}");
                    }

                    // Do NOT TriggerWalletLogin inline here — the WCv1 relay is still reconnecting
                    // and Reown may not be initialized. VerifyRestoredSession() handles an existing
                    // token; a fresh sign-in login (no-token case) is triggered later from
                    // TryReconnectWalletSessions once the connection is confirmed ready.
                    return true;
                }
            }

            var magicIdentity = MagicIdentity.TryLoadSession();
            if (magicIdentity != null)
            {
                SetIdentity(magicIdentity);
                BlockmakerLog.Info("[BlockmakerAuth] Magic session restored.");
                return true;
            }

            var emailIdentity = EmailIdentity.TryLoadSession();
            if (emailIdentity != null)
            {
                SetIdentity(emailIdentity);
                BlockmakerLog.Info("[BlockmakerAuth] Email session restored.");
                return true;
            }

            return false;
        }

        private void VerifyRestoredSession()
        {
            string token = null;
            string refreshToken = null;
            bool   isWallet = false;

            if (Identity is EmailIdentity email)
            { token = email.SessionToken; refreshToken = email.RefreshToken; }
            else if (Identity is MagicIdentity magic)
            { token = magic.SessionToken; refreshToken = magic.RefreshToken; }
            else if (Identity is WalletConnectIdentity wc)
            { token = wc.SessionToken; refreshToken = wc.RefreshToken; isWallet = true; }
            else if (Identity is EvmXChainIdentity evm)
            { token = evm.SessionToken; refreshToken = evm.RefreshToken; isWallet = true; }

            if (string.IsNullOrEmpty(token) && string.IsNullOrEmpty(refreshToken))
            {
                // For wallet identities, "no tokens" is a normal restore state: the JWT is
                // acquired by a fresh wallet-signature Login once the connection is confirmed
                // ready (TryReconnectWalletSessions / OnWalletReconnectedFromJS). Leave the
                // identity in place rather than downgrading to Guest.
                if (isWallet)
                {
                    // Lute has no silent signing session: every approval is a fresh,
                    // user-opened browser/extension surface. A tokenless remembered
                    // address must return to the sign-in picker instead of trying to
                    // open a popup during boot, which browsers correctly block.
                    if (Identity is LuteIdentity lute)
                    {
                        BlockmakerLog.Info("[BlockmakerAuth] Remembered Lute identity has no valid session — fresh player sign-in required.");
                        lute.ClearSession();
                        SetIdentity(new GuestIdentity());
                        SettleSessionRestore();
                        return;
                    }
                    BlockmakerLog.Info("[BlockmakerAuth] Restored wallet session has no JWT yet — will sign in once the connection is ready.");
                    // Settled as "no valid token right now" — a later relay-gated wallet login
                    // that lands a JWT announces itself via OnIdentityChanged (allowed upgrade).
                    SettleSessionRestore();
                    return;
                }
                BlockmakerLog.Info("[BlockmakerAuth] Restored session has no tokens — clearing.");
                SetIdentity(new GuestIdentity());
                SettleSessionRestore();
                return;
            }

            var capturedIdentity = Identity;

            // If we have a refresh token, use it to get a fresh JWT immediately
            if (!string.IsNullOrEmpty(refreshToken) && BlockmakerClient.Instance != null)
            {
                BlockmakerClient.Instance.RefreshToken(refreshToken, result =>
                {
                    // Every path below settles the restore. On the success path the settle must
                    // come AFTER UpdateTokens/SaveSession: settle subscribers may immediately fire
                    // token-authed requests (profile fetch), which would 401 on the stale token.
                    if (this == null) { SettleSessionRestore(); return; }
                    if (Identity != capturedIdentity) { SettleSessionRestore(); return; }
                    if (result != null && !string.IsNullOrEmpty(result.sessionToken))
                    {
                        if (capturedIdentity is ServerSignedIdentity ss) ss.UpdateTokens(result.sessionToken, result.refreshToken);
                        else if (capturedIdentity is MagicIdentity m) m.UpdateTokens(result.sessionToken, result.refreshToken);
                        else if (capturedIdentity is WalletConnectIdentity cwc) cwc.UpdateTokens(result.sessionToken, result.refreshToken);
                        else if (capturedIdentity is EvmXChainIdentity cevm) cevm.UpdateTokens(result.sessionToken, result.refreshToken);
                        capturedIdentity.SaveSession();
                        BlockmakerLog.Info("[BlockmakerAuth] Session token refreshed on restore.");
                        // Settle FIRST (fresh token is stored now), so OnIdentityChanged
                        // subscribers already read SessionRestoreSettled == true.
                        SettleSessionRestore();
                        // The restore-time OnIdentityChanged fired BEFORE this fresh JWT existed,
                        // so session-gated consumers (balance tracker, profile manager) skipped
                        // their loads and are waiting for a re-fire that would otherwise never
                        // come on this path. Announce the now-valid session the same way a
                        // completed wallet-signature login does (see RunWalletLogin).
                        SafeInvoke(OnIdentityChanged, capturedIdentity);
                    }
                    else
                    {
                        // Refresh "succeeded" but returned no usable token — resolved either way.
                        SettleSessionRestore();
                    }
                }, err =>
                {
                    // Restore attempt resolved: the stored token could not be refreshed. Either a
                    // wallet re-sign (below) or a fresh user sign-in is required from here.
                    // Settled at the END of each path (after the identity is in its final state)
                    // so settle subscribers never act on the not-yet-downgraded identity.
                    if (this == null) { SettleSessionRestore(); return; }
                    if (Identity != capturedIdentity) { SettleSessionRestore(); return; }
                    // For wallet identities (WalletConnect/EVM xChain) a refresh failure does NOT
                    // mean the player must drop to Guest: the live wallet relay can re-sign for a
                    // fresh JWT. Clear only the stale tokens and leave the identity in place so the
                    // gated TriggerWalletLogin path (TryReconnectWalletSessions / reconnect / restore
                    // callbacks) re-acquires a JWT once the connection is ready.
                    // Email/Magic keep the original behavior: a failed refresh means re-login.
                    if (capturedIdentity is LuteIdentity lute)
                    {
                        BlockmakerLog.Info($"[BlockmakerAuth] Lute session expired — fresh player sign-in required: {err}");
                        lute.ClearSession();
                        SetIdentity(new GuestIdentity());
                        SafeInvoke(OnAuthError, "Your Lute sign-in expired. Connect Lute again to continue.");
                    }
                    else if (capturedIdentity is WalletConnectIdentity cwc)
                    {
                        BlockmakerLog.Info($"[BlockmakerAuth] Wallet token refresh failed — keeping identity, will re-sign when relay is ready: {err}");
                        cwc.ClearTokens();
                        cwc.SaveSession();
                        // Re-sign immediately now that the stale token is cleared, rather than
                        // waiting on a later connection event (an adverse async ordering could
                        // otherwise leave the wallet connected-but-tokenless until reconnect).
                        // The F1 in-flight guard + the empty-token check inside TriggerWalletLogin
                        // prevent a duplicate prompt; tokens are now empty so it won't early-return.
                        TriggerWalletLogin(cwc);
                    }
                    else if (capturedIdentity is EvmXChainIdentity cevm)
                    {
                        BlockmakerLog.Info($"[BlockmakerAuth] Wallet token refresh failed — keeping identity, will re-sign when relay is ready: {err}");
                        cevm.ClearTokens();
                        cevm.SaveSession();
                        // Re-sign immediately now that the stale token is cleared (see WC branch).
                        TriggerWalletLogin(cevm);
                    }
                    else
                    {
                        BlockmakerLog.Info($"[BlockmakerAuth] Refresh failed — clearing session: {err}");
                        SetIdentity(new GuestIdentity());
                    }
                    SettleSessionRestore();
                });
                return;
            }

            // No refresh token — just verify the JWT
            if (BlockmakerClient.Instance == null)
            {
                BlockmakerLog.Warning("[BlockmakerAuth] Cannot verify session — no server connection. Clearing session.");
                SetIdentity(new GuestIdentity());
                SettleSessionRestore();
                return;
            }
            BlockmakerClient.Instance.VerifySessionToken(token, ok =>
            {
                // Verified either way — the restore attempt is resolved. Settle AFTER the identity
                // reaches its final state (the !ok downgrade), so settle subscribers never read a
                // signed-in identity that is about to drop to Guest.
                if (this == null) { SettleSessionRestore(); return; }
                if (Identity != capturedIdentity) { SettleSessionRestore(); return; }
                if (!ok)
                {
                    BlockmakerLog.Info($"[BlockmakerAuth] {capturedIdentity.ProviderName} session expired — please sign in again.");
                    SetIdentity(new GuestIdentity());
                }
                SettleSessionRestore();
            });
        }

        // ── Proactive token refresh ──────────────────────────────────────────────

        private void StartTokenRefreshTimer()
        {
            StopTokenRefreshTimer();
            _tokenRefreshCoroutine = StartCoroutine(TokenRefreshLoop());
        }

        private void StopTokenRefreshTimer()
        {
            if (_tokenRefreshCoroutine != null)
            {
                StopCoroutine(_tokenRefreshCoroutine);
                _tokenRefreshCoroutine = null;
            }
        }

        private IEnumerator TokenRefreshLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(TOKEN_REFRESH_INTERVAL);

                string refreshToken = null;
                if (Identity is ServerSignedIdentity e) refreshToken = e.RefreshToken;
                else if (Identity is MagicIdentity m) refreshToken = m.RefreshToken;
                else if (Identity is WalletConnectIdentity wc) refreshToken = wc.RefreshToken;
                else if (Identity is EvmXChainIdentity evm) refreshToken = evm.RefreshToken;

                if (_isRefreshing && BlockmakerClient.Instance == null)
                    _isRefreshing = false;

                if (string.IsNullOrEmpty(refreshToken) || BlockmakerClient.Instance == null || _isRefreshing)
                    continue;

                _isRefreshing = true;
                _lastRefreshTime = Time.realtimeSinceStartup;
                var capturedIdentity = Identity;
                BlockmakerClient.Instance.RefreshToken(refreshToken, result =>
                {
                    _isRefreshing = false;
                    if (this == null || Identity != capturedIdentity) return;
                    if (result == null || string.IsNullOrEmpty(result.sessionToken)) return;

                    if (capturedIdentity is ServerSignedIdentity ce) ce.UpdateTokens(result.sessionToken, result.refreshToken);
                    else if (capturedIdentity is MagicIdentity cm) cm.UpdateTokens(result.sessionToken, result.refreshToken);
                    else if (capturedIdentity is WalletConnectIdentity cwc) cwc.UpdateTokens(result.sessionToken, result.refreshToken);
                    else if (capturedIdentity is EvmXChainIdentity cevm) cevm.UpdateTokens(result.sessionToken, result.refreshToken);
                    capturedIdentity.SaveSession();
                    BlockmakerLog.Info("[BlockmakerAuth] Token proactively refreshed.");
                }, err =>
                {
                    _isRefreshing = false;
                    BlockmakerLog.Warning($"[BlockmakerAuth] Proactive token refresh failed: {err}");
                    SafeInvoke(OnAuthError, "Your session could not be refreshed. You may need to sign in again.");
                });
            }
        }

        private void TryReconnectWalletSessions()
        {
            if (_connector != null && _connector.IsInitialized)
            {
                var address = _connector.TryRestoreSession();
                if (address != null)
                {
                    BlockmakerLog.Info($"[BlockmakerAuth] Native WC v2 session restored: {address}");
                    // Connection is now ready. If the restored wallet identity has no JWT yet,
                    // run a fresh wallet-signature login now (gated behind connection-ready, not
                    // fired inline during TryRestoreSession). VerifyRestoredSession already
                    // refreshed/verified any existing token, so this no-ops when one is present.
                    TriggerWalletLogin(Identity);
                    return;
                }
            }

    #if UNITY_WEBGL && !UNITY_EDITOR
            // Browser-held sessions (Pera JS lib / Magic / EVM) restore independently of
            // Reown — see TryReconnectBrowserWalletSessions (idempotent; normally already
            // fired from Start, this covers late callers).
            TryReconnectBrowserWalletSessions();
    #else
            // Native (non-WebGL): the WCv1 Pera path doesn't go through _connector.TryRestoreSession
            // above. If a wallet identity restored without a JWT, run a fresh wallet-signature login
            // now that we're past Reown init. Login() itself waits for the WCv1 relay to reconnect,
            // and RunWalletLogin defers while any sign is in flight. No-ops if a token already exists.
            if (Identity is WalletConnectIdentity || Identity is EvmXChainIdentity)
                TriggerWalletLogin(Identity);
    #endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Guard so the browser reconnect runs exactly once whether it fires from Start
        // (Reown-independent) or from OnReownInitialized (the legacy trigger).
        private bool _browserWalletReconnectAttempted;

        /// <summary>
        /// Restore wallet sessions that live in the BROWSER, not in Reown: Pera's JS lib
        /// (localStorage), Magic, and the EVM bridge. Historically this only ran from
        /// OnReownInitialized — if Reown's init threw or hung (its failure path swallows
        /// and never fires OnInitialized), a returning Pera player's JWT restored fine but
        /// the Pera JS session was never re-attached, so their FIRST signature failed with
        /// "Pera wallet not connected" until a manual reconnect. Pera needs nothing from
        /// Reown, so this is also called directly from Start.
        /// </summary>
        private void TryReconnectBrowserWalletSessions()
        {
            if (_browserWalletReconnectAttempted) return;
            _browserWalletReconnectAttempted = true;

            foreach (var provider in new[] { "Pera", "Defly" })
            {
                BlockmakerWalletBridge.TryReconnect(
                    provider,
                    gameObject.name,
                    nameof(OnWalletReconnectedFromJS),
                    nameof(OnWalletReconnectFailed)
                );
            }

            // Pera on WebGL sessions live in Pera's own JS library (localStorage). The jslib
            // emits "Pera:<address>" — the same payload the generic reconnect receivers expect.
            if (Identity is WalletConnectIdentity peraId && peraId.ProviderName == ProviderPera)
            {
                BlockmakerWalletBridge.PeraJsReconnect(
                    gameObject.name,
                    nameof(OnWalletReconnectedFromJS),
                    nameof(OnWalletReconnectFailed)
                );
            }

            var cfg = BlockmakerClient.Instance?.config;
            if (Identity is MagicIdentity && cfg != null && cfg.enableMagicEmail && !string.IsNullOrEmpty(cfg.magicPublishableKey))
            {
                BlockmakerWalletBridge.MagicTryRestore(
                    cfg.magicPublishableKey,
                    gameObject.name,
                    nameof(OnMagicRestoreSuccess),
                    nameof(OnMagicRestoreError)
                );
            }

            if (Identity is EvmXChainIdentity evm)
            {
                _evmRestoreCapturedIdentity = Identity;
                BlockmakerWalletBridge.EvmTryRestore(
                    evm.EvmAddress,
                    gameObject.name,
                    nameof(OnEvmRestoreSuccess),
                    nameof(OnEvmRestoreError)
                );
            }
        }
#endif

        // ── Connect wallet (QR flow) ───────────────────────────────────────────────

        /// <summary>
        /// Begin a wallet connection.
        ///
        /// Pera: uses native WalletConnect v1 on all platforms.
        /// Defly: uses Reown SDK (WalletConnect v2); falls back to JS bridge on WebGL.
        /// Lute: uses the official browser/extension adapter in WebGL.
        ///
        /// OnWalletQRReady fires with the QR code for display.
        /// onSuccess / OnIdentityChanged fire when the user approves.
        /// </summary>
        public void ConnectWallet(
            string provider,
            Action<IBlockmakerIdentity> onSuccess = null,
            Action<string>              onError   = null)
        {
            if (_isWalletConnecting || IsAuthenticating)
            {
                onError?.Invoke(_isWalletConnecting
                    ? "A wallet connection is already in progress. Please wait."
                    : "Another sign-in is already in progress. Please wait.");
                return;
            }

            if (!provider.Equals(ProviderPera, StringComparison.OrdinalIgnoreCase) &&
                !provider.Equals(ProviderDefly, StringComparison.OrdinalIgnoreCase) &&
                !provider.Equals(ProviderLute, StringComparison.OrdinalIgnoreCase))
            {
                BlockmakerLog.Error($"[BlockmakerAuth] Unknown wallet provider '{provider}'. Supported: \"{ProviderPera}\", \"{ProviderDefly}\", \"{ProviderLute}\".");
                onError?.Invoke($"Unknown wallet provider \"{provider}\". Please use Pera, Defly or Lute.");
                return;
            }

            IsAuthenticating       = true;
            _isWalletConnecting    = true;
            _pendingConnectSuccess = onSuccess;
            _pendingConnectError   = onError;

            if (provider.Equals(ProviderLute, StringComparison.OrdinalIgnoreCase))
            {
    #if UNITY_WEBGL && !UNITY_EDITOR
                BlockmakerWalletBridge.LuteJsConnect(
                    gameObject.name,
                    nameof(OnWalletConnectedFromJS),
                    nameof(OnWalletErrorFromJS));
                StartWebGLTimeout(WalletSignTimeout, () =>
                {
                    if (_isWalletConnecting) FailWalletConnection("Lute did not respond in time. Please try again.");
                });
    #else
                _isWalletConnecting = false;
                IsAuthenticating = false;
                _pendingConnectSuccess = null;
                _pendingConnectError = null;
                onError?.Invoke("Lute is available in the WebGL version of this game.");
    #endif
                return;
            }

            if (provider.Equals(ProviderPera, StringComparison.OrdinalIgnoreCase))
            {
    #if UNITY_WEBGL && !UNITY_EDITOR
                // Pera speaks WalletConnect v1 ONLY (Pera-founder-confirmed) — never route
                // it through the WC v2 paths (Reown / the in-house jslib client): the app
                // cannot pair their QR codes. On WebGL we use Pera's official JS library
                // HEADLESS: its DOM modal is suppressed (browser fullscreen would hide it —
                // sign-in must never leave fullscreen) and the v1 URI is sent to Unity for
                // the usual in-canvas QR.
                BlockmakerWalletBridge.PeraJsConnect(
                    gameObject.name,
                    nameof(OnPeraJsConnected),
                    nameof(OnPeraJsError),
                    nameof(OnWalletQRFromJS)   // v1 URI → the usual in-canvas QR pipeline
                );
                StartWebGLTimeout(WalletSignTimeout, () =>
                {
                    if (_isWalletConnecting) FailWalletConnection("Connection timed out. Please try again.");
                });
    #else
                _peraConnectCoroutine = StartCoroutine(PeraNativeWCv1Flow());
    #endif
                return;
            }

            if (_connector != null && _connector.IsInitialized)
            {
                _connector.ConnectAlgorand(
                    provider,
                    onConnected: (prov, address) => CompleteWalletConnection(prov, address),
                    onError:     err             => FailWalletConnection(err)
                );
                return;
            }

    #if UNITY_WEBGL && !UNITY_EDITOR
            if (string.IsNullOrEmpty(ResolvedWalletConnectProjectId))
            {
                BlockmakerLog.Error("[BlockmakerAuth] WalletConnect Project ID is not set. Set it on BlockmakerConfig (https://cloud.walletconnect.com).");
                _isWalletConnecting = false;
                IsAuthenticating    = false;
                onError?.Invoke("Wallet connection is not available right now. Please try again later.");
                return;
            }

            BlockmakerWalletBridge.ConnectWalletQR(
                ResolvedWalletConnectProjectId,
                provider,
                gameObject.name,
                nameof(OnWalletQRFromJS),
                nameof(OnWalletConnectedFromJS),
                nameof(OnWalletErrorFromJS)
            );
            StartWebGLTimeout(WalletSignTimeout, () =>
            {
                if (_isWalletConnecting) FailWalletConnection("Connection timed out. Please try again.");
            });
    #else
            _isWalletConnecting = false;
            IsAuthenticating    = false;
            onError?.Invoke("Wallet connection is not available. Please restart the game and try again.");
    #endif
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnWalletQRFromJS(string payload)
        {
            int firstPipe = payload.IndexOf('|');
            if (firstPipe < 0) return;
            var provider  = payload.Substring(0, firstPipe);
            var rest      = payload.Substring(firstPipe + 1);

            int secondPipe = rest.IndexOf('|');
            if (secondPipe < 0) return;
            var wcUri  = rest.Substring(0, secondPipe);
            var qrB64  = rest.Substring(secondPipe + 1);

            BlockmakerLog.Info($"[BlockmakerAuth] QR ready for {provider}");
            SafeInvoke(OnWalletQRReady, new WalletQREventArgs(provider, wcUri, qrB64));
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnWalletConnectedFromJS(string payload)
        {
            CancelWebGLTimeout();
            if (!_isWalletConnecting)
            {
                BlockmakerLog.Warning("[BlockmakerAuth] Ignoring unexpected wallet connection callback.");
                return;
            }

            var parts    = payload.Split(new[] { ':' }, 2);
            var provider = parts.Length > 1 ? parts[0] : "Unknown";
            var address  = parts.Length > 1 ? parts[1] : payload;

            CompleteWalletConnection(provider, address);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnWalletReconnectedFromJS(string payload)
        {
            var parts    = payload.Split(new[] { ':' }, 2);
            var provider = parts.Length > 1 ? parts[0] : "Unknown";
            var address  = parts.Length > 1 ? parts[1] : payload;

            if (Identity is WalletConnectIdentity existing && existing.Address == address)
            {
                BlockmakerLog.Info($"[BlockmakerAuth] WebGL wallet reconnected: {provider} {address}");
                // The relay is now confirmed live. If this restored identity still has no JWT,
                // this is the correct gated moment to run a fresh wallet-signature login.
                // TriggerWalletLogin no-ops when a token already exists.
                if (string.IsNullOrEmpty(existing.SessionToken))
                    TriggerWalletLogin(existing);
                return;
            }

            if (Identity != null && !(Identity is WalletConnectIdentity) && !(Identity is GuestIdentity))
            {
                BlockmakerLog.Info($"[BlockmakerAuth] Ignoring wallet reconnect — current identity is {Identity.ProviderName}");
                return;
            }

            var identity = CreateWalletIdentity(provider, address);
            SetIdentity(identity);
            identity.SaveSession();
            TriggerWalletLogin(identity);
            BlockmakerLog.Info($"[BlockmakerAuth] WebGL wallet session restored via reconnect: {provider} {address}");
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnWalletReconnectFailed(string error)
        {
            BlockmakerLog.Warning($"[BlockmakerAuth] WebGL wallet reconnect failed: {error}");
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnWalletErrorFromJS(string error)
        {
            CancelWebGLTimeout();
            FailWalletConnection(error);
        }

        // ── Pera official JS SDK callbacks (WebGL Pera path) ─────────────────────

        /// <summary>
        /// Success callback for BlockmakerWalletBridge.PeraJsConnect (WebGL only).
        /// Receives the bare Algorand address — Pera's own modal handled the
        /// QR / deep-link UX, so this feeds straight into the same post-connect
        /// funnel as the other wallets (identity → SaveSession → TriggerWalletLogin).
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnPeraJsConnected(string address)
        {
            CancelWebGLTimeout();
            if (!_isWalletConnecting)
            {
                BlockmakerLog.Warning("[BlockmakerAuth] Ignoring unexpected Pera JS connection callback.");
                return;
            }

            CompleteWalletConnection(ProviderPera, address);
        }

        /// <summary>Error callback for BlockmakerWalletBridge.PeraJsConnect (WebGL only).</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnPeraJsError(string error)
        {
            CancelWebGLTimeout();
            if (!_isWalletConnecting)
            {
                BlockmakerLog.Warning($"[BlockmakerAuth] Ignoring Pera JS error after connect ended: {error}");
                return;
            }

            // Recognizable code sent by the jslib when the user simply closed Pera's modal.
            if (error == "PERA_CONNECT_CANCELLED")
                error = "The connection was cancelled. Please try again.";

            FailWalletConnection(error);
        }

        // ── Pera native WalletConnect v1 ──────────────────────────────────────────

        private IEnumerator PeraNativeWCv1Flow()
        {
            CleanupWCv1();
            var dAppUrl = blockmakerConfig?.dAppUrl;
            if (string.IsNullOrEmpty(dAppUrl)) dAppUrl = blockmakerConfig?.serverUrl;
            if (string.IsNullOrEmpty(dAppUrl)) dAppUrl = BlockmakerConfig.DefaultServerUrl;

            _wcv1Client = new WalletConnectV1Client(
                chainId: 4160,
                appName: Application.productName,
                appDescription: Application.productName,
                appUrl: dAppUrl
            );

            string wcUri    = _wcv1Client.Uri;
            _wcv1Address = null;
            _wcv1Error   = null;
            _wcv1Done    = false;

            _wcv1Client.OnSessionApproved += addr => { _wcv1Address = addr; _wcv1Done = true; };
            _wcv1Client.OnSessionRejected += err  => { _wcv1Error = err;    _wcv1Done = true; };
            _wcv1Client.OnError           += err  => { _wcv1Error = err;    _wcv1Done = true; };

            // Show QR immediately — the URI is known before connecting
            var qrTexture = QRTextureGenerator.Generate(wcUri, 512);
            _peraQRTexture = qrTexture;
            ReownWalletConnector.FireQRReadyForBridge("Pera", wcUri, qrTexture);

            BlockmakerLog.Info($"[BlockmakerAuth] Pera WCv1 connecting to bridge...");

            var connectTask = _wcv1Client.Connect();
            while (!connectTask.IsCompleted)
                yield return null;

            if (connectTask.IsFaulted)
            {
                _peraConnectCoroutine = null;
                if (qrTexture != null) { Destroy(qrTexture); _peraQRTexture = null; }
                string msg = connectTask.Exception?.InnerException?.Message ?? "Could not connect to the Pera wallet service. Please check your internet connection and try again.";
                FailWalletConnection(msg);
                CleanupWCv1();
                yield break;
            }

            float elapsed = 0f;
            float TIMEOUT = WalletSignTimeout;

            float lastCheck = Time.realtimeSinceStartup;
            while (!_wcv1Done && elapsed < TIMEOUT && _isWalletConnecting)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                float now = Time.realtimeSinceStartup;
                elapsed += now - lastCheck;
                lastCheck = now;
            }

            _peraConnectCoroutine = null;
            if (qrTexture != null) { Destroy(qrTexture); _peraQRTexture = null; }

            if (!string.IsNullOrEmpty(_wcv1Address))
            {
                BlockmakerLog.Info($"[BlockmakerAuth] Pera WCv1 connected: {_wcv1Address}");

                if (_wcv1Client != null)
                {
                    var sd = _wcv1Client.GetSessionData();
                    if (sd != null)
                    {
                        SecurePrefs.SetString(BlockmakerPrefs.Key("wc_session_pera_wcv1"), JsonUtility.ToJson(sd));
                        SecurePrefs.Save();
                    }
                }

                CompleteWalletConnection("Pera", _wcv1Address);
            }
            else if (!string.IsNullOrEmpty(_wcv1Error))
            {
                FailWalletConnection(_wcv1Error);
                CleanupWCv1();
            }
            else
            {
                FailWalletConnection("Connection timed out. Please try again.");
                CleanupWCv1();
            }
        }

        private void CleanupWCv1()
        {
            if (_peraQRTexture != null) { Destroy(_peraQRTexture); _peraQRTexture = null; }
            if (_wcv1Client != null)
            {
                if (Identity is WalletConnectIdentity wcIdentity && wcIdentity.OwnWCv1Client == _wcv1Client)
                    wcIdentity.OwnWCv1Client = null;
                _wcv1Client.Dispose();
                _wcv1Client = null;
            }
        }

        private void CompleteWalletConnection(string provider, string address)
        {
            try
            {
                var prevTier = Tier;
                var identity = CreateWalletIdentity(provider, address);
                if (identity is WalletConnectIdentity wcIdentity && _wcv1Client != null)
                    wcIdentity.OwnWCv1Client = _wcv1Client;

                var successCb = _pendingConnectSuccess;
                _pendingConnectSuccess = null;
                _pendingConnectError   = null;
                _isWalletConnecting    = false;
                IsAuthenticating       = false;

                SetIdentity(identity);
                identity.SaveSession();
                if (identity is LuteIdentity)
                {
                    // Lute's sign-in proof needs a second browser approval window.
                    // Do not attempt to open it from this async connection callback;
                    // browsers would block it. The step-two UI asks the player to
                    // continue, and RetryWalletLogin primes the window from that click.
                    SafeInvoke(OnAuthStatus,
                        "Lute is connected. Continue in Lute to approve the free sign-in request.");
                }
                else
                {
                    TriggerWalletLogin(identity);
                }

                if (prevTier < IdentityTier.SelfCustody)
                    SafeInvoke(OnIdentityUpgraded, identity, prevTier);

                successCb?.Invoke(identity);
            }
            catch (Exception ex)
            {
                BlockmakerLog.Error($"[BlockmakerAuth] Wallet connection error: {ex.Message}");
                FailWalletConnection("Something went wrong while connecting. Please try again.");
            }
        }

        private void FailWalletConnection(string error)
        {
            BlockmakerLog.Error($"[BlockmakerAuth] Wallet error: {error}");
            SafeInvoke(OnAuthError, error);
            _pendingConnectError?.Invoke(error);
            _pendingConnectError   = null;
            _pendingConnectSuccess = null;
            _isWalletConnecting    = false;
            IsAuthenticating       = false;
        }

        public void CancelWalletConnect()
        {
            if (!_isWalletConnecting) return;
            CancelWebGLTimeout();
            if (_peraQRTexture != null) { Destroy(_peraQRTexture); _peraQRTexture = null; }
            if (_peraConnectCoroutine != null)
            {
                StopCoroutine(_peraConnectCoroutine);
                _peraConnectCoroutine = null;
            }
            CleanupWCv1();
            _connector?.CancelConnection();
            IsAuthenticating       = false;
            _pendingConnectSuccess = null;
            _pendingConnectError   = null;
            _isWalletConnecting    = false;
    #if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.CancelWalletQR();
    #endif
        }

        private void StartWebGLTimeout(float seconds, Action onTimeout)
        {
            CancelWebGLTimeout();
            _webglTimeoutCoroutine = StartCoroutine(WebGLTimeoutRoutine(seconds, onTimeout));
        }

        private void CancelWebGLTimeout()
        {
            if (_webglTimeoutCoroutine != null)
            {
                StopCoroutine(_webglTimeoutCoroutine);
                _webglTimeoutCoroutine = null;
            }
        }

        private IEnumerator WebGLTimeoutRoutine(float seconds, Action onTimeout)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _webglTimeoutCoroutine = null;
            onTimeout?.Invoke();
        }

        // ── Magic email login ────────────────────────────────────────────────────

        /// <summary>
        /// Start a Magic SDK email login. Magic handles its own OTP verification UI.
        /// On success the DID token is sent to our server for JWT issuance.
        /// </summary>
        public void ConnectMagicEmail(
            string email,
            Action<IBlockmakerIdentity> onSuccess = null,
            Action<string>              onError   = null)
        {
            if (IsAuthenticating || _pendingMagicSuccess != null || _pendingMagicError != null)
            {
                onError?.Invoke("Another sign-in is already in progress. Please wait.");
                return;
            }

            IsAuthenticating = true;

            var cfg = BlockmakerClient.Instance?.config;
            if (cfg == null || !cfg.enableMagicEmail || string.IsNullOrEmpty(cfg.magicPublishableKey))
            {
                IsAuthenticating = false;
                BlockmakerLog.Error("[BlockmakerAuth] Magic publishable key not configured. Set it on the BlockmakerConfig asset.");
                onError?.Invoke("Email sign-in is not available right now. Please try again later.");
                return;
            }

    #if UNITY_WEBGL && !UNITY_EDITOR
            _pendingMagicSuccess = onSuccess;
            _pendingMagicError   = onError;

            BlockmakerWalletBridge.MagicLoginWithEmail(
                cfg.magicPublishableKey,
                email,
                gameObject.name,
                nameof(OnMagicLoginSuccess),
                nameof(OnMagicLoginError)
            );
            StartWebGLTimeout(WalletSignTimeout, () =>
            {
                if (_pendingMagicSuccess != null || _pendingMagicError != null)
                    OnMagicLoginError("Sign-in timed out. Please try again.");
            });
    #else
            IsAuthenticating = false;
            onError?.Invoke("Email sign-in is only available when playing in a web browser.");
    #endif
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnMagicLoginSuccess(string payload)
        {
            CancelWebGLTimeout();
            if (_pendingMagicSuccess == null && _pendingMagicError == null)
                return;

            var parts = payload.Split(new[] { '|' }, 4);
            if (parts.Length < 4)
            {
                OnMagicLoginError("Sign-in could not be completed. Please try again.");
                return;
            }
            var address  = parts[1];
            var email    = parts[2];
            var didToken = parts[3];

            _magicLoginCoroutine = StartCoroutine(FinishMagicLogin(address, email, didToken));
        }

        private IEnumerator FinishMagicLogin(string address, string email, string didToken)
        {
            if (BlockmakerClient.Instance == null)
            {
                OnMagicLoginError("Unable to reach the server. Please check your connection and try again.");
                yield break;
            }

            string jwt        = null;
            string refreshTok = null;
            string serverAddress = null;
            string error      = null;

            try
            {
                yield return BlockmakerClient.Instance.VerifyMagicToken(
                    didToken, email,
                    result =>
                    {
                        jwt           = result.sessionToken;
                        refreshTok    = result.refreshToken;
                        serverAddress = result.walletAddress;
                    },
                    err    => { error = err; }
                );
            }
            finally
            {
                _magicLoginCoroutine = null;
            }

            if (error != null)
            {
                BlockmakerLog.Error("[BlockmakerAuth] Magic server verification failed.");
                string playerError = BlockmakerErrors.PlayerFacingMagicLoginError(error);
                SafeInvoke(OnAuthError, playerError);
                _pendingMagicError?.Invoke(playerError);
                _pendingMagicError   = null;
                _pendingMagicSuccess = null;
                IsAuthenticating     = false;
                yield break;
            }

            if (_pendingMagicSuccess == null && _pendingMagicError == null)
                yield break;

            if (!MagicWalletAddressMatches(serverAddress, address))
            {
                BlockmakerLog.Error("[BlockmakerAuth] Magic browser and server wallet identities did not match.");
#if UNITY_WEBGL && !UNITY_EDITOR
                BlockmakerWalletBridge.MagicLogout();
#endif
                OnMagicLoginError("MAGIC_IDENTITY_MISMATCH");
                yield break;
            }

            try
            {
                var prevTier = Tier;
                var identity = new MagicIdentity(email, address, jwt, refreshTok);
                SetIdentity(identity);
                identity.SaveSession();

                if (prevTier < IdentityTier.Email)
                    SafeInvoke(OnIdentityUpgraded, identity, prevTier);

                IsAuthenticating = false;
                _pendingMagicSuccess?.Invoke(identity);
                _pendingMagicSuccess = null;
                _pendingMagicError   = null;
            }
            catch (Exception ex)
            {
                BlockmakerLog.Error($"[BlockmakerAuth] Magic login finalization failed ({ex.GetType().Name}).");
                OnMagicLoginError("Something went wrong while signing in. Please try again.");
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnMagicLoginError(string error)
        {
            CancelWebGLTimeout();
            if (_pendingMagicSuccess == null && _pendingMagicError == null)
                return;

            string playerError = BlockmakerErrors.PlayerFacingMagicProviderError(error);
            BlockmakerLog.Error("[BlockmakerAuth] Magic login attempt failed.");
            SafeInvoke(OnAuthError, playerError);
            _pendingMagicError?.Invoke(playerError);
            _pendingMagicError   = null;
            _pendingMagicSuccess = null;
            IsAuthenticating     = false;
        }

        public void CancelPendingMagic()
        {
            if (_magicLoginCoroutine != null)
            {
                StopCoroutine(_magicLoginCoroutine);
                _magicLoginCoroutine = null;
            }
            _pendingMagicSuccess = null;
            _pendingMagicError   = null;
            IsAuthenticating     = false;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnMagicRestoreSuccess(string payload)
        {
            if (!(Identity is MagicIdentity magic)) return;
            var parts = payload?.Split(new[] { '|' }, 3);
            if (parts == null || parts.Length < 3 ||
                !MagicIdentityMatches(magic.Address, magic.Email, parts[1], parts[2]))
            {
                BlockmakerLog.Warning("[BlockmakerAuth] Restored Magic browser identity did not match the saved session; clearing it.");
                magic.ClearSession();
                SetIdentity(new GuestIdentity());
                SafeInvoke(OnAuthError, "Your email wallet session changed. Please sign in again.");
                return;
            }
            BlockmakerLog.Info("[BlockmakerAuth] Magic JS session confirmed active.");
        }

        private static bool MagicIdentityMatches(
            string expectedAddress,
            string expectedEmail,
            string actualAddress,
            string actualEmail)
        {
            return MagicWalletAddressMatches(expectedAddress, actualAddress)
                && !string.IsNullOrEmpty(expectedEmail)
                && !string.IsNullOrEmpty(actualEmail)
                && string.Equals(expectedEmail, actualEmail, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MagicWalletAddressMatches(string expectedAddress, string actualAddress)
        {
            return !string.IsNullOrEmpty(expectedAddress)
                && !string.IsNullOrEmpty(actualAddress)
                && string.Equals(expectedAddress, actualAddress, StringComparison.Ordinal);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnMagicRestoreError(string error)
        {
            if (Identity is MagicIdentity magic)
            {
                BlockmakerLog.Info("[BlockmakerAuth] Magic JS session expired — verifying server JWT…");
                if (string.IsNullOrEmpty(magic.SessionToken))
                {
                    Identity.ClearSession();
                    SetIdentity(new GuestIdentity());
                    return;
                }
                if (BlockmakerClient.Instance == null)
                {
                    BlockmakerLog.Warning("[BlockmakerAuth] Cannot verify Magic JWT — no server connection. Clearing session.");
                    Identity.ClearSession();
                    SetIdentity(new GuestIdentity());
                    return;
                }
                var capturedIdentity = Identity;
                BlockmakerClient.Instance.VerifySessionToken(magic.SessionToken, ok =>
                {
                    if (this == null) return;
                    if (Identity != capturedIdentity) return;
                    if (!ok)
                    {
                        BlockmakerLog.Info("[BlockmakerAuth] Server JWT also invalid — clearing session.");
                        Identity.ClearSession();
                        SetIdentity(new GuestIdentity());
                    }
                    else
                    {
                        BlockmakerLog.Info("[BlockmakerAuth] Server JWT still valid — keeping Magic identity.");
                    }
                });
            }
        }

        // ── EVM session restore callbacks ──────────────────────────────────────────

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmRestoreSuccess(string payload)
        {
            _evmRestoreCapturedIdentity = null;
            // payload is the raw EVM address ("0x…") — the jslib already verified it
            // matches the expected address passed to EvmTryRestore.
            if (string.IsNullOrEmpty(payload)) return;
            BlockmakerLog.Info($"[BlockmakerAuth] EVM wallet reconnected: {payload}");
            // Provider confirmed live. If the restored EVM identity still has no JWT, run a
            // fresh wallet-signature login now (the gated, connection-ready moment). No-ops if
            // a token already exists (VerifyRestoredSession refreshes/verifies any existing one).
            if (Identity is EvmXChainIdentity evm && string.IsNullOrEmpty(evm.SessionToken))
                TriggerWalletLogin(evm);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmRestoreError(string error)
        {
            if (Identity != _evmRestoreCapturedIdentity) { _evmRestoreCapturedIdentity = null; return; }
            _evmRestoreCapturedIdentity = null;
            if (Identity is EvmXChainIdentity)
            {
                BlockmakerLog.Warning($"[BlockmakerAuth] EVM restore failed: {error} — clearing session.");
                Identity.ClearSession();
                SetIdentity(new GuestIdentity());
            }
        }

        // ── EVM xChain ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Connect an EVM wallet via xChain Accounts. Derives an Algorand LogicSig address.
        /// This is the NO-PICKER path (auto-pick: last-used wallet → first announced →
        /// window.ethereum) — the wallet-picker UI uses <see cref="DiscoverEvmWallets"/> +
        /// <see cref="ConnectEvmWallet"/> instead. onError receives a bare message here
        /// (unchanged legacy shape).
        /// </summary>
        public void ConnectEvm(
            Action<IBlockmakerIdentity> onSuccess = null,
            Action<string>              onError   = null)
        {
            if (IsAuthenticating || _pendingEvmSuccess != null || _pendingEvmError != null)
            {
                onError?.Invoke("Another sign-in is already in progress. Please wait.");
                return;
            }

            IsAuthenticating = true;
            _pendingEvmSuccess = onSuccess;
            _pendingEvmError   = onError;
            _evmConnectErrorsIncludeCode = false;   // legacy path: bare-message errors
            _evmExpectedRdns = null;                // auto-pick: accept whichever wallet connects

    #if UNITY_WEBGL && !UNITY_EDITOR
            // Zero-bundle browser path: discover the installed EVM wallets first
            // (EIP-6963), then connect. OnEvmWalletsDiscovered picks the wallet and
            // calls EvmConnect; the timeout below covers the whole chain.
            // _evmConnectDispatched guards the discover→connect hop: a cancel +
            // immediate reconnect can leave a STALE discovery callback in flight,
            // and without the guard both callbacks would call EvmConnect → duplicate
            // eth_requestAccounts popups.
            _evmConnectDispatched = false;
            BlockmakerWalletBridge.EvmDiscoverWallets(
                gameObject.name,
                nameof(OnEvmWalletsDiscovered)
            );
            StartWebGLTimeout(WalletSignTimeout, () =>
            {
                if (_pendingEvmSuccess != null || _pendingEvmError != null)
                    OnEvmError("Connection timed out. Please try again.");
            });
    #else
            if (_connector == null || !_connector.IsInitialized)
            {
                IsAuthenticating   = false;
                _pendingEvmSuccess = null;
                _pendingEvmError   = null;
                onError?.Invoke("Wallet connection is not ready yet. Please try again in a moment.");
                return;
            }

            // OnEvmConnected receives the raw EVM address and derives the Algorand
            // LogicSig address in C# — the same funnel the WebGL bridge feeds.
            _connector.ConnectEvm(
                evmAddr => OnEvmConnected(evmAddr),
                err     => OnEvmError(err)
            );
    #endif
        }

        /// <summary>
        /// Callback for BlockmakerWalletBridge.EvmDiscoverWallets on the ConnectEvm
        /// AUTO-CONNECT path (WebGL only). Payload: JSON
        /// {"wallets":[{"rdns","name","icon","lastUsed"},…],"legacy":bool} — or the
        /// sentinel "!none" when no EVM provider is installed at all. This path
        /// ignores the wallet list and auto-picks via EvmConnect(""); the picker UI
        /// uses the separate DiscoverEvmWallets / OnEvmWalletsDiscoveredForUi /
        /// ConnectEvmWallet API below instead.
        /// </summary>
        // True once the current connect attempt has dispatched EvmConnect — stale
        // discovery callbacks (from a cancelled attempt) must not dispatch a second.
        private bool _evmConnectDispatched;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmWalletsDiscovered(string payload)
        {
            if (_pendingEvmSuccess == null && _pendingEvmError == null)
                return; // the connect was cancelled while discovery ran

            if (_evmConnectDispatched)
                return; // a discovery callback already advanced this attempt to connect

            if (payload == "!none")
            {
                OnEvmError("No EVM wallet found. Please install one to continue.");
                return;
            }

            // Do NOT log the payload itself — it now carries base64 icon bytes.
            BlockmakerLog.Info($"[BlockmakerAuth] EVM wallets discovered ({(payload?.Length ?? 0)} chars) — auto-connecting.");

            _evmConnectDispatched = true;
            BlockmakerWalletBridge.EvmConnect(
                "",   // auto-pick: last-used rdns (localStorage) → first announced → window.ethereum
                gameObject.name,
                nameof(OnEvmConnected),
                nameof(OnEvmError)
            );
        }

        // ── EVM wallet picker (discover / connect split) ───────────────────────────
        // The picker UI drives these two calls: DiscoverEvmWallets → show the list →
        // ConnectEvmWallet(chosen rdns). ConnectEvm above stays the no-picker
        // auto-pick path for API compatibility.

        // Single pending slot for the UI discovery callback (last-wins): a newer
        // DiscoverEvmWallets call replaces the callback; the first jslib response
        // consumes the slot and any later stale response finds it empty and is dropped.
        private Action<string> _pendingEvmDiscoverForUi;

        // True while the CURRENT EVM connect attempt came from ConnectEvmWallet (the
        // picker path): OnEvmError then forwards "code|message" to the pending error
        // callback so the UI can branch on EIP-1193 codes. ConnectEvm (the legacy
        // auto-pick path) resets it so its callers keep receiving the bare message.
        private bool _evmConnectErrorsIncludeCode;

        /// <summary>
        /// Discover installed EVM wallets for a picker UI. onResult receives the RAW
        /// jslib payload:
        ///   JSON — {"wallets":[{"rdns","name","icon","lastUsed"},…],"legacy":bool}
        ///   (icon = base64 96x96 PNG, no data: prefix, "" if unavailable; lastUsed
        ///   marks the last-used wallet; legacy = a window.ethereum provider exists)
        ///   — or the sentinel "!none" when no EVM provider is available at all.
        /// On non-WebGL platforms onResult fires immediately with
        /// {"wallets":[],"legacy":false,"native":true} — the UI should skip the
        /// picker and connect via the native (Reown) flow.
        /// Discovery is passive (no wallet popup) and does not touch IsAuthenticating.
        /// </summary>
        public void DiscoverEvmWallets(Action<string> onResult)
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            _pendingEvmDiscoverForUi = onResult;   // single slot, last-wins
            BlockmakerWalletBridge.EvmDiscoverWallets(
                gameObject.name,
                nameof(OnEvmWalletsDiscoveredForUi)
            );
    #else
            onResult?.Invoke("{\"wallets\":[],\"legacy\":false,\"native\":true}");
    #endif
        }

        /// <summary>
        /// Receiver for DiscoverEvmWallets (picker path) — deliberately separate from
        /// OnEvmWalletsDiscovered, which belongs to the ConnectEvm auto-connect flow
        /// and dispatches a connect on arrival. This one only relays the payload.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmWalletsDiscoveredForUi(string payload)
        {
            var cb = _pendingEvmDiscoverForUi;
            _pendingEvmDiscoverForUi = null;
            cb?.Invoke(payload);
        }

        /// <summary>
        /// Connect the SPECIFIC EVM wallet chosen in the picker — like ConnectEvm but
        /// skips discovery and passes rdns straight to the bridge ("" = auto-pick).
        /// A picked rdns that is no longer available FAILS (no silent fallback).
        /// onError always receives "code|message": code is the wallet's numeric
        /// EIP-1193 / JSON-RPC error code when it supplied one (e.g.
        /// "4001|User rejected the request.") and "" otherwise — including timeouts,
        /// busy-guard rejections and native-path errors, e.g. "|Connection timed out.
        /// Please try again.". On non-WebGL platforms this falls back to the
        /// ConnectEvm (Reown) behavior and rdns is ignored.
        /// </summary>
        public void ConnectEvmWallet(
            string rdns,
            Action<IBlockmakerIdentity> onSuccess = null,
            Action<string>              onError   = null)
        {
            if (IsAuthenticating || _pendingEvmSuccess != null || _pendingEvmError != null)
            {
                onError?.Invoke("|Another sign-in is already in progress. Please wait.");
                return;
            }

            IsAuthenticating   = true;
            _pendingEvmSuccess = onSuccess;
            _pendingEvmError   = onError;
            _evmConnectErrorsIncludeCode = true;   // picker path: "code|message" errors
            // The user picked THIS wallet — a stale success echoing any other rdns
            // (an earlier cancelled attempt's still-open popup getting approved)
            // must be dropped, not logged in. Null = accept any (auto-pick paths).
            _evmExpectedRdns = string.IsNullOrEmpty(rdns) ? null : rdns;

    #if UNITY_WEBGL && !UNITY_EDITOR
            // Straight to connect — discovery already ran for the picker. Mark the
            // discover→connect hop as already done so a STALE discovery callback
            // (from an earlier cancelled ConnectEvm) can't dispatch a second
            // EvmConnect and race this one with a duplicate eth_requestAccounts popup.
            _evmConnectDispatched = true;
            if (!string.IsNullOrEmpty(rdns) && rdns.StartsWith("wc:", StringComparison.Ordinal))
            {
                string walletName = rdns.Substring(3);
                BlockmakerWalletBridge.EvmConnectWalletConnect(
                    ResolvedWalletConnectProjectId,
                    walletName,
                    rdns,
                    gameObject.name,
                    nameof(OnWalletQRFromJS),
                    nameof(OnEvmConnected),
                    nameof(OnEvmError)
                );
            }
            else
            {
                BlockmakerWalletBridge.EvmConnect(
                    rdns ?? "",
                    gameObject.name,
                    nameof(OnEvmConnected),
                    nameof(OnEvmError)
                );
            }
            StartWebGLTimeout(WalletSignTimeout, () =>
            {
                if (_pendingEvmSuccess != null || _pendingEvmError != null)
                    OnEvmError("Connection timed out. Please try again.");
            });
    #else
            if (_connector == null || !_connector.IsInitialized)
            {
                IsAuthenticating   = false;
                _pendingEvmSuccess = null;
                _pendingEvmError   = null;
                onError?.Invoke("|Wallet connection is not ready yet. Please try again in a moment.");
                return;
            }

            // Native fallback = ConnectEvm behavior: Reown has no rdns concept, so the
            // chosen rdns is ignored and the same OnEvmConnected/OnEvmError funnel runs.
            _connector.ConnectEvm(
                evmAddr => OnEvmConnected(evmAddr),
                err     => OnEvmError(err)
            );
    #endif
        }

        public void CancelEvmConnect()
        {
            if (_pendingEvmSuccess == null && _pendingEvmError == null) return;
            _connector?.CancelConnection();
            _pendingEvmSuccess = null;
            _pendingEvmError   = null;
            IsAuthenticating   = false;
    #if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.CancelWalletQR();
    #endif
        }

        // rdns the CURRENT connect attempt expects the bridge to echo back; null =
        // accept any (auto-pick / native). Guards the cancel-then-repick race: wallet
        // A's still-open popup approved during attempt B must not become identity A.
        private string _evmExpectedRdns;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmConnected(string payload)
        {
            if (_pendingEvmSuccess == null && _pendingEvmError == null)
                return;

            // WebGL bridge payload is "rdns|0xEvmAddress" (rdns '' for the legacy
            // window.ethereum fallback); the native Reown connector sends a bare
            // "0x…" address. Split on the first '|' when present.
            string echoedRdns = null;
            var evmAddress = payload;
            int sep = payload?.IndexOf('|') ?? -1;
            if (sep >= 0)
            {
                echoedRdns = payload.Substring(0, sep);
                evmAddress = payload.Substring(sep + 1);
            }

            // Stale-success guard: this success is for a DIFFERENT wallet than the
            // current attempt picked — an earlier cancelled attempt's popup was
            // approved late. Ignore it (the current attempt keeps waiting on its own
            // callback); consuming it would log the player into the wrong wallet.
            if (_evmExpectedRdns != null && echoedRdns != null && echoedRdns != _evmExpectedRdns)
            {
                BlockmakerLog.Warning(
                    $"[BlockmakerAuth] Ignoring stale EVM connect success from '{echoedRdns}' — the current attempt expects '{_evmExpectedRdns}'.");
                return;
            }

            CancelWebGLTimeout();

            // The Algorand LogicSig address is derived here in C# (byte-proven
            // against the on-chain LogicSig derivation).
            if (string.IsNullOrEmpty(evmAddress) ||
                !evmAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                OnEvmError("Something went wrong during wallet connection. Please try again.");
                return;
            }

            try
            {
                var algoAddr = XChainAddressDeriver.DeriveAlgorandAddress(evmAddress);
                var prevTier = Tier;
                var identity = new EvmXChainIdentity(algoAddr, evmAddress);
                SetIdentity(identity);
                identity.SaveSession();
                TriggerWalletLogin(identity);

                if (prevTier < IdentityTier.SelfCustody)
                    SafeInvoke(OnIdentityUpgraded, identity, prevTier);

                IsAuthenticating = false;
                _pendingEvmSuccess?.Invoke(identity);
                _pendingEvmSuccess = null;
                _pendingEvmError   = null;
            }
            catch (Exception ex)
            {
                BlockmakerLog.Error($"[BlockmakerAuth] EVM connection error: {ex.Message}");
                OnEvmError("Something went wrong while connecting. Please try again.");
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmError(string error)
        {
            CancelWebGLTimeout();
            if (_pendingEvmSuccess == null && _pendingEvmError == null)
                return;

            // The jslib's EvmConnect errors arrive as "code|message" (numeric EIP-1193
            // code, or empty). Internal, timeout and native-connector errors are bare
            // messages. Normalize: humans (log + OnAuthError) always get the message
            // alone; the pending callback gets "code|message" on the picker path
            // (ConnectEvmWallet) and the bare message on the legacy ConnectEvm path.
            ParseEvmConnectError(error, out var code, out var message);
            BlockmakerLog.Error($"[BlockmakerAuth] EVM connect error: {(string.IsNullOrEmpty(code) ? message : code + " " + message)}");
            SafeInvoke(OnAuthError, message);
            _pendingEvmError?.Invoke(_evmConnectErrorsIncludeCode ? code + "|" + message : message);
            _pendingEvmError   = null;
            _pendingEvmSuccess = null;
            IsAuthenticating   = false;
        }

        /// <summary>
        /// Split a "code|message" EVM connect-error payload (the jslib EvmConnect
        /// error shape). The prefix before the FIRST '|' is accepted as the code only
        /// when it is empty or an integer (optionally negative — JSON-RPC codes like
        /// -32002); otherwise the '|' belonged to a bare message, which is returned
        /// whole with code "". Payloads without any '|' are bare messages too.
        /// </summary>
        private static void ParseEvmConnectError(string payload, out string code, out string message)
        {
            code    = "";
            message = payload ?? "";
            if (string.IsNullOrEmpty(payload)) return;

            int pipe = payload.IndexOf('|');
            if (pipe < 0) return;

            var prefix = payload.Substring(0, pipe);
            if (prefix == "-") return; // a lone minus is not a code
            for (int i = 0; i < prefix.Length; i++)
            {
                char c = prefix[i];
                if (!(char.IsDigit(c) || (c == '-' && i == 0))) return;
            }

            code    = prefix;
            message = payload.Substring(pipe + 1);
        }

        // ── Legacy email login (server-managed wallet) ────────────────────────────

        private Coroutine _otpRequestCoroutine;

        public void RequestEmailOTP(string email, Action onSent, Action<string> onError)
        {
            if (IsAuthenticating)
            {
                onError?.Invoke("Another sign-in is already in progress. Please wait.");
                return;
            }
            if (_otpRequestCoroutine != null)
            {
                onError?.Invoke("A code request is already in progress. Please wait.");
                return;
            }
            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Unable to reach the server. Please check your connection and try again.");
                return;
            }
            _otpRequestCoroutine = StartCoroutine(RequestEmailOTPGuarded(email, onSent, onError));
        }

        private System.Collections.IEnumerator RequestEmailOTPGuarded(string email, Action onSent, Action<string> onError)
        {
            try
            {
                yield return BlockmakerClient.Instance.RequestEmailOTP(email, onSent, onError);
            }
            finally
            {
                _otpRequestCoroutine = null;
            }
        }

        public void VerifyEmailOTP(
            string email,
            string otp,
            Action<IBlockmakerIdentity> onSuccess,
            Action<string>              onError)
        {
            if (IsAuthenticating || _emailOtpCoroutine != null)
            {
                onError?.Invoke("Another sign-in is already in progress. Please wait.");
                return;
            }
            if (BlockmakerClient.Instance == null)
            {
                onError?.Invoke("Unable to reach the server. Please check your connection and try again.");
                return;
            }

            IsAuthenticating = true;
            _emailOtpCoroutine = StartCoroutine(VerifyEmailOTPRoutine(email, otp, onSuccess, onError));
        }

        private IEnumerator VerifyEmailOTPRoutine(
            string email,
            string otp,
            Action<IBlockmakerIdentity> onSuccess,
            Action<string>              onError)
        {
            if (BlockmakerClient.Instance == null)
            {
                _emailOtpCoroutine = null;
                IsAuthenticating = false;
                onError?.Invoke("Unable to reach the server. Please check your connection and try again.");
                yield break;
            }

            EmailIdentity identity = null;
            string        error    = null;

            try
            {
                yield return BlockmakerClient.Instance.VerifyEmailOTP(
                    email, otp,
                    result => { identity = new EmailIdentity(email, result.walletAddress, result.sessionToken, result.refreshToken); },
                    err    => { error = err; }
                );
            }
            finally
            {
                _emailOtpCoroutine = null;
                IsAuthenticating = false;
            }

            if (error != null)
            {
                // Inline-only: the caller's onError renders the message ON the code-entry
                // page. Broadcasting OnAuthError here too made AuthPromptController close
                // the OTP page (HandleAuthError → ShowOptionsPage), so ONE typo ejected
                // the player from code entry — and re-requesting a code burns the
                // 3-per-10-min budget. A wrong code is a field-level error, not an
                // auth-flow failure.
                onError?.Invoke(error);
                yield break;
            }

            var prevTier = Tier;
            SetIdentity(identity);
            identity.SaveSession();

            if (prevTier < IdentityTier.Email)
                SafeInvoke(OnIdentityUpgraded, identity, prevTier);

            onSuccess?.Invoke(identity);
        }

        // ── Transaction signing (JS callbacks) ────────────────────────────────────

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnTxnSignedFromJS(string signedTxnBase64)
        {
            if (_pendingSignGeneration == _signGeneration)
                PendingSignedTxn = signedTxnBase64;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnGroupTxnSignedFromJS(string signedTxnsJson)
        {
            if (_pendingSignGeneration != _signGeneration) return;
            try
            {
                var wrapper = JsonUtility.FromJson<StringArrayWrapper>("{\"items\":" + signedTxnsJson + "}");
                if (wrapper?.items == null)
                {
                    PendingSignError = "Your wallet did not return a signed transaction. Please try again.";
                    return;
                }
                PendingSignedTxns = wrapper.items;
            }
            catch (Exception ex)
            {
                BlockmakerLog.Warning($"[BlockmakerAuth] Failed to parse group sign result: {ex.Message}");
                PendingSignError = "Something went wrong while processing the signed transactions. Please try again.";
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnTxnErrorFromJS(string error)
        {
            if (_pendingSignGeneration == _signGeneration)
                PendingSignError = error;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnMagicTxnErrorFromJS(string error)
        {
            if (_pendingSignGeneration == _signGeneration)
                PendingSignError = BlockmakerErrors.PlayerFacingWalletSigningError(error);
        }

        // ── Logout ─────────────────────────────────────────────────────────────────

        public void Logout()
        {
            StopTokenRefreshTimer();
            _isRefreshing = false;
            CancelWebGLTimeout();
            BeginPendingSign();
            _signAwaiting = false; // logout invalidates any in-flight sign rather than awaiting it

            IsAuthenticating       = false;
            _pendingConnectSuccess = null;
            _pendingConnectError   = null;
            _pendingEvmSuccess     = null;
            _pendingEvmError       = null;
            _pendingEvmDiscoverForUi = null;
            _pendingMagicSuccess   = null;
            _pendingMagicError     = null;
            _isWalletConnecting    = false;
            _walletLoginInFlight   = false;
            // Invalidate + hard-stop any in-flight wallet-signature login: without this a
            // still-polling Login() could complete AFTER logout and write a fresh session
            // (UpdateTokens/SaveSession) for an identity the user just discarded.
            _walletLoginSeq++;
            if (_walletLoginCoroutine != null)
            {
                StopCoroutine(_walletLoginCoroutine);
                _walletLoginCoroutine = null;
            }

            if (_magicLoginCoroutine != null)
            {
                StopCoroutine(_magicLoginCoroutine);
                _magicLoginCoroutine = null;
            }
            if (_otpRequestCoroutine != null)
            {
                StopCoroutine(_otpRequestCoroutine);
                _otpRequestCoroutine = null;
            }
            if (_emailOtpCoroutine != null)
            {
                StopCoroutine(_emailOtpCoroutine);
                _emailOtpCoroutine = null;
            }
            if (_peraConnectCoroutine != null)
            {
                StopCoroutine(_peraConnectCoroutine);
                _peraConnectCoroutine = null;
            }
            CleanupWCv1();

            _connector?.CancelConnection();

    #if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.CancelWalletQR();
    #endif

            string refreshToken = null;
            if (Identity is ServerSignedIdentity e) refreshToken = e.RefreshToken;
            else if (Identity is MagicIdentity m) refreshToken = m.RefreshToken;
            else if (Identity is WalletConnectIdentity wc) refreshToken = wc.RefreshToken;
            else if (Identity is EvmXChainIdentity evm) refreshToken = evm.RefreshToken;
            BlockmakerClient.Instance?.ServerLogout(refreshToken);

            Identity?.ClearSession();
            SetIdentity(new GuestIdentity());
            // Any in-flight boot restore is moot now — the auth state is KNOWN (guest).
            // Idempotent; prevents UI from waiting on a settle that will never come.
            SettleSessionRestore();
            BlockmakerLog.Info("[BlockmakerAuth] Logged out — reverted to guest.");
        }

        // ── Safe event helpers ─────────────────────────────────────────────────────

        private static void SafeInvoke<T>(Action<T> handler, T arg)
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<T>)d).Invoke(arg); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        private static void SafeInvoke<T1, T2>(Action<T1, T2> handler, T1 a, T2 b)
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<T1, T2>)d).Invoke(a, b); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private void SetIdentity(IBlockmakerIdentity identity)
        {
            var oldProvider = Identity?.ProviderName;
            var oldAddress  = Identity?.Address;
            var oldTier     = Identity?.Tier ?? IdentityTier.Guest;

            if (Identity != null && Identity != identity && oldTier > IdentityTier.Guest)
            {
                if (oldProvider != identity.ProviderName ||
                    oldAddress != identity.Address)
                    Identity.ClearSession();
            }
            Identity = identity;
            IsAuthenticating = false;
            BlockmakerLog.Info($"[BlockmakerAuth] Identity set: {identity.ProviderName} | Tier: {identity.Tier}");

            if (identity is EmailIdentity || identity is MagicIdentity ||
                identity is WalletConnectIdentity || identity is EvmXChainIdentity)
                StartTokenRefreshTimer();
            else
                StopTokenRefreshTimer();

            SafeInvoke(OnIdentityChanged, identity);

            if (oldTier > IdentityTier.Guest &&
                identity.Tier > IdentityTier.Guest &&
                !string.IsNullOrEmpty(oldAddress) &&
                !string.IsNullOrEmpty(identity.Address) &&
                oldAddress != identity.Address)
            {
                SafeInvoke(OnWalletAddressChanged, new WalletAddressChangedEventArgs(oldProvider, oldAddress, identity.ProviderName, identity.Address));
            }
        }

        internal static void NotifyIdentityChanged(IBlockmakerIdentity identity)
            => SafeInvoke(OnIdentityChanged, identity);

        /// <summary>
        /// Acquire a player session token (JWT) for a self-custody wallet identity by
        /// running its wallet-signature login (challenge → sign → verify → store).
        /// No-op if the identity already holds a non-empty SessionToken. The login is
        /// fire-and-forget — game code can act on the wallet immediately; the token
        /// lands asynchronously and is then sent on player-authed requests.
        /// </summary>
        private void TriggerWalletLogin(IBlockmakerIdentity identity)
        {
            if (identity == null) return;

            // In-flight guard: the SessionToken check below is a no-op during the async login
            // window (token isn't set until Login()'s last line), so concurrent connect +
            // restore + reconnect events could each start a Login() — duplicate signature
            // prompts and torn SaveSession. _walletLoginInFlight closes that window.
            //
            // CRITICAL: set the flag SYNCHRONOUSLY here, before StartCoroutine. The flag used to
            // be set deep inside RunWalletLogin (after a possible frame of deferral), so two
            // TriggerWalletLogin calls in the SAME frame both passed this guard and both started
            // a full Login() → the wallet was prompted to sign twice. Setting it here makes the
            // second same-frame call early-return. RunWalletLogin now only OWNS/CLEARS the flag
            // (cleared on every exit), never sets it. Logout() also resets it.
            if (_walletLoginInFlight) return;

            if (identity is WalletConnectIdentity wc)
            {
                if (!string.IsNullOrEmpty(wc.SessionToken)) return;
                _walletLoginInFlight = true;

                // The connect approval is done; a SECOND approval (the login signature)
                // is about to arrive in the wallet app. Without this message users think
                // sign-in stalled — the request is easy to miss on a phone.
                SafeInvoke(OnAuthStatus,
                    $"Connected! Now approve the sign-in request in your {wc.ProviderName} app…");
                _walletLoginCoroutine = StartCoroutine(RunWalletLogin(wc, ++_walletLoginSeq));
            }
            else if (identity is EvmXChainIdentity evm)
            {
                if (!string.IsNullOrEmpty(evm.SessionToken)) return;
                _walletLoginInFlight = true;
                _walletLoginCoroutine = StartCoroutine(RunWalletLogin(evm, ++_walletLoginSeq));
            }
        }

        /// <summary>
        /// Runs a wallet-signature login, deferring it while a user-initiated WebGL sign is
        /// in flight so the two signs (which share OnTxnSignedFromJS / the pending-sign slots)
        /// don't race — the generation guard would otherwise make one die "interrupted".
        /// The auto-login is the deferrer, so the user's sign always wins and the login simply
        /// runs afterwards (and is retryable on the next trigger if it never gets a clear slot).
        /// </summary>
        private IEnumerator RunWalletLogin(IBlockmakerIdentity identity, int seq)
        {
            // _walletLoginInFlight was set SYNCHRONOUSLY by TriggerWalletLogin before this
            // coroutine started. This coroutine OWNS the flag from here on: it never sets it,
            // and it must CLEAR it on EVERY exit path (early yield breaks below, plus the
            // success/error callbacks). Otherwise a future TriggerWalletLogin would be stuck.
            //
            // `seq` is this attempt's id (captured from _walletLoginSeq at start). If it stops
            // matching, RetryWalletLogin/CancelWalletLogin/Logout superseded this attempt: the
            // flag now belongs to a NEWER attempt (or was deliberately cleared), so a stale
            // attempt must exit without touching the flag and its late callbacks are ignored.

            // Defer briefly if a sign is already in flight (connect or a user txn sign).
            // Bounded so a stuck sign can't pin the auto-login forever; if it never clears,
            // we abort and rely on the next TriggerWalletLogin / proactive refresh.
            float waited = 0f;
            while ((_isWalletConnecting || IsWebGLSignInFlight) && waited < WalletSignTimeout)
            {
                if (_walletLoginSeq != seq) yield break; // superseded by retry/cancel/logout
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (_walletLoginSeq != seq) yield break;     // superseded by retry/cancel/logout
            if (_isWalletConnecting || IsWebGLSignInFlight)
            {
                BlockmakerLog.Info("[BlockmakerAuth] Auto wallet sign-in deferred — a sign is still in flight; will retry on next trigger.");
                _walletLoginInFlight = false;
                yield break;
            }

            // Re-check guards after the wait (identity may have changed or the token may have
            // arrived while we yielded). We still own the flag, so clear it before bailing.
            if (Identity != identity) { _walletLoginInFlight = false; yield break; }

            string existingToken = null;
            if (identity is WalletConnectIdentity wcCheck) existingToken = wcCheck.SessionToken;
            else if (identity is EvmXChainIdentity evmCheck) existingToken = evmCheck.SessionToken;
            if (!string.IsNullOrEmpty(existingToken)) { _walletLoginInFlight = false; yield break; }

            // try/finally (no catch — legal around `yield` in an iterator) so the flag is
            // ALWAYS cleared, even if Login() throws mid-yield. The per-callback clears stay as
            // the fast path; the finally is the backstop. Idempotent bool write, so a double
            // clear is harmless. Without this, an unhandled exception in the yield region would
            // skip the defensive clear and pin _walletLoginInFlight until Logout().
            try
            {
                if (identity is WalletConnectIdentity wc)
                {
                    yield return wc.Login(
                        onSuccess: () =>
                        {
                            if (_walletLoginSeq != seq) { BlockmakerLog.Info($"[BlockmakerAuth] Ignoring stale wallet sign-in success for {wc.ProviderName} (superseded by retry/cancel)."); return; }
                            _walletLoginInFlight = false;
                            if (Identity == wc) SafeInvoke(OnIdentityChanged, wc);
                        },
                        onError: err =>
                        {
                            if (_walletLoginSeq != seq) { BlockmakerLog.Info($"[BlockmakerAuth] Ignoring stale wallet sign-in error for {wc.ProviderName} (superseded by retry/cancel): {err}"); return; }
                            _walletLoginInFlight = false;
                            BlockmakerLog.Warning($"[BlockmakerAuth] Wallet sign-in failed for {wc.ProviderName}: {err}");
                            // Tell the step-2 panel — a declined/failed sign-in signature
                            // previously only logged, leaving the panel silently pulsing
                            // "approve the request" with no hint anything went wrong.
                            SafeInvoke(OnAuthStatus,
                                wc is LuteIdentity
                                    ? "Sign-in request was declined or failed — choose CONTINUE WITH LUTE to try again, or CANCEL."
                                    : "Sign-in request was declined or failed — use RESEND to try again, or CANCEL.");
                        });
                }
                else if (identity is EvmXChainIdentity evm)
                {
                    yield return evm.Login(
                        onSuccess: () =>
                        {
                            if (_walletLoginSeq != seq) { BlockmakerLog.Info("[BlockmakerAuth] Ignoring stale wallet sign-in success for EVM xChain (superseded by retry/cancel)."); return; }
                            _walletLoginInFlight = false;
                            if (Identity == evm) SafeInvoke(OnIdentityChanged, evm);
                        },
                        onError: err =>
                        {
                            if (_walletLoginSeq != seq) { BlockmakerLog.Info($"[BlockmakerAuth] Ignoring stale wallet sign-in error for EVM xChain (superseded by retry/cancel): {err}"); return; }
                            _walletLoginInFlight = false;
                            BlockmakerLog.Warning($"[BlockmakerAuth] Wallet sign-in failed for EVM xChain: {err}");
                            SafeInvoke(OnAuthStatus,
                                "Sign-in request was declined or failed — use RESEND to try again, or CANCEL.");
                        });
                }
                else
                {
                    _walletLoginInFlight = false;
                }
            }
            finally
            {
                // Defensive backstop: every Login() exit path invokes a callback that clears the
                // flag, but guarantee it's cleared even on an exception or a future Login refactor
                // that returns without one. Seq-guarded: this finally also runs when retry/cancel
                // StopCoroutine()s this attempt (Unity disposes the iterator), and a superseded
                // attempt must NOT clear the flag the newer attempt now owns.
                if (_walletLoginSeq == seq)
                {
                    _walletLoginInFlight  = false;
                    _walletLoginCoroutine = null;
                }
            }
        }

        // ── Wallet-login retry / cancel (the "stuck on approval 2 of 2" escape hatch) ──

        /// <summary>
        /// True when the current identity is a self-custody wallet that has connected
        /// (approval 1) but not yet completed the login signature (approval 2) — i.e. it
        /// holds no session token. This is the state where the step-2 panel is shown and
        /// <see cref="RetryWalletLogin"/> / <see cref="CancelWalletLogin"/> are meaningful.
        /// Intentionally true even while a login attempt is in flight: the whole point of
        /// retry is to replace an attempt whose wallet request expired or was missed.
        /// </summary>
        public static bool CanRetryWalletLogin
        {
            get
            {
                var id = Instance?.Identity;
                if (id is WalletConnectIdentity wc)  return string.IsNullOrEmpty(wc.SessionToken);
                if (id is EvmXChainIdentity     evm) return string.IsNullOrEmpty(evm.SessionToken);
                return false;
            }
        }

        /// <summary>
        /// Abandon any in-flight wallet-signature login attempt and start a fresh one for
        /// the current identity: a new challenge is requested and a NEW sign request is
        /// pushed to the wallet app (the old one may have expired or been dismissed).
        /// Re-fires <see cref="OnAuthStatus"/> so the UI can show "approve the request…"
        /// feedback again. Safe no-op (log only) when <see cref="CanRetryWalletLogin"/> is false.
        /// </summary>
        public void RetryWalletLogin()
        {
            if (!CanRetryWalletLogin)
            {
                BlockmakerLog.Info("[BlockmakerAuth] RetryWalletLogin ignored — no tokenless wallet identity to retry.");
                return;
            }

            BlockmakerLog.Info("[BlockmakerAuth] Retrying wallet sign-in — abandoning the previous attempt and sending a fresh request.");
            AbandonWalletLoginAttempt();

            // Lute needs a browser window opened directly from this button click. The
            // challenge and exact zero-value sign-in transaction are prepared after
            // the click; lute-connect reuses this named window when signing begins.
            if (Identity is LuteIdentity && !PrimeWalletApprovalWindow())
            {
                SafeInvoke(OnAuthStatus,
                    "Your browser blocked Lute. Allow popups for this game, then choose CONTINUE WITH LUTE again.");
                return;
            }

            // Fresh attempt. For WalletConnect identities TriggerWalletLogin itself fires the
            // "Connected! Now approve the sign-in request…" OnAuthStatus message; EVM xChain
            // has no message in the trigger path, so give the UI equivalent feedback here.
            if (Identity is EvmXChainIdentity)
                SafeInvoke(OnAuthStatus, "Approve the sign-in request in your wallet…");
            TriggerWalletLogin(Identity);
        }

        /// <summary>
        /// Reserve a wallet approval surface directly from the current player click.
        /// Only Lute WebGL needs this because its web signer opens a separate window;
        /// other wallets return true without doing anything. Games should call this
        /// at the start of a click that will prepare a transaction asynchronously.
        /// No transaction or account data is sent by this method.
        /// </summary>
        public bool PrimeWalletApprovalWindow()
        {
            if (!(Identity is LuteIdentity)) return true;
#if UNITY_WEBGL && !UNITY_EDITOR
            return BlockmakerWalletBridge.LuteJsPrimeSignWindow() == 1;
#else
            return false;
#endif
        }

        /// <summary>
        /// Close a Lute approval window that was reserved but never received a
        /// transaction (for example because preparation failed). No-op otherwise.
        /// </summary>
        public void CancelPrimedWalletApprovalWindow()
        {
            if (!(Identity is LuteIdentity)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.LuteJsCancelPrimedSignWindow();
#endif
        }

        /// <summary>
        /// Abort the wallet login-signature phase entirely: invalidate any in-flight attempt's
        /// callbacks, clear the guards, and <see cref="Logout"/> back to a clean guest state.
        /// A tokenless wallet identity can't call player-authed endpoints, so keeping it
        /// half-connected only causes confusion — OnIdentityChanged fires via Logout as usual.
        /// </summary>
        public void CancelWalletLogin()
        {
            BlockmakerLog.Info("[BlockmakerAuth] Wallet sign-in cancelled — aborting the login attempt and logging out.");
            AbandonWalletLoginAttempt();
            Logout();
        }

        /// <summary>
        /// Invalidate the in-flight wallet-login attempt (if any) so it can neither complete
        /// nor clear the guards out from under a successor: bumps the attempt seq (late
        /// callbacks become stale no-ops), hard-stops the RunWalletLogin coroutine (which also
        /// tears down the nested Login() wait), clears the in-flight flag, and frees the shared
        /// WebGL pending-sign slots (same idiom as Logout) — bumping the sign generation makes
        /// a zombie JS sign wait exit, and clearing the awaiting bit stops the next attempt
        /// from deferring behind the dead sign for a full WalletSignTimeout.
        /// </summary>
        private void AbandonWalletLoginAttempt()
        {
            _walletLoginSeq++;
            if (_walletLoginCoroutine != null)
            {
                StopCoroutine(_walletLoginCoroutine);
                _walletLoginCoroutine = null;
            }
            _walletLoginInFlight = false;

            BeginPendingSign();
            _signAwaiting = false; // invalidate, don't await, the abandoned sign
        }

        private static IBlockmakerIdentity CreateWalletIdentity(string provider, string address)
        {
            if (provider.Equals(ProviderDefly, StringComparison.OrdinalIgnoreCase))
                return new DeflyIdentity(address);
            if (provider.Equals(ProviderPera, StringComparison.OrdinalIgnoreCase))
                return new PeraIdentity(address);
            if (provider.Equals(ProviderLute, StringComparison.OrdinalIgnoreCase))
                return new LuteIdentity(address);

            throw new ArgumentException($"Unknown wallet provider '{provider}'.", nameof(provider));
        }
    }

    public static class BlockmakerPrefs
    {
        private static string _prefix;

        public static string Prefix
        {
            get
            {
                if (_prefix == null)
                {
                    var config = BlockmakerAuth.Instance?.blockmakerConfig;
                    string id = config != null ? config.gameId : "";
                    if (string.IsNullOrEmpty(id))
                        id = Application.identifier ?? "default";
                    _prefix = $"bm_{id}_";
                }
                return _prefix;
            }
        }

        public static string Key(string baseName) => Prefix + baseName;

        public static void InvalidatePrefix() => _prefix = null;
    }

    [Serializable]
    internal class StringArrayWrapper
    {
        public string[] items;
    }

    public readonly struct WalletQREventArgs
    {
        public string Provider           { get; }
        public string WalletConnectUri   { get; }
        public string QRCodeBase64Png    { get; }

        public WalletQREventArgs(string provider, string walletConnectUri, string qrCodeBase64Png)
        {
            Provider         = provider;
            WalletConnectUri = walletConnectUri;
            QRCodeBase64Png  = qrCodeBase64Png;
        }
    }

    public readonly struct WalletAddressChangedEventArgs
    {
        public string OldProvider { get; }
        public string OldAddress  { get; }
        public string NewProvider { get; }
        public string NewAddress  { get; }

        public WalletAddressChangedEventArgs(string oldProvider, string oldAddress, string newProvider, string newAddress)
        {
            OldProvider = oldProvider;
            OldAddress  = oldAddress;
            NewProvider = newProvider;
            NewAddress  = newAddress;
        }
    }

}
