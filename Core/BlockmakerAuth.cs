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
    ///   Both paths generate a QR code via OnWalletQRReady for display in the UI.
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
            BlockmakerPrefs.InvalidatePrefix();
        }

        // ── Provider constants ─────────────────────────────────────────────────────
        public const string ProviderPera  = "Pera";
        public const string ProviderDefly = "Defly";

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
        /// Used by identity classes instead of hardcoded constants.
        /// </summary>
        internal static float WalletSignTimeout =>
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
            if (!TryRestoreSession())
                SetIdentity(new GuestIdentity());
            else
                VerifyRestoredSession();

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

            foreach (var provider in new[] { "Pera", "Defly" })
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
                BlockmakerLog.Info($"[BlockmakerAuth] Magic session restored: {magicIdentity.Email}");
                return true;
            }

            var emailIdentity = EmailIdentity.TryLoadSession();
            if (emailIdentity != null)
            {
                SetIdentity(emailIdentity);
                BlockmakerLog.Info($"[BlockmakerAuth] Email session restored: {emailIdentity.Email}");
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
                    BlockmakerLog.Info("[BlockmakerAuth] Restored wallet session has no JWT yet — will sign in once the connection is ready.");
                    return;
                }
                BlockmakerLog.Info("[BlockmakerAuth] Restored session has no tokens — clearing.");
                SetIdentity(new GuestIdentity());
                return;
            }

            var capturedIdentity = Identity;

            // If we have a refresh token, use it to get a fresh JWT immediately
            if (!string.IsNullOrEmpty(refreshToken) && BlockmakerClient.Instance != null)
            {
                BlockmakerClient.Instance.RefreshToken(refreshToken, result =>
                {
                    if (this == null) return;
                    if (Identity != capturedIdentity) return;
                    if (result != null && !string.IsNullOrEmpty(result.sessionToken))
                    {
                        if (capturedIdentity is ServerSignedIdentity ss) ss.UpdateTokens(result.sessionToken, result.refreshToken);
                        else if (capturedIdentity is MagicIdentity m) m.UpdateTokens(result.sessionToken, result.refreshToken);
                        else if (capturedIdentity is WalletConnectIdentity cwc) cwc.UpdateTokens(result.sessionToken, result.refreshToken);
                        else if (capturedIdentity is EvmXChainIdentity cevm) cevm.UpdateTokens(result.sessionToken, result.refreshToken);
                        capturedIdentity.SaveSession();
                        BlockmakerLog.Info("[BlockmakerAuth] Session token refreshed on restore.");
                    }
                }, err =>
                {
                    if (this == null) return;
                    if (Identity != capturedIdentity) return;
                    // For wallet identities (WalletConnect/EVM xChain) a refresh failure does NOT
                    // mean the player must drop to Guest: the live wallet relay can re-sign for a
                    // fresh JWT. Clear only the stale tokens and leave the identity in place so the
                    // gated TriggerWalletLogin path (TryReconnectWalletSessions / reconnect / restore
                    // callbacks) re-acquires a JWT once the connection is ready.
                    // Email/Magic keep the original behavior: a failed refresh means re-login.
                    if (capturedIdentity is WalletConnectIdentity cwc)
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
                });
                return;
            }

            // No refresh token — just verify the JWT
            if (BlockmakerClient.Instance == null)
            {
                BlockmakerLog.Warning("[BlockmakerAuth] Cannot verify session — no server connection. Clearing session.");
                SetIdentity(new GuestIdentity());
                return;
            }
            BlockmakerClient.Instance.VerifySessionToken(token, ok =>
            {
                if (this == null) return;
                if (Identity != capturedIdentity) return;
                if (!ok)
                {
                    BlockmakerLog.Info($"[BlockmakerAuth] {capturedIdentity.ProviderName} session expired — please sign in again.");
                    SetIdentity(new GuestIdentity());
                }
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
            foreach (var provider in new[] { "Pera", "Defly" })
            {
                BlockmakerWalletBridge.TryReconnect(
                    provider,
                    gameObject.name,
                    nameof(OnWalletReconnectedFromJS),
                    nameof(OnWalletReconnectFailed)
                );
            }

            // Pera on WebGL connects via the official @perawallet/connect JS SDK, which
            // persists its own session in localStorage. Restore it here so signing works
            // after a page reload. The jslib sends "Pera:<address>" — the same payload
            // shape as TryReconnect — so the existing reconnect receivers are reused
            // (they re-trigger the wallet-signature login when no JWT is stored).
            if (Identity is WalletConnectIdentity peraIdentity &&
                peraIdentity.ProviderName == ProviderPera)
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
    #else
            // Native (non-WebGL): the WCv1 Pera path doesn't go through _connector.TryRestoreSession
            // above. If a wallet identity restored without a JWT, run a fresh wallet-signature login
            // now that we're past Reown init. Login() itself waits for the WCv1 relay to reconnect,
            // and RunWalletLogin defers while any sign is in flight. No-ops if a token already exists.
            if (Identity is WalletConnectIdentity || Identity is EvmXChainIdentity)
                TriggerWalletLogin(Identity);
    #endif
        }

        // ── Connect wallet (QR flow) ───────────────────────────────────────────────

        /// <summary>
        /// Begin a wallet connection.
        ///
        /// Pera: uses native WalletConnect v1 on all platforms.
        /// Defly: uses Reown SDK (WalletConnect v2); falls back to JS bridge on WebGL.
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
                !provider.Equals(ProviderDefly, StringComparison.OrdinalIgnoreCase))
            {
                BlockmakerLog.Error($"[BlockmakerAuth] Unknown wallet provider '{provider}'. Supported: \"{ProviderPera}\", \"{ProviderDefly}\".");
                onError?.Invoke($"Unknown wallet provider \"{provider}\". Please use Pera or Defly.");
                return;
            }

            IsAuthenticating       = true;
            _isWalletConnecting    = true;
            _pendingConnectSuccess = onSuccess;
            _pendingConnectError   = onError;

            if (provider.Equals(ProviderPera, StringComparison.OrdinalIgnoreCase))
            {
    #if UNITY_WEBGL && !UNITY_EDITOR
                // Pera speaks WalletConnect v1 only, and the native WCv1 client cannot
                // run on WebGL (System.Net.WebSockets). Use Pera's official browser SDK
                // (@perawallet/connect) via the jslib bridge instead. Pera renders its
                // OWN connect modal (QR on desktop, deep links on mobile), so no
                // OnWalletQRReady event fires on this path.
                BlockmakerWalletBridge.PeraJsConnect(
                    gameObject.name,
                    nameof(OnPeraJsConnected),
                    nameof(OnPeraJsError)
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
                TriggerWalletLogin(identity);

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
            string error      = null;

            try
            {
                yield return BlockmakerClient.Instance.VerifyMagicToken(
                    didToken, email,
                    result => { jwt = result.sessionToken; refreshTok = result.refreshToken; },
                    err    => { error = err; }
                );
            }
            finally
            {
                _magicLoginCoroutine = null;
            }

            if (error != null)
            {
                BlockmakerLog.Error($"[BlockmakerAuth] Magic server verify failed: {error}");
                SafeInvoke(OnAuthError, "Sign-in could not be completed. Please try again.");
                _pendingMagicError?.Invoke("Sign-in could not be completed. Please try again.");
                _pendingMagicError   = null;
                _pendingMagicSuccess = null;
                IsAuthenticating     = false;
                yield break;
            }

            if (_pendingMagicSuccess == null && _pendingMagicError == null)
                yield break;

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
                BlockmakerLog.Error($"[BlockmakerAuth] Magic login error: {ex.Message}");
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

            BlockmakerLog.Error($"[BlockmakerAuth] Magic login error: {error}");
            SafeInvoke(OnAuthError, error);
            _pendingMagicError?.Invoke(error);
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
            var parts = payload.Split('|');
            if (parts.Length < 3) return;
            BlockmakerLog.Info($"[BlockmakerAuth] Magic JS session confirmed active for {parts[2]}");
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
            var parts = payload.Split('|');
            if (parts.Length < 3) return;
            BlockmakerLog.Info($"[BlockmakerAuth] xChain SDK loaded, EVM wallet reconnected: {parts[2]}");
            // Relay confirmed live. If the restored EVM identity still has no JWT, run a fresh
            // wallet-signature login now (the gated, connection-ready moment). No-ops if a token
            // already exists (VerifyRestoredSession refreshes/verifies any existing one).
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

        /// <summary>Connect an EVM wallet via xChain Accounts. Derives an Algorand LogicSig address.</summary>
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

    #if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletBridge.EvmConnect(
                gameObject.name,
                nameof(OnEvmConnected),
                nameof(OnEvmError)
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

            _connector.ConnectEvm(
                evmAddr =>
                {
                    try
                    {
                        var algoAddr = XChainAddressDeriver.DeriveAlgorandAddress(evmAddr);
                        OnEvmConnected($"EvmXChain|{algoAddr}|{evmAddr}");
                    }
                    catch (Exception ex)
                    {
                        BlockmakerLog.Error($"[BlockmakerAuth] Failed to derive Algorand address: {ex.Message}");
                        OnEvmError("Something went wrong while setting up your wallet. Please try again.");
                    }
                },
                err => OnEvmError(err)
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

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Preserve]
        public void OnEvmConnected(string payload)
        {
            CancelWebGLTimeout();
            if (_pendingEvmSuccess == null && _pendingEvmError == null)
                return;

            var parts = payload.Split('|');
            if (parts.Length < 3)
            {
                OnEvmError("Something went wrong during wallet connection. Please try again.");
                return;
            }

            var algoAddr   = parts[1];
            var evmAddress = parts[2];

            try
            {
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

            BlockmakerLog.Error($"[BlockmakerAuth] EVM connect error: {error}");
            SafeInvoke(OnAuthError, error);
            _pendingEvmError?.Invoke(error);
            _pendingEvmError   = null;
            _pendingEvmSuccess = null;
            IsAuthenticating   = false;
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
                SafeInvoke(OnAuthError, error);
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
            _pendingMagicSuccess   = null;
            _pendingMagicError     = null;
            _isWalletConnecting    = false;
            _walletLoginInFlight   = false;

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
            BlockmakerLog.Info($"[BlockmakerAuth] Identity set: {identity.ProviderName} | {identity.Address} | Tier: {identity.Tier}");

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
                StartCoroutine(RunWalletLogin(wc));
            }
            else if (identity is EvmXChainIdentity evm)
            {
                if (!string.IsNullOrEmpty(evm.SessionToken)) return;
                _walletLoginInFlight = true;
                StartCoroutine(RunWalletLogin(evm));
            }
        }

        /// <summary>
        /// Runs a wallet-signature login, deferring it while a user-initiated WebGL sign is
        /// in flight so the two signs (which share OnTxnSignedFromJS / the pending-sign slots)
        /// don't race — the generation guard would otherwise make one die "interrupted".
        /// The auto-login is the deferrer, so the user's sign always wins and the login simply
        /// runs afterwards (and is retryable on the next trigger if it never gets a clear slot).
        /// </summary>
        private IEnumerator RunWalletLogin(IBlockmakerIdentity identity)
        {
            // _walletLoginInFlight was set SYNCHRONOUSLY by TriggerWalletLogin before this
            // coroutine started. This coroutine OWNS the flag from here on: it never sets it,
            // and it must CLEAR it on EVERY exit path (early yield breaks below, plus the
            // success/error callbacks). Otherwise a future TriggerWalletLogin would be stuck.

            // Defer briefly if a sign is already in flight (connect or a user txn sign).
            // Bounded so a stuck sign can't pin the auto-login forever; if it never clears,
            // we abort and rely on the next TriggerWalletLogin / proactive refresh.
            float waited = 0f;
            while ((_isWalletConnecting || IsWebGLSignInFlight) && waited < WalletSignTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
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
                        onSuccess: () => { _walletLoginInFlight = false; if (Identity == wc) SafeInvoke(OnIdentityChanged, wc); },
                        onError:   err => { _walletLoginInFlight = false; BlockmakerLog.Warning($"[BlockmakerAuth] Wallet sign-in failed for {wc.ProviderName}: {err}"); });
                }
                else if (identity is EvmXChainIdentity evm)
                {
                    yield return evm.Login(
                        onSuccess: () => { _walletLoginInFlight = false; if (Identity == evm) SafeInvoke(OnIdentityChanged, evm); },
                        onError:   err => { _walletLoginInFlight = false; BlockmakerLog.Warning($"[BlockmakerAuth] Wallet sign-in failed for EVM xChain: {err}"); });
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
                // that returns without one.
                _walletLoginInFlight = false;
            }
        }

        private static IBlockmakerIdentity CreateWalletIdentity(string provider, string address)
        {
            if (provider.Equals(ProviderDefly, StringComparison.OrdinalIgnoreCase))
                return new DeflyIdentity(address);
            if (provider.Equals(ProviderPera, StringComparison.OrdinalIgnoreCase))
                return new PeraIdentity(address);

            BlockmakerLog.Warning($"[BlockmakerAuth] Unknown provider '{provider}', defaulting to Pera.");
            return new PeraIdentity(address);
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