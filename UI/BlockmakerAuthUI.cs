using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Blockmaker
{

    /// <summary>
    /// Top-level auth UI MonoBehaviour.
    /// Replaces the old BootManager + LoginScreenUI + OTPScreenUI + WalletUpgradePrompt.
    ///
    /// Attach to the _AuthUI GameObject alongside a UIDocument component.
    /// The UIDocument should reference AuthScreen.uxml as its source asset.
    /// OTPScreen and WalletUpgradePrompt panels are instantiated as child
    /// TemplateContainers at runtime via their VisualTreeAsset references.
    ///
    /// Boot sequence (mirrors old BootManager):
    ///   1. Wait one frame for BlockmakerAuth.Start() to run
    ///   2. Verify server reachability (2 s window, non-blocking)
    ///   3. Check current identity tier
    ///      - Guest + allowGuestAutoRoute → load Loadout
    ///      - Email or SelfCustody        → load Loadout
    ///      - No session                  → show AuthScreen
    ///
    /// After successful login:
    ///   • Fires OnAuthComplete (game code subscribes to this)
    ///   • If no subscribers → falls back to loading loadoutSceneName
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BlockmakerAuthUI : MonoBehaviour
    {
        // ── Public event ───────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player completes auth (any tier >= Guest).
        /// Subscribe in Awake() on your game manager before this fires.
        /// If no one is subscribed, BlockmakerAuthUI falls back to loading
        /// the scene named by <see cref="loadoutSceneName"/>.
        /// </summary>
        public static event Action<IBlockmakerIdentity> OnAuthComplete;

        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Scene Routing")]
        [Tooltip("Fallback scene to load after auth when no OnAuthComplete subscribers exist.")]
        public string loadoutSceneName = "";

        [Header("UI Assets")]
        [Tooltip("The OTPScreen VisualTreeAsset (OTPScreen.uxml)")]
        public VisualTreeAsset otpScreenAsset;

        [Tooltip("The WalletUpgradePrompt VisualTreeAsset (WalletUpgradePrompt.uxml)")]
        public VisualTreeAsset walletUpgradeAsset;

        [Tooltip("The PeraConnectModal VisualTreeAsset (PeraConnectModal.uxml)")]
        public VisualTreeAsset peraConnectAsset;

        [Header("Wallet Logos")]
        [Tooltip("Pera logo sprite (white on transparent)")]
        public Sprite peraLogoIcon;

        [Tooltip("Defly logo sprite (white on transparent)")]
        public Sprite deflyLogoIcon;

        [Tooltip("EVM/MetaMask logo sprite (white on transparent)")]
        public Sprite evmLogoIcon;



        [Header("Appearance")]
        public string gameTitle    = "My Game";
        public string gameSubtitle = "Sign in or play as guest";

        [Header("Behaviour")]
        [Tooltip("Guests are routed to the game without showing the auth screen.")]
        public bool allowGuestAutoRoute = true;

        [Tooltip("Show the wallet upgrade prompt after email login.")]
        public bool showUpgradeAfterEmail = true;

        // ── Private ────────────────────────────────────────────────────────────

        private UIDocument             _doc;
        private VisualElement          _root;
        private AuthScreenController   _authCtrl;
        private OTPScreenController        _otpCtrl;
        private WalletUpgradeController    _upgradeCtrl;
        private PeraConnectModalController _peraCtrl;

        private VisualElement _otpRoot;
        private VisualElement _upgradeRoot;
        private VisualElement _peraRoot;
        private Coroutine     _autoContinueCoroutine;
        private Coroutine     _connectTimeoutCoroutine;
        private bool          _loadingScene;

        public static BlockmakerAuthUI Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            OnAuthComplete = null;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            _doc  = GetComponent<UIDocument>();

            if (_doc.visualTreeAsset == null)
            {
                BlockmakerLog.Error("[BlockmakerAuthUI] UIDocument has no Source Asset assigned. " +
                               "Select _AuthUI in the hierarchy and assign AuthScreen.uxml to the UIDocument component.");
                yield break;
            }

            _root = _doc.rootVisualElement;

            BuildControllers();
            HideAllOverlays();

            BlockmakerAuth.OnWalletAddressChanged += HandleWalletAddressChanged;

            // Wait one frame for BlockmakerAuth.Start() to run
            yield return null;

            // Non-blocking server check — poll until response or 3s timeout
            bool serverReachable = false;
            bool checkDone = false;
            if (BlockmakerClient.Instance != null)
                BlockmakerClient.Instance.VerifyConnection(ok => { serverReachable = ok; checkDone = true; });
            else
                checkDone = true;

            float waited = 0f;
            while (!checkDone && waited < 3f)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            if (!serverReachable)
                BlockmakerLog.Warning("[BlockmakerAuthUI] Server unreachable — offline mode.");

            EvaluateAuthState();
        }

        private void OnDestroy()
        {
            BlockmakerAuth.OnWalletAddressChanged -= HandleWalletAddressChanged;

            if (_autoContinueCoroutine != null)
                StopCoroutine(_autoContinueCoroutine);
            StopConnectTimeout();

            _peraCtrl?.Close();

            if (Instance == this)
                Instance = null;
        }

        // ── Build ──────────────────────────────────────────────────────────────

        private void BuildControllers()
        {
            // Customise title/subtitle
            var titleEl    = _root.Q<Label>("lbl-game-title");
            var subtitleEl = _root.Q<Label>("lbl-game-subtitle");
            if (titleEl    != null) titleEl.text    = gameTitle;
            if (subtitleEl != null) subtitleEl.text = gameSubtitle;

            // Also update the address-panel copy of the title
            var titleEl2 = _root.Q<Label>("lbl-game-title-2");
            if (titleEl2 != null) titleEl2.text = gameTitle;

            // Auth screen
            _authCtrl = new AuthScreenController(_root);
            _authCtrl.OnPeraClicked       = () => ConnectWallet("Pera");
            _authCtrl.OnDeflyClicked      = () => ConnectWallet("Defly");
            _authCtrl.OnMetamaskClicked   = ConnectMetaMask;
            _authCtrl.OnEmailClicked      = ShowOTPScreen;
            _authCtrl.OnGuestClicked      = ContinueAsGuest;
            _authCtrl.OnContinueClicked   = () => { var a = BlockmakerAuth.Instance; if (a != null) FinishAuth(a.Identity); };
            _authCtrl.OnDisconnectClicked = Disconnect;

            var cfg = BlockmakerClient.Instance?.config;
            _authCtrl.SetMetamaskVisible(cfg == null || cfg.enableEvmXChain);

            // OTP screen
            if (otpScreenAsset != null)
            {
                _otpRoot = otpScreenAsset.Instantiate();
                _root.Add(_otpRoot);
                _otpCtrl = new OTPScreenController(_otpRoot, this);
                _otpCtrl.OnSendCode = HandleSendCode;
                _otpCtrl.OnVerify   = HandleVerifyOTP;
                _otpCtrl.OnBack     = () => { BlockmakerAuth.Instance?.CancelPendingMagic(); HideAllOverlays(); _authCtrl.ShowMain(); };
            }

            // Wallet upgrade
            if (walletUpgradeAsset != null)
            {
                _upgradeRoot = walletUpgradeAsset.Instantiate();
                _root.Add(_upgradeRoot);
                _upgradeCtrl = new WalletUpgradeController(_upgradeRoot);
                _upgradeCtrl.OnPeraClicked     = () => { HideUpgrade(); ConnectWallet("Pera"); };
                _upgradeCtrl.OnDeflyClicked    = () => { HideUpgrade(); ConnectWallet("Defly"); };
                _upgradeCtrl.OnMetamaskClicked = () => { HideUpgrade(); ConnectMetaMask(); };
                _upgradeCtrl.OnSkipped         = () => { HideUpgrade(); var a = BlockmakerAuth.Instance; if (a != null) FinishAuth(a.Identity); };
            }

            // Pera connect modal
            if (peraConnectAsset != null)
            {
                _peraRoot = peraConnectAsset.Instantiate();
                _peraRoot.style.position = Position.Absolute;
                _peraRoot.style.left   = 0;
                _peraRoot.style.top    = 0;
                _peraRoot.style.right  = 0;
                _peraRoot.style.bottom = 0;
                _peraRoot.pickingMode  = PickingMode.Ignore;
                _root.Add(_peraRoot);
                _peraCtrl = new PeraConnectModalController(_peraRoot);
                _peraCtrl.OnBackClicked = () =>
                {
                    BlockmakerAuth.Instance?.CancelWalletConnect();
                    BlockmakerAuth.Instance?.CancelEvmConnect();
                };
                _peraRoot.style.display = DisplayStyle.None;
            }
        }

        // ── Auth evaluation ────────────────────────────────────────────────────

        private void EvaluateAuthState()
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null)
            {
                BlockmakerLog.Error("[BlockmakerAuthUI] BlockmakerAuth not found. Ensure _Blockmaker is in the scene.");
                _authCtrl.ShowMain();
                Show();
                return;
            }

            if (auth.Tier == IdentityTier.Guest && allowGuestAutoRoute)
            {
                BlockmakerLog.Info($"[BlockmakerAuthUI] Guest — auto-routing to {loadoutSceneName}.");
                LoadLoadout();
                return;
            }

            if (auth.Tier >= IdentityTier.Email)
            {
                BlockmakerLog.Info($"[BlockmakerAuthUI] Session restored ({auth.Identity.ProviderName}) — routing.");
                _authCtrl.ShowAddress(auth.Identity.ProviderName, auth.Address);
                Show();
                // Give the player a moment to see their connected state, then route
                _autoContinueCoroutine = StartCoroutine(AutoContinueRoutine());
                return;
            }

            // No session — show login
            _authCtrl.ShowMain();
            Show();
        }

        private IEnumerator AutoContinueRoutine()
        {
            yield return new WaitForSecondsRealtime(1.2f);
            _autoContinueCoroutine = null;
            if (this != null && BlockmakerAuth.Instance != null && BlockmakerAuth.Instance.Identity != null)
                FinishAuth(BlockmakerAuth.Instance.Identity);
        }

        // ── Wallet connect ─────────────────────────────────────────────────────

        private const float WalletConnectTimeoutSeconds = 45f;

        private void ConnectWallet(string provider)
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null) { _authCtrl.SetStatus("Unable to connect. Please restart the game."); return; }

            if (_peraCtrl != null)
            {
                Sprite logo = provider == "Defly" ? deflyLogoIcon : peraLogoIcon;
                OpenModalAndConnect(provider, logo);
                return;
            }

            _authCtrl.SetStatus("Connecting…", isError: false);
            _authCtrl.SetLoading(true);
            StartConnectTimeout(msg => _authCtrl.SetStatus(msg, isWarning: true));

            auth.ConnectWallet(
                provider,
                onSuccess: identity =>
                {
                    StopConnectTimeout();
                    _authCtrl.SetLoading(false);
                    _authCtrl.ShowAddress(identity.ProviderName, identity.Address);
                    FinishAuth(identity);
                },
                onError: err =>
                {
                    StopConnectTimeout();
                    _authCtrl.SetLoading(false);
                    _authCtrl.SetStatus(err);
                    BlockmakerLog.Warning($"[BlockmakerAuthUI] Wallet connect error: {err}");
                }
            );
        }

        private void ConnectMetaMask()
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null) { _authCtrl.SetStatus("Unable to connect. Please restart the game."); return; }

            if (_peraCtrl != null)
            {
                _peraCtrl.Open("MetaMask", evmLogoIcon);
                StartConnectTimeout(msg => _peraCtrl.SetStatus(msg));

                auth.ConnectEvm(
                    onSuccess: identity =>
                    {
                        StopConnectTimeout();
                        _peraCtrl.Close();
                        _authCtrl.ShowAddress(identity.ProviderName, identity.Address);
                        FinishAuth(identity);
                    },
                    onError: err =>
                    {
                        StopConnectTimeout();
                        _peraCtrl.Close();
                        _authCtrl.SetStatus(err);
                        BlockmakerLog.Warning($"[BlockmakerAuthUI] EVM connect error: {err}");
                    }
                );
                return;
            }

            _authCtrl.SetStatus("Looking for your wallet app…", isError: false);
            _authCtrl.SetLoading(true);
            StartConnectTimeout(msg => _authCtrl.SetStatus(msg, isWarning: true));

            auth.ConnectEvm(
                onSuccess: identity =>
                {
                    StopConnectTimeout();
                    _authCtrl.SetLoading(false);
                    _authCtrl.ShowAddress(identity.ProviderName, identity.Address);
                    FinishAuth(identity);
                },
                onError: err =>
                {
                    StopConnectTimeout();
                    _authCtrl.SetLoading(false);
                    _authCtrl.SetStatus(err);
                    BlockmakerLog.Warning($"[BlockmakerAuthUI] EVM connect error: {err}");
                }
            );
        }

        private void OpenModalAndConnect(string provider, Sprite logo)
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null) { _authCtrl.SetStatus("Unable to connect. Please restart the game."); return; }

            _peraCtrl.Open(provider, logo);
            StartConnectTimeout(msg => _peraCtrl.SetStatus(msg));

            auth.ConnectWallet(
                provider,
                onSuccess: identity =>
                {
                    StopConnectTimeout();
                    _peraCtrl.Close();
                    _authCtrl.ShowAddress(identity.ProviderName, identity.Address);
                    FinishAuth(identity);
                },
                onError: err =>
                {
                    StopConnectTimeout();
                    _peraCtrl.Close();
                    _authCtrl.SetStatus(err);
                    BlockmakerLog.Warning($"[BlockmakerAuthUI] {provider} connect error: {err}");
                }
            );
        }

        private void StartConnectTimeout(Action<string> onTimeout)
        {
            StopConnectTimeout();
            _connectTimeoutCoroutine = StartCoroutine(ConnectTimeoutRoutine(onTimeout));
        }

        private void StopConnectTimeout()
        {
            if (_connectTimeoutCoroutine != null) { StopCoroutine(_connectTimeoutCoroutine); _connectTimeoutCoroutine = null; }
        }

        private IEnumerator ConnectTimeoutRoutine(Action<string> onTimeout)
        {
            yield return new WaitForSecondsRealtime(WalletConnectTimeoutSeconds);
            _connectTimeoutCoroutine = null;
            onTimeout?.Invoke("Still waiting for your wallet. Make sure the wallet app is open.");
        }

        // ── Email login (Magic or legacy OTP) ─────────────────────────────────

        private void ShowOTPScreen()
        {
            _otpCtrl?.Reset();
            if (_otpRoot != null) _otpRoot.style.display = DisplayStyle.Flex;
        }

        private void HandleSendCode(string email)
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null) { _otpCtrl?.SetStatus("Unable to connect. Please restart the game."); return; }

            var cfg = BlockmakerClient.Instance?.config;
    #if UNITY_WEBGL && !UNITY_EDITOR
            if (cfg != null && cfg.enableMagicEmail && !string.IsNullOrEmpty(cfg.magicPublishableKey))
            {
    #else
    #pragma warning disable CS0162
            if (false) // Magic SDK requires a browser — native builds use OTP
            {
    #endif
                _otpCtrl.SetLoading(true);
                _otpCtrl.SetStatus("Waiting for verification…", isError: false);

                auth.ConnectMagicEmail(
                    email,
                    onSuccess: identity =>
                    {
                        _otpCtrl.SetLoading(false);
                        HandleEmailLoginSuccess(identity);
                    },
                    onError: err =>
                    {
                        _otpCtrl.SetLoading(false);
                        _otpCtrl.SetStatus(err);
                        BlockmakerLog.Warning($"[BlockmakerAuthUI] Magic login error: {err}");
                    }
                );
                return;
            }
    #pragma warning restore CS0162

            _otpCtrl.SetLoading(true);
            _otpCtrl.SetStatus("Sending code…", isError: false);

            auth.RequestEmailOTP(
                email,
                onSent: () =>
                {
                    _otpCtrl.SetLoading(false);
                    _otpCtrl.SetStatus("", isError: false);
                    _otpCtrl.AdvanceToStep2(email);
                },
                onError: err =>
                {
                    _otpCtrl.SetLoading(false);
                    _otpCtrl.SetStatus(err);
                    BlockmakerLog.Warning($"[BlockmakerAuthUI] OTP request error: {err}");
                }
            );
        }

        private void HandleVerifyOTP(string email, string otp)
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null) { _otpCtrl?.SetStatus("Unable to connect. Please restart the game."); return; }

            _otpCtrl.SetLoading(true);
            _otpCtrl.SetStatus("Verifying…", isError: false);

            auth.VerifyEmailOTP(
                email, otp,
                onSuccess: identity =>
                {
                    _otpCtrl.SetLoading(false);
                    HandleEmailLoginSuccess(identity);
                },
                onError: err =>
                {
                    _otpCtrl.SetLoading(false);
                    _otpCtrl.SetStatus(err);
                    BlockmakerLog.Warning($"[BlockmakerAuthUI] OTP verify error: {err}");
                }
            );
        }

        private void HandleEmailLoginSuccess(IBlockmakerIdentity identity)
        {
            if (_otpRoot != null) _otpRoot.style.display = DisplayStyle.None;

            if (showUpgradeAfterEmail && _upgradeCtrl != null)
            {
                _authCtrl.ShowAddress(identity.ProviderName, identity.Address);
                _upgradeCtrl.Show(WalletUpgradeController.UpgradeReason.Generic);
                if (_upgradeRoot != null) _upgradeRoot.style.display = DisplayStyle.Flex;
            }
            else
            {
                FinishAuth(identity);
            }
        }

        // ── Guest / disconnect ─────────────────────────────────────────────────

        private void ContinueAsGuest()
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null) { BlockmakerLog.Error("[BlockmakerAuthUI] BlockmakerAuth not found."); return; }
            auth.CancelPendingMagic();
            auth.CancelWalletConnect();
            auth.CancelEvmConnect();

            if (auth.Tier == IdentityTier.Guest)
            {
                FinishAuth(auth.Identity);
            }
            else
            {
                auth.Logout();
                FinishAuth(auth.Identity);
            }
        }

        private void Disconnect()
        {
            if (_autoContinueCoroutine != null)
            {
                StopCoroutine(_autoContinueCoroutine);
                _autoContinueCoroutine = null;
            }
            HideUpgrade();
            _peraCtrl?.Close();
            BlockmakerAuth.Instance?.Logout();
            _authCtrl.ShowMain();
        }

        // ── Wallet upgrade ─────────────────────────────────────────────────────

        private void HideUpgrade()
        {
            _upgradeCtrl?.Hide();
            if (_upgradeRoot != null) _upgradeRoot.style.display = DisplayStyle.None;
        }

        // ── Auth complete ──────────────────────────────────────────────────────

        private void FinishAuth(IBlockmakerIdentity identity)
        {
            if (_autoContinueCoroutine != null)
            {
                StopCoroutine(_autoContinueCoroutine);
                _autoContinueCoroutine = null;
            }

            Hide();

            var handler = OnAuthComplete;
            if (handler != null)
            {
                foreach (var d in handler.GetInvocationList())
                {
                    try { ((Action<IBlockmakerIdentity>)d).Invoke(identity); }
                    catch (Exception ex) { BlockmakerLog.Exception(ex); }
                }
            }
            else
            {
                LoadLoadout();
            }
        }

        // ── Scene loading ──────────────────────────────────────────────────────

        private void LoadLoadout()
        {
            if (_loadingScene) return;
            if (string.IsNullOrEmpty(loadoutSceneName))
            {
                BlockmakerLog.Warning("[BlockmakerAuthUI] No fallback scene configured in loadoutSceneName. Subscribe to OnAuthComplete to handle auth completion.");
                return;
            }
            _loadingScene = true;
            StartCoroutine(LoadSceneRoutine(loadoutSceneName));
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            SceneManager.LoadScene(sceneName);
        }

        // ── Visibility helpers ─────────────────────────────────────────────────

        public void Show()
        {
            gameObject.SetActive(true);
            if (_root != null) _root.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }

        private void HideAllOverlays()
        {
            if (_otpRoot     != null) _otpRoot.style.display     = DisplayStyle.None;
            if (_upgradeRoot != null) _upgradeRoot.style.display = DisplayStyle.None;
            _peraCtrl?.Close();
        }

        private void HandleWalletAddressChanged(WalletAddressChangedEventArgs e)
        {
            string warning = $"Heads up: you switched to {e.NewProvider}, so items linked to your old {e.OldProvider} account won't show here. To see them again, sign back in with {e.OldProvider}.";
            _authCtrl?.SetStatus(warning, isError: false, isWarning: true);
            BlockmakerLog.Warning($"[BlockmakerAuthUI] Wallet address changed: {e.OldProvider} ({e.OldAddress}) → {e.NewProvider} ({e.NewAddress})");
        }

    }

}