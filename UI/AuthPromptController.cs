using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{

    /// <summary>
    /// Controls the AuthPrompt.uxml overlay — shown when a guest player clicks Profile.
    ///
    /// Wire-up:
    ///   1. Add a UIDocument (Sort Order 100) to a GameObject, assign AuthPrompt.uxml.
    ///   2. Add this component to the same GameObject.
    ///   3. To open the prompt call:  AuthPromptController.Instance.Show()
    ///   4. Subscribe to react after a successful login:
    ///        AuthPromptController.OnAuthSucceeded += () => SceneManager.LoadScene("Profile");
    ///
    /// The prompt manages five pages:
    ///   page-options       — Email / Wallets buttons
    ///   page-evm-wallets   — unified "EVM / AVM Wallets" picker: ONE code-built
    ///                        flat list (Pera + Defly rows always, then the
    ///                        discovered/curated EVM rows below — skeleton row while
    ///                        discovery runs, quiet none-found row when empty) plus
    ///                        the CONNECTING (+ error states) pane — see the
    ///                        "unified wallet picker" region for the state machine
    ///   page-otp           — step1 (email entry) → step2 (6-digit code)
    ///   page-qr            — WalletConnect QR code display
    ///   page-step2         — "STEP 2 OF 2" wait state: the wallet is connected and
    ///                        the free login-signature approval is pending in the
    ///                        wallet app (players kept missing that second request)
    ///
    /// TODO: AuthScreenController and WalletUpgradeController still call the plain
    /// ConnectEvm (auto-pick, no picker). Funnel those surfaces through this prompt's
    /// page-evm-wallets flow so every EVM entry point gets the wallet picker.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AuthPromptController : MonoBehaviour
    {
        // ── Public API ─────────────────────────────────────────────────────────────
        public static AuthPromptController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            OnAuthSucceeded = null;
        }

        /// <summary>Fired when the player successfully authenticates (any tier > Guest).</summary>
        public static event Action OnAuthSucceeded;

        // ── Inspector ──────────────────────────────────────────────────────────────

        [Tooltip("The PeraConnectModal VisualTreeAsset (PeraConnectModal.uxml)")]
        public VisualTreeAsset peraConnectAsset;

        [Header("Wallet Logos")]
        [Tooltip("Pera logo sprite (white on transparent)")]
        public Sprite peraLogoIcon;

        [Tooltip("Defly logo sprite (white on transparent)")]
        public Sprite deflyLogoIcon;

        [Tooltip("EVM/MetaMask logo sprite (white on transparent)")]
        public Sprite evmLogoIcon;

        // ── UI refs ────────────────────────────────────────────────────────────────
        private VisualElement _overlay;
        private VisualElement _pageOptions;
        private VisualElement _pageOtp;
        private VisualElement _pageQr;

        // OTP
        private VisualElement _otpStep1;
        private VisualElement _otpStep2;
        private TextField     _inputEmail;
        private TextField     _inputOtp;
        private Label         _lblSentTo;
        private Button        _btnSendCode;
        private Button        _btnVerify;
        private Button        _btnResend;

        // QR page
        private VisualElement _qrImage;
        private VisualElement _qrLoadingWrap;
        private Label         _lblQrProvider;
        private Button        _btnCopyLink;
        private Button        _btnOpenWallet;

        private Label _lblStatus;

        // Step 2 of 2 (wallet sign-in approval) page
        private VisualElement   _pageStep2;
        private Label           _lblStep2Body;
        private VisualElement[] _step2Dots;
        private IVisualElementScheduledItem _step2DotAnim;
        private int  _step2DotIndex;
        private bool _authAnnounced;   // OnAuthSucceeded already fired this prompt session

        // Step 2 recovery controls (resend the sign request / cancel the login)
        private Button _btnStep2Resend;
        private Button _btnStep2Cancel;
        private Label  _lblStep2ResendHint;
        private IVisualElementScheduledItem _step2ResendCooldown;
        private bool _step2ResendCoolingDown;

        private const string ResendRequestLabel = "RESEND REQUEST";
        private const string ResendSentLabel    = "SENT - CHECK YOUR WALLET";
        private const long   ResendCooldownMs   = 5000;

        // Wallet warning
        private VisualElement _walletWarningBanner;
        private Label         _lblWalletWarning;

        // ── Unified wallet picker (page-evm-wallets) ──────────────────────────────
        private VisualElement _pageEvmWallets;
        private Button        _btnBackCorner;
        private Label         _lblEvmHeading;
        private Label         _lblEvmSubheading;
        private VisualElement _evmStateList;    // list pane: the unified row list
        private VisualElement _evmSubNone;      // quiet "none found" row below the list
        private VisualElement _evmListFade;     // bottom "more below" fade (overflow only)
        private ScrollView    _evmWalletList;   // ONE flat list: Pera/Defly + EVM rows
        private VisualElement _evmStateConnecting;
        private VisualElement _evmConnectIcon;
        private Label         _lblEvmConnectGlyph;
        private Label         _lblEvmConnectTitle;
        private Label         _lblEvmConnectBody;
        private Button        _btnEvmRetry;
        private Button        _btnEvmCancel;
        private VisualElement[] _evmDots;
        private IVisualElementScheduledItem _evmDotAnim;
        private int _evmDotIndex;

        // Loading / None / List describe the EVM portion of the unified list
        // (Loading = the skeleton row is still in the list, None = the quiet
        // "none found" row shows below it); Connecting / Error swap the whole
        // list pane for the connecting pane.
        private enum EvmPageState { Loading, None, List, Connecting, Error }
        private EvmPageState _evmState = EvmPageState.Loading;

        // One badge slot per row (between name and chevron), max one badge —
        // priority RECOMMENDED > RECENT.
        private enum WalletBadge { None, Recommended, Recent }

        // Discovery-in-flight skeleton row (one, below Pera/Defly) + its pulse timer.
        private VisualElement _evmSkeletonRow;
        private IVisualElementScheduledItem _evmSkeletonPulse;
        private const long EvmSkeletonPulseMs = 600;

        // Discovered wallets (lastUsed first, then announced order) + textures we
        // decoded for their icons (destroyed when the list is rebuilt / prompt reset).
        private readonly System.Collections.Generic.List<EvmWalletEntry> _evmWallets =
            new System.Collections.Generic.List<EvmWalletEntry>();
        private readonly System.Collections.Generic.List<Texture2D> _evmIconTextures =
            new System.Collections.Generic.List<Texture2D>();

        private EvmWalletEntry _evmSelectedWallet;   // wallet the connecting pane targets
        private int            _evmFlowSeq;          // stale-callback guard for discovery
        private Coroutine      _evmConnectDelayCoroutine;

        // Matches the DiscoverEvmWallets JSON contract:
        //   {"wallets":[{"rdns","name","icon","lastUsed"}], "legacy":bool, "native":bool}
        // icon = base64 PNG rasterized 96x96 in-browser, may be "".
        [Serializable] private class EvmWalletEntry
        {
            public string rdns;
            public string name;
            public string icon;
            public bool   lastUsed;
        }

        [Serializable] private class EvmWalletDiscovery
        {
            public EvmWalletEntry[] wallets;
            public bool legacy;
            public bool native;
        }

        private string    _pendingEmail;
        private string    _pendingWcUri;
        private string    _pendingProvider;
        private Coroutine _resendCoroutine;
        private Coroutine _connectTimeoutCoroutine;
        private Texture2D _qrTexture;

        // Pera modal
        private VisualElement             _peraRoot;
        private PeraConnectModalController _peraCtrl;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var doc       = GetComponent<UIDocument>();
            var panelRoot = doc.rootVisualElement;

            // TemplateContainer must be absolute + PickingMode.Ignore so it never
            // blocks clicks on the lobby UI that sits behind this overlay.
            if (panelRoot.childCount > 0)
            {
                var tc = panelRoot[0];
                tc.style.position = UnityEngine.UIElements.Position.Absolute;
                tc.style.left     = 0;
                tc.style.top      = 0;
                tc.style.right    = 0;
                tc.style.bottom   = 0;
                tc.pickingMode    = UnityEngine.UIElements.PickingMode.Ignore;
            }

            var root = panelRoot;

            // Pages
            _overlay         = root.Q("auth-overlay");
            _pageOptions     = root.Q("page-options");
            _pageOtp         = root.Q("page-otp");
            _pageQr          = root.Q("page-qr");
            _pageStep2       = root.Q("page-step2");

            // Step 2 of 2 (all null-safe — older UXML simply falls back to the status line)
            _lblStep2Body = root.Q<Label>("lbl-step2-body");
            // The step-2 copy interpolates the picked wallet's UNTRUSTED display name —
            // rich text off so a wallet named "<size=64>" etc. can't mangle/spoof the UI.
            if (_lblStep2Body != null) _lblStep2Body.enableRichText = false;
            _step2Dots    = new[]
            {
                root.Q("step2-dot-1"),
                root.Q("step2-dot-2"),
                root.Q("step2-dot-3"),
            };
            _btnStep2Resend     = root.Q<Button>("btn-step2-resend");
            _btnStep2Cancel     = root.Q<Button>("btn-step2-cancel");
            _lblStep2ResendHint = root.Q<Label>("lbl-step2-resend-hint");
            _btnStep2Resend?.RegisterCallback<ClickEvent>(_ => OnStep2ResendClicked());
            _btnStep2Cancel?.RegisterCallback<ClickEvent>(_ => OnStep2CancelClicked());

            // OTP
            _otpStep1    = root.Q("otp-step1");
            _otpStep2    = root.Q("otp-step2");
            _inputEmail  = root.Q<TextField>("input-email");
            _inputOtp    = root.Q<TextField>("input-otp");
            _lblSentTo   = root.Q<Label>("lbl-sent-to");
            _btnSendCode = root.Q<Button>("btn-send-code");
            _btnVerify   = root.Q<Button>("btn-verify");
            _btnResend   = root.Q<Button>("btn-resend-code");

            // QR
            _qrImage       = root.Q("img-qr");
            _qrLoadingWrap = root.Q("qr-loading");
            _lblQrProvider = root.Q<Label>("lbl-qr-provider");
            _btnCopyLink   = root.Q<Button>("btn-copy-wc-link");
            _btnOpenWallet = root.Q<Button>("btn-open-wallet");

            _lblStatus = root.Q<Label>("lbl-auth-status");

            _walletWarningBanner = root.Q("wallet-warning-banner");
            _lblWalletWarning    = root.Q<Label>("lbl-wallet-warning");

            // Unified wallet picker (all null-safe — older UXML falls back to plain ConnectEvm)
            _pageEvmWallets     = root.Q("page-evm-wallets");
            _lblEvmHeading      = root.Q<Label>("lbl-evm-heading");
            _lblEvmSubheading   = root.Q<Label>("lbl-evm-subheading");
            _evmStateList       = root.Q("evm-state-list");
            _evmSubNone         = root.Q("evm-sub-none");
            _evmListFade        = root.Q("evm-list-fade");
            _evmWalletList      = root.Q<ScrollView>("evm-wallet-list");
            if (_evmWalletList != null)
            {
                // No scrollbar chrome — wheel + touch scrolling still work with
                // Hidden (confirmed) and content is not clipped. A bottom fade
                // (evm-list-fade) signals "more below" when the list overflows.
                _evmWalletList.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
                _evmWalletList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                // Mobile: clamp so overscroll doesn't fight the surrounding canvas.
                _evmWalletList.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            }
            _evmStateConnecting = root.Q("evm-state-connecting");
            _evmConnectIcon     = root.Q("evm-connect-icon");
            _lblEvmConnectGlyph = root.Q<Label>("lbl-evm-connect-glyph");
            _lblEvmConnectTitle = root.Q<Label>("lbl-evm-connect-title");
            _lblEvmConnectBody  = root.Q<Label>("lbl-evm-connect-body");
            _btnEvmRetry        = root.Q<Button>("btn-evm-retry");
            _btnEvmCancel       = root.Q<Button>("btn-evm-cancel");
            _evmDots            = new[]
            {
                root.Q("evm-dot-1"),
                root.Q("evm-dot-2"),
                root.Q("evm-dot-3"),
            };
            // Single card-level corner back button — its action is set per page
            // via SetBack() (routed contextually; hidden on the first page).
            _btnBackCorner = root.Q<Button>("btn-back-corner");
            _btnBackCorner?.RegisterCallback<ClickEvent>(_ => _backAction?.Invoke());
            root.Q<Button>("btn-evm-find-wallet")?.RegisterCallback<ClickEvent>(_ => OnEvmFindWalletClicked());
            _btnEvmRetry?.RegisterCallback<ClickEvent>(_  => OnEvmRetryClicked());
            _btnEvmCancel?.RegisterCallback<ClickEvent>(_ => OnEvmCancelClicked());

            // Placeholder text
            if (_inputEmail != null) _inputEmail.textEdition.placeholder = "your@email.com";
            if (_inputOtp   != null) _inputOtp.textEdition.placeholder   = "6-digit code";

            // Button wiring
            root.Q<Button>("btn-close")?.RegisterCallback<ClickEvent>(_        => OnCloseClicked());
            root.Q<Button>("btn-email")?.RegisterCallback<ClickEvent>(_        => ShowOtpPage());
            // btn-algorand is the single wallet entry now — it opens the unified
            // "EVM / AVM Wallets" page (name kept for back-compat with older UXML).
            root.Q<Button>("btn-algorand")?.RegisterCallback<ClickEvent>(_     => BeginUnifiedWalletFlow());
            // Pera/Defly rows are no longer static UXML — BuildBaseWalletRows adds
            // them to the unified list when the picker page opens. (The old per-page
            // back buttons are gone; the corner button above handles all back nav.)
            _btnCopyLink?.RegisterCallback<ClickEvent>(_ => CopyWcLink());
            // Must run synchronously inside the click handler: on WebGL the deep link is a
            // browser navigation, and iOS Safari only allows it from a user gesture.
            _btnOpenWallet?.RegisterCallback<ClickEvent>(_ => OpenWalletApp());
            _btnSendCode?.RegisterCallback<ClickEvent>(_ => OnSendCodeClicked());
            _btnVerify?.RegisterCallback<ClickEvent>(_   => OnVerifyClicked());
            _btnResend?.RegisterCallback<ClickEvent>(_   => OnResendClicked());

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
                root.Add(_peraRoot);
                _peraCtrl = new PeraConnectModalController(_peraRoot);
                _peraCtrl.OnBackClicked = () =>
                {
                    BlockmakerAuth.Instance?.CancelWalletConnect();
                    BlockmakerAuth.Instance?.CancelEvmConnect();
                    ShowOptionsPage();
                };
                _peraCtrl.OnCloseClicked = () => Hide();
                // Step-2 CANCEL inside the modal: the wallet login is already aborted
                // by the modal controller — land back on the sign-in options with a
                // neutral (non-error) note instead of a dead end.
                _peraCtrl.OnSignInCancelled = () =>
                {
                    ShowOptionsPage();
                    SetStatus("Sign-in cancelled.");
                };
                _peraRoot.style.display = DisplayStyle.None;
            }

            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }

        private void OnEnable()
        {
            BlockmakerAuth.OnIdentityChanged      += HandleIdentityChanged;
            BlockmakerAuth.OnAuthError             += HandleAuthError;
            BlockmakerAuth.OnWalletQRReady         += HandleQRReady;
            BlockmakerAuth.OnAuthStatus            += HandleAuthStatus;
            BlockmakerAuth.OnWalletAddressChanged  += HandleWalletAddressChanged;
            ReownWalletConnector.OnQRReady         += HandleNativeQRReady;
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged      -= HandleIdentityChanged;
            BlockmakerAuth.OnAuthError             -= HandleAuthError;
            BlockmakerAuth.OnWalletQRReady         -= HandleQRReady;
            BlockmakerAuth.OnAuthStatus            -= HandleAuthStatus;
            BlockmakerAuth.OnWalletAddressChanged  -= HandleWalletAddressChanged;
            ReownWalletConnector.OnQRReady         -= HandleNativeQRReady;

            StopResendCountdown();
            StopConnectTimeout();
            CancelEvmConnectDelay();
            StopEvmDots();
            SetLoading(false);
        }

        private void OnDestroy()
        {
            _peraCtrl?.Close();
            if (_qrTexture != null) Destroy(_qrTexture);

            if (Instance == this)
                Instance = null;
        }

        // ── Public show / hide ─────────────────────────────────────────────────────

        public void Show()
        {
            ResetState();
            ShowOptionsPage();

            if (_overlay != null)
                _overlay.style.display = DisplayStyle.Flex;
            else
                BlockmakerLog.Error("[AuthPromptController] auth-overlay not found in AuthPrompt.uxml");
        }

        /// The x button. During the step-2 wait it must also abort the pending wallet
        /// login — otherwise the session lingers half-authenticated behind a closed
        /// prompt. Guarded by NeedsLoginSignature because Hide() also runs on SUCCESS
        /// while the step-2 page is still visible (must not cancel a completed login).
        private void OnCloseClicked()
        {
            bool stepTwoActive =
                (_pageStep2 != null && _pageStep2.style.display == DisplayStyle.Flex) ||
                (_peraCtrl != null && _peraCtrl.IsShowingStepTwo);

            var auth = BlockmakerAuth.Instance;
            if (stepTwoActive && auth != null && NeedsLoginSignature(auth.Identity))
                auth.CancelWalletLogin();

            Hide();
        }

        public void Hide()
        {
            _peraCtrl?.Close();
            StopConnectTimeout();
            // Cancel any in-flight email/Magic login too — otherwise backing out mid-flight
            // leaves IsAuthenticating true and every other sign-in method reports "Another
            // sign-in is already in progress" until the 120s Magic timeout. Safe no-op when
            // nothing is pending. (Mirrors BlockmakerAuthUI's OTP back handler.)
            BlockmakerAuth.Instance?.CancelPendingMagic();
            BlockmakerAuth.Instance?.CancelWalletConnect();
            BlockmakerAuth.Instance?.CancelEvmConnect();
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            ResetState();
        }

        // ── Page switching ─────────────────────────────────────────────────────────

        private void ShowOptionsPage()
        {
            // Backing out to the options page must also cancel a pending email/Magic login,
            // or IsAuthenticating stays true and locks out every other method for ~120s.
            // Safe no-op when nothing is pending. (Mirrors BlockmakerAuthUI's OTP back handler.)
            BlockmakerAuth.Instance?.CancelPendingMagic();
            BlockmakerAuth.Instance?.CancelWalletConnect();
            BlockmakerAuth.Instance?.CancelEvmConnect();
            SetPage(_pageOptions);
            ClearStatus();
        }

        private void ShowOtpPage()
        {
            SetPage(_pageOtp);
            ShowStep1();
            ClearStatus();
        }

        private void ShowStep1()
        {
            if (_otpStep1 != null)    _otpStep1.style.display    = DisplayStyle.Flex;
            if (_otpStep2 != null)    _otpStep2.style.display    = DisplayStyle.None;
            if (_inputEmail != null)  _inputEmail.value = "";
        }

        private void ShowStep2()
        {
            if (_otpStep1 != null)   _otpStep1.style.display    = DisplayStyle.None;
            if (_otpStep2 != null)   _otpStep2.style.display    = DisplayStyle.Flex;
            if (_inputOtp  != null)  _inputOtp.value  = "";
            if (_lblSentTo != null)  _lblSentTo.text   = $"Code sent to {_pendingEmail}";
            StartResendCountdown();
        }

        private void ShowQrPage(string provider)
        {
            SetPage(_pageQr);
            ClearStatus();

            // Update provider label. Strip a trailing " Wallet" from the provider name so
            // "Trust Wallet"/"Coinbase Wallet" don't compose "Scan with Trust Wallet Wallet".
            if (_lblQrProvider != null)
            {
                string providerName = StripWalletSuffix(provider);
                _lblQrProvider.text = string.IsNullOrEmpty(providerName)
                    ? "Scan with your wallet"
                    : $"Scan with {providerName} Wallet";
            }


            // Show loading state; hide QR image until received
            if (_qrLoadingWrap != null) _qrLoadingWrap.style.display = DisplayStyle.Flex;
            if (_qrImage        != null) _qrImage.style.display       = DisplayStyle.None;
            if (_btnCopyLink    != null) _btnCopyLink.style.display    = DisplayStyle.None;
            if (_btnOpenWallet  != null) _btnOpenWallet.style.display  = DisplayStyle.None;
        }

        private void SetPage(VisualElement activePage)
        {
            if (_pageOptions     != null) _pageOptions.style.display     = DisplayStyle.None;
            if (_pageEvmWallets  != null) _pageEvmWallets.style.display  = DisplayStyle.None;
            if (_pageOtp         != null) _pageOtp.style.display         = DisplayStyle.None;
            if (_pageQr          != null) _pageQr.style.display          = DisplayStyle.None;
            if (_pageStep2       != null) _pageStep2.style.display       = DisplayStyle.None;

            if (activePage != _pageStep2)
            {
                StopStepTwoDots();
                ResetStepTwoResend();
            }

            if (activePage != _pageEvmWallets)
            {
                StopEvmDots();
                CancelEvmConnectDelay();
                RemoveEvmSkeletonRow();   // discovery is void — don't leave it pulsing
                _evmFlowSeq++;   // invalidate any in-flight discovery callback
            }

            if (activePage != null) activePage.style.display = DisplayStyle.Flex;

            // Route the shared corner back button for this page. The picker delegates
            // to OnEvmBackClicked (which itself cancels a connect/error or returns to
            // options). The first page has no back.
            if      (activePage == _pageOptions)    SetBack(null);
            else if (activePage == _pageEvmWallets) SetBack(OnEvmBackClicked);
            else if (activePage == _pageOtp)        SetBack(ShowOptionsPage);
            else if (activePage == _pageQr)         SetBack(ShowOptionsPage);
            else                                    SetBack(null);   // step2 etc.: no back mid-flow
        }

        // The corner back button's action for the current page (null = hidden).
        private System.Action _backAction;

        private void SetBack(System.Action action)
        {
            _backAction = action;
            if (_btnBackCorner != null)
                _btnBackCorner.style.display = action == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ── Step 2 of 2 (wallet sign-in approval) ─────────────────────────────────

        /// <summary>
        /// Wallet sign-in needs TWO approvals: (1) connect, (2) a free login signature
        /// that mints the backend session. Players kept approving #1 and missing #2,
        /// so when the signature phase begins we switch whichever skin is on screen
        /// (Pera/Defly connect modal, or this prompt's pages) to a bold "STEP 2 OF 2"
        /// wait state instead of a one-line status. Returns false when neither skin
        /// can show it (older UXML) so callers can fall back to the status label.
        /// </summary>
        private bool EnterStepTwoState(string provider, string failureMessage = null)
        {
            if (_peraCtrl != null && _peraCtrl.IsOpen)
            {
                // The modal covers the prompt, so don't fall through to the page skin;
                // a false return (older modal UXML) means callers keep old behavior.
                return _peraCtrl.ShowStepTwo(provider, failureMessage);
            }

            if (_pageStep2 == null) return false;

            SetPage(_pageStep2);
            ClearStatus();
            bool isFailure = !string.IsNullOrEmpty(failureMessage);
            if (_lblStep2Body != null)
            {
                // A declined/failed signature shows its own message here — otherwise the
                // generic "approve" prompt would overwrite it and the player would see no
                // sign anything went wrong. "in {name}" (not "in your … app") reads right
                // for a desktop extension as well as a mobile app.
                string appName = string.IsNullOrEmpty(provider) ? "your wallet" : provider;
                _lblStep2Body.text = isFailure
                    ? failureMessage
                    : $"Approve the sign-in request in {appName} — it's a free signature, nothing leaves your wallet.";
                _lblStep2Body.EnableInClassList("auth-step2-body--error", isFailure);
            }
            RefreshStepTwoActions();   // keeps RESEND / CANCEL visible after a decline
            if (isFailure) StopStepTwoDots();   // stop the "still waiting" pulse on failure
            else           StartStepTwoDots();
            return true;
        }

        /// Show/hide the resend controls based on whether the auth layer can actually
        /// re-send the sign request. Re-entrant safe: a retry fires OnAuthStatus →
        /// EnterStepTwoState again, and this must not wipe the "SENT" cooldown state.
        private void RefreshStepTwoActions()
        {
            bool canRetry = BlockmakerAuth.CanRetryWalletLogin;

            if (_btnStep2Resend != null)
            {
                _btnStep2Resend.style.display = canRetry ? DisplayStyle.Flex : DisplayStyle.None;
                if (!_step2ResendCoolingDown)
                {
                    _btnStep2Resend.text = ResendRequestLabel;
                    _btnStep2Resend.SetEnabled(canRetry);
                }
            }

            if (_lblStep2ResendHint != null)
                _lblStep2ResendHint.style.display = canRetry ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnStep2ResendClicked()
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null || !BlockmakerAuth.CanRetryWalletLogin) return;

            auth.RetryWalletLogin();

            if (_btnStep2Resend == null) return;
            _step2ResendCoolingDown = true;
            _btnStep2Resend.text = ResendSentLabel;
            _btnStep2Resend.SetEnabled(false);

            if (_step2ResendCooldown == null)
                _step2ResendCooldown = _btnStep2Resend.schedule.Execute(RestoreStepTwoResendButton);
            _step2ResendCooldown.ExecuteLater(ResendCooldownMs);
        }

        private void RestoreStepTwoResendButton()
        {
            _step2ResendCoolingDown = false;
            if (_btnStep2Resend == null) return;
            _btnStep2Resend.text = ResendRequestLabel;
            _btnStep2Resend.SetEnabled(BlockmakerAuth.CanRetryWalletLogin);
        }

        private void ResetStepTwoResend()
        {
            _step2ResendCooldown?.Pause();
            _step2ResendCoolingDown = false;
            if (_btnStep2Resend != null)
            {
                _btnStep2Resend.text = ResendRequestLabel;
                _btnStep2Resend.SetEnabled(true);
            }
        }

        private void OnStep2CancelClicked()
        {
            // Abort the pending wallet login (logs out the half-authenticated wallet
            // identity). HandleIdentityChanged ignores the resulting Guest identity,
            // so route back to the sign-in options explicitly — with a neutral note,
            // not an error.
            BlockmakerAuth.Instance?.CancelWalletLogin();
            ShowOptionsPage();
            SetStatus("Sign-in cancelled.");
        }

        private void StartStepTwoDots()
        {
            if (_pageStep2 == null || _step2Dots == null || _step2Dots.Length == 0) return;
            _step2DotIndex = 0;
            if (_step2DotAnim == null)
                _step2DotAnim = _pageStep2.schedule.Execute(AdvanceStepTwoDot).Every(360);
            else
                _step2DotAnim.Resume();
        }

        private void StopStepTwoDots()
        {
            _step2DotAnim?.Pause();
            if (_step2Dots == null) return;
            foreach (var dot in _step2Dots)
                dot?.RemoveFromClassList("auth-step2-dot--on");
        }

        private void AdvanceStepTwoDot()
        {
            if (_step2Dots == null || _step2Dots.Length == 0) return;
            for (int i = 0; i < _step2Dots.Length; i++)
                _step2Dots[i]?.EnableInClassList("auth-step2-dot--on", i == _step2DotIndex);
            _step2DotIndex = (_step2DotIndex + 1) % _step2Dots.Length;
        }

        /// <summary>True for a self-custody wallet identity that has connected but not
        /// yet completed the login signature (no backend session token yet).</summary>
        private static bool NeedsLoginSignature(IBlockmakerIdentity identity)
        {
            if (identity is WalletConnectIdentity wc)  return string.IsNullOrEmpty(wc.SessionToken);
            if (identity is EvmXChainIdentity   evm)   return string.IsNullOrEmpty(evm.SessionToken);
            return false;
        }

        private string CurrentWalletProviderName()
        {
            var id = BlockmakerAuth.Instance != null ? BlockmakerAuth.Instance.Identity : null;
            if (id == null || id is GuestIdentity) return null;
            return FriendlyWalletNameFor(id);
        }

        /// "Pera"/"Defly" read well in player copy; the internal "EvmXChain" name maps
        /// to the picked EVM wallet's own name when the picker chose one, otherwise it
        /// falls back to the generic "wallet" wording (null -> "wallet" downstream).
        private string FriendlyWalletNameFor(IBlockmakerIdentity identity)
        {
            string provider = identity != null ? identity.ProviderName : null;
            if (string.IsNullOrEmpty(provider)) return null;
            if (provider == "EvmXChain")
                return _evmSelectedWallet != null ? ClampWalletName(_evmSelectedWallet.name) : null;
            return provider;
        }

        // ── Wallet connect ─────────────────────────────────────────────────────────

        private const float WalletConnectTimeoutSeconds = 45f;

        /// True while a wallet connect/sign-in is already pending — used to debounce
        /// wallet-row clicks. Without this, a double-click re-enters the connect flow,
        /// the second attempt hits "already in progress", and its error handler closes
        /// the modal AND cancels the first (still-good) attempt. Covers both the auth
        /// layer's IsAuthenticating flag and the brief pre-connect EVM pane/delay window
        /// (the row click shows the connecting pane 0.6s before IsAuthenticating flips).
        private bool WalletConnectBusy()
        {
            var auth = BlockmakerAuth.Instance;
            if (auth != null && auth.IsAuthenticating)            return true;
            if (_evmConnectDelayCoroutine != null)               return true;
            if (_evmState == EvmPageState.Connecting)             return true;
            return false;
        }

        private void BeginWalletConnect(string provider)
        {
            if (WalletConnectBusy()) return;   // debounce double-clicks on a wallet row
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }

            if (_peraCtrl != null)
            {
                Sprite logo = provider == "Defly" ? deflyLogoIcon : peraLogoIcon;
                BeginModalConnect(provider, logo);
                return;
            }

            ShowQrPage(provider);
            StartConnectTimeout(msg => { if (this != null) SetStatus(msg); });

            BlockmakerAuth.Instance.ConnectWallet(
                provider,
                onSuccess: _ => { if (this == null) return; StopConnectTimeout(); },
                onError: err =>
                {
                    if (this == null) return;
                    StopConnectTimeout();
                    ShowOptionsPage();
                    SetStatus(err, isError: true);
                }
            );
        }

        private void BeginModalConnect(string provider, Sprite logo)
        {
            _peraCtrl.Open(provider, logo);
            StartConnectTimeout(msg => { if (this != null) _peraCtrl.SetStatus(msg); });

            BlockmakerAuth.Instance.ConnectWallet(
                provider,
                onSuccess: _ =>
                {
                    if (this == null) return;
                    StopConnectTimeout();
                    // Connected, but the modal may now be guiding approval 2 of 2
                    // (the login signature) — keep it open until the token lands.
                    if (_peraCtrl.IsShowingStepTwo) return;
                    _peraCtrl.Close();
                },
                onError: err =>
                {
                    if (this == null) return;
                    StopConnectTimeout();
                    _peraCtrl.Close();
                    ShowOptionsPage();
                    SetStatus(err, isError: true);
                }
            );
        }

        // ── Unified wallet picker (page-evm-wallets) ──────────────────────────────
        //
        // One page, ONE code-built flat list: the Pera (RECOMMENDED) + Defly rows are
        // added immediately when the page opens, a single skeleton row pulses below
        // them while EVM discovery (EIP-6963) runs, and the resolved EVM rows replace
        // the skeleton. When config.enableEvmXChain is off the skeleton + discovery
        // are skipped entirely (list = Pera + Defly). Two panes, one active at a time:
        //
        //   ENTRY (options wallet button) → LIST pane (Pera/Defly + skeleton) →
        //   DiscoverEvmWallets →
        //     '!none' / 0 wallets, no legacy → quiet "none found" row (+ Find one)
        //     native:true (non-WebGL)        → curated rows (MetaMask / Rainbow /
        //                                      Coinbase Wallet / Trust Wallet) →
        //                                      BeginEvmConnectFallback(name) (Reown QR)
        //     legacy-only (0 announced)      → one "Browser wallet" row → ConnectEvmWallet("")
        //     1+ wallets                     → rows (lastUsed first w/ RECENT badge,
        //                                      then announced order)
        //   EVM row click → CONNECTING pane → (0.6s registration delay) → ConnectEvmWallet →
        //     success → HandleIdentityChanged (step-2 page or close, as with other wallets)
        //     error   → ERROR on the same pane ("code|message": 4001 / -32002 / generic)
        //   CONNECTING/ERROR: Cancel/Back → CancelEvmConnect → back to the unified list
        //   Pera/Defly rows → BeginWalletConnect (connect modal), exactly as before.

        /// Delay between the row click and the actual connect call, so the connecting
        /// pane registers on screen before the extension popup steals focus.
        private const float EvmConnectClickDelaySeconds = 0.6f;
        private const float EvmDiscoveryTimeoutSeconds  = 12f;
        private const int   EvmWalletNameMaxChars       = 24;
        private const string EvmFindWalletUrl = "https://ethereum.org/en/wallets/find-wallet/";

        /// Entry point for the options-page wallet button — opens the unified list.
        private void BeginUnifiedWalletFlow()
        {
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }

            // Older UXML without the unified page — keep the previous direct behavior.
            if (_pageEvmWallets == null) { BeginEvmConnectFallback(); return; }

            SetPage(_pageEvmWallets);
            ClearStatus();
            ClearEvmWalletState();   // drop rows/textures from a previous pass

            // The Pera/Defly rows show immediately (never animated); the EVM portion
            // is gated by config (the same gate that used to hide the options-page
            // X-Chain button).
            BuildBaseWalletRows();

            var cfg = BlockmakerClient.Instance?.config;
            bool evmEnabled = cfg == null || cfg.enableEvmXChain;
            if (!evmEnabled)
            {
                ShowEvmState(EvmPageState.List);   // Pera/Defly only — no skeleton, no discovery
                return;
            }

            AddEvmSkeletonRow();
            ShowEvmState(EvmPageState.Loading);

            int seq = ++_evmFlowSeq;

            // Discovery is expected to answer fast (it's an in-page query); if the
            // callback never lands, fail soft to the "none found" row — Pera/Defly
            // stay usable either way. Reuses the shared timeout slot so page
            // switches / Hide() stop it as usual.
            StopConnectTimeout();
            _connectTimeoutCoroutine = StartCoroutine(EvmDiscoveryTimeoutRoutine(seq));

            BlockmakerAuth.Instance.DiscoverEvmWallets(result =>
            {
                if (this == null) return;
                if (seq != _evmFlowSeq) return; // user navigated away / restarted the flow
                StopConnectTimeout();
                HandleEvmDiscovery(result);
            });
        }

        private IEnumerator EvmDiscoveryTimeoutRoutine(int seq)
        {
            yield return new WaitForSecondsRealtime(EvmDiscoveryTimeoutSeconds);
            _connectTimeoutCoroutine = null;
            if (seq != _evmFlowSeq) yield break;
            // Soft-fail in place: Pera/Defly stay clickable above; the EVM portion
            // just reports nothing found.
            BlockmakerLog.Warning("[AuthPrompt] EVM wallet discovery timed out.");
            ShowEvmState(EvmPageState.None);
        }

        private void HandleEvmDiscovery(string result)
        {
            // No EVM provider at all → the quiet "none found" row (+ Find one).
            if (result == "!none")
            {
                // No extension installed: keep the major wallets available through
                // the WalletConnect QR fallback instead of presenting a dead end.
                AppendCuratedEvmRows();
                ShowEvmState(EvmPageState.List);
                return;
            }

            EvmWalletDiscovery discovery = null;
            try { discovery = JsonUtility.FromJson<EvmWalletDiscovery>(result); }
            catch (Exception e) { BlockmakerLog.Warning($"[AuthPrompt] EVM discovery payload unreadable: {e.Message}"); }

            if (discovery == null)
            {
                // Unreadable payload — offer the generic auto-pick row so a present
                // wallet is still reachable (errors land on the connecting pane).
                ShowBrowserWalletRow();
                return;
            }

            // Native (non-WebGL) → no in-page wallets to pick from; the curated set,
            // each row into the existing Reown QR flow.
            if (discovery.native)
            {
                if (_evmWalletList == null) { ShowEvmState(EvmPageState.None); return; }
                AppendCuratedEvmRows();
                ShowEvmState(EvmPageState.List);
                return;
            }

            int count = discovery.wallets != null ? discovery.wallets.Length : 0;

            // Legacy-only window.ethereum (no EIP-6963 announcements) → one generic
            // row; there is nothing meaningful to name (ConnectEvmWallet("") = auto-pick).
            if (count == 0)
            {
                if (discovery.legacy) ShowBrowserWalletRow();
                else
                {
                    AppendCuratedEvmRows();
                    ShowEvmState(EvmPageState.List);
                }
                return;
            }

            // Order: lastUsed first (RECENT badge), then announced order (stable).
            // The unified list always shows Pera/Defly above — recent never rises
            // above Defly — so even a single EVM wallet is listed; no auto-advance
            // into its connect pane.
            _evmWallets.Clear();
            foreach (var w in discovery.wallets) if (w != null &&  w.lastUsed) _evmWallets.Add(w);
            foreach (var w in discovery.wallets) if (w != null && !w.lastUsed) _evmWallets.Add(w);

            AppendDiscoveredEvmRows();
            AppendMissingCuratedEvmRows();
            ShowEvmState(EvmPageState.List);
        }

        /// One generic "Browser wallet" row → ConnectEvmWallet("") (auto-pick / legacy
        /// window.ethereum). Used for legacy-only pages and unreadable discovery payloads.
        private void ShowBrowserWalletRow()
        {
            if (_evmWalletList == null) { ShowEvmState(EvmPageState.None); return; }
            RemoveEvmSkeletonRow();
            var legacyEntry = new EvmWalletEntry { rdns = string.Empty, name = "Browser wallet", icon = string.Empty };
            var row = BuildWalletRow(legacyEntry.name, null, WalletBadge.None,
                                     (icon, glyph) => ApplyEvmWalletIcon(icon, glyph, legacyEntry),
                                     () => BeginEvmWalletConnect(legacyEntry));
            AnimateRowEnter(row, 0);
            _evmWalletList.Add(row);
            ShowEvmState(EvmPageState.List);
        }

        // ── EVM state switching ────────────────────────────────────────────────────

        private void ShowEvmState(EvmPageState state)
        {
            _evmState = state;

            bool connectingPane = state == EvmPageState.Connecting || state == EvmPageState.Error;

            // The list pane (the unified row list) backs every non-connecting state.
            if (_evmStateList       != null) _evmStateList.style.display       = connectingPane ? DisplayStyle.None : DisplayStyle.Flex;
            if (_evmStateConnecting != null) _evmStateConnecting.style.display = connectingPane ? DisplayStyle.Flex : DisplayStyle.None;

            // The unified list stays visible with the list pane (it holds Pera/Defly);
            // Loading just means the skeleton row is still in it. The quiet "none
            // found" row shows below the list only when discovery came back empty.
            if (_evmWalletList != null) _evmWalletList.style.display = connectingPane ? DisplayStyle.None : DisplayStyle.Flex;
            if (_evmSubNone    != null) _evmSubNone.style.display    = state == EvmPageState.None ? DisplayStyle.Flex : DisplayStyle.None;

            // Bottom "more below" fade: hidden on the connecting pane; on any list
            // state re-measure after layout settles and show it only if the list
            // overflows (a fade over a short list would darken the last real row).
            if (_evmListFade != null)
            {
                if (connectingPane) _evmListFade.style.display = DisplayStyle.None;
                else                UpdateEvmListFade();
            }

            if (state != EvmPageState.Loading) RemoveEvmSkeletonRow();

            if (_lblEvmHeading != null)
                _lblEvmHeading.text = connectingPane ? "Connect Wallet" : "Connect a Wallet";
            if (_lblEvmSubheading != null)
                _lblEvmSubheading.style.display = connectingPane ? DisplayStyle.None : DisplayStyle.Flex;

            if (state == EvmPageState.Connecting) StartEvmDots();
            else                                  StopEvmDots();
        }

        /// Show the bottom fade only when the wallet list actually overflows its
        /// viewport (max-height 385px). Measured one tick later so the ScrollView
        /// has laid out its freshly (re)built rows; comparing the content height to
        /// the viewport height is the "scroll range > 0" test.
        private void UpdateEvmListFade()
        {
            if (_evmListFade == null || _evmWalletList == null) return;
            _evmWalletList.schedule.Execute(() =>
            {
                if (_evmListFade == null || _evmWalletList == null) return;
                var content  = _evmWalletList.contentContainer;
                var viewport = _evmWalletList.contentViewport;
                if (content == null || viewport == null) return;
                float contentH  = content.layout.height;
                float viewportH = viewport.layout.height;
                // NaN before first layout → treat as no overflow (a later state
                // change re-runs this once real heights exist).
                bool overflow = !float.IsNaN(contentH) && !float.IsNaN(viewportH)
                                && contentH > viewportH + 1f;
                _evmListFade.style.display = overflow ? DisplayStyle.Flex : DisplayStyle.None;
            }).ExecuteLater(1);
        }

        // ── Unified wallet list (rows built in code) ──────────────────────────────

        // Curated wallets for native/editor builds (replaces the single "scan QR"
        // row) — each connects via the existing Reown QR fallback flow. Letter-glyph
        // chips with FIXED hues (same S/V as WalletFallbackColor).
        // letter/hue are the fallback chip if the logo texture is ever missing.
        private static readonly (string name, string letter, float hue, string logoClass)[] CuratedEvmWallets =
        {
            ("MetaMask",        "M",  30f, "wallet-logo--metamask"),
            ("Rainbow",         "R", 260f, "wallet-logo--rainbow"),
            ("Coinbase Wallet", "C", 220f, "wallet-logo--coinbase"),
            ("Trust Wallet",    "T", 160f, "wallet-logo--trust"),
        };

        /// The Pera (RECOMMENDED) + Defly rows that top the unified list on every
        /// platform. Added immediately on page open — never animated, never rebuilt
        /// by discovery.
        private void BuildBaseWalletRows()
        {
            if (_evmWalletList == null) return;

            _evmWalletList.Add(BuildWalletRow(
                "Pera Wallet", null, WalletBadge.None,
                (icon, glyph) => icon.AddToClassList("auth-btn-icon--pera"),
                () => BeginWalletConnect("Pera")));

            _evmWalletList.Add(BuildWalletRow(
                "Defly Wallet", null, WalletBadge.None,
                (icon, glyph) => icon.AddToClassList("auth-btn-icon--defly"),
                () => BeginWalletConnect("Defly")));
        }

        /// Discovered (EIP-6963) rows, appended below Pera/Defly in _evmWallets
        /// order (lastUsed first → RECENT badge, then announced order).
        private void AppendDiscoveredEvmRows()
        {
            if (_evmWalletList == null) return;
            RemoveEvmSkeletonRow();

            int index = 0;
            foreach (var wallet in _evmWallets)
            {
                var entry = wallet; // capture per-row
                var row = BuildWalletRow(
                    entry.name, null,
                    entry.lastUsed ? WalletBadge.Recent : WalletBadge.None,
                    (icon, glyph) => ApplyEvmWalletIcon(icon, glyph, entry),
                    () => BeginEvmWalletConnect(entry));
                AnimateRowEnter(row, index++);
                _evmWalletList.Add(row);
            }
        }

        /// The curated rows for native/editor builds, plus the muted "these connect
        /// by QR" caption below the last row.
        private void AppendCuratedEvmRows()
        {
            if (_evmWalletList == null) return;
            RemoveEvmSkeletonRow();

            int index = 0;
            foreach (var (name, letter, hue, logoClass) in CuratedEvmWallets)
            {
                string walletName  = name;   // capture per-row
                string chipLetter  = letter;
                float  chipHue     = hue;
                string chipLogo    = logoClass;
                var row = BuildWalletRow(
                    walletName, null, WalletBadge.None,
                    (icon, glyph) => ApplyCuratedWalletIcon(icon, glyph, chipLetter, chipHue, chipLogo),
                    () => BeginCuratedEvmConnect(walletName));
                AnimateRowEnter(row, index++);
                _evmWalletList.Add(row);
            }

            var caption = new Label("These connect by QR — scan with the wallet's mobile app");
            caption.AddToClassList("evm-native-caption");
            _evmWalletList.Add(caption);
        }

        /// Add QR rows only for major wallets that were not already announced by
        /// EIP-6963. Installed extensions remain the preferred one-click path;
        /// these rows make the same wallets reachable from mobile/clean browsers.
        private void AppendMissingCuratedEvmRows()
        {
            if (_evmWalletList == null) return;
            foreach (var (name, letter, hue, logoClass) in CuratedEvmWallets)
            {
                bool installed = _evmWallets.Exists(w =>
                    w != null && !string.IsNullOrEmpty(w.name) &&
                    w.name.IndexOf(name.Split(' ')[0], StringComparison.OrdinalIgnoreCase) >= 0);
                if (installed) continue;
                string walletName = name;
                string chipLetter = letter;
                float chipHue = hue;
                string chipLogo = logoClass;
                _evmWalletList.Add(BuildWalletRow(
                    walletName, "Connect by QR", WalletBadge.None,
                    (icon, glyph) => ApplyCuratedWalletIcon(icon, glyph, chipLetter, chipHue, chipLogo),
                    () => BeginCuratedEvmConnect(walletName)));
            }
        }

        private void BeginCuratedEvmConnect(string walletName)
        {
            if (WalletConnectBusy()) return;   // debounce double-clicks on a wallet row
#if UNITY_WEBGL && !UNITY_EDITOR
            BeginEvmWalletConnect(new EvmWalletEntry
            {
                rdns = "wc:" + walletName,
                name = walletName,
                icon = string.Empty
            });
#else
            BeginEvmConnectFallback(walletName);
#endif
        }

        /// <summary>
        /// ONE wallet row for the unified list — Pera, Defly, discovered EVM,
        /// curated EVM and the legacy "Browser wallet" row all come through here:
        /// icon chip + name (+ optional second line) + one badge slot + chevron.
        /// paintIcon fills the 40x40 chip (USS logo class, decoded PNG, or
        /// letter-glyph fallback).
        /// </summary>
        private Button BuildWalletRow(string displayName, string subText, WalletBadge badge,
                                      Action<VisualElement, Label> paintIcon, Action onClick)
        {
            var row = new Button(() => onClick?.Invoke()) { text = string.Empty };
            row.AddToClassList("auth-btn");
            row.AddToClassList("auth-btn--wallet");
            row.AddToClassList("wallet-row");
            if (!string.IsNullOrEmpty(subText))
                row.AddToClassList("wallet-row--two-line");

            // Icon chip: the painter decides — USS logo class (Pera/Defly), decoded
            // PNG, or a letter-glyph chip with a per-wallet hue.
            var icon = new VisualElement();
            icon.AddToClassList("auth-btn-icon");
            icon.AddToClassList("wallet-row-icon");
            var glyph = new Label();
            glyph.AddToClassList("auth-btn-icon-glyph");
            glyph.AddToClassList("wallet-row-glyph");
            icon.Add(glyph);
            paintIcon?.Invoke(icon, glyph);
            row.Add(icon);

            // Name: untrusted text — rich text off, length-clamped.
            var nameLbl = new Label(ClampWalletName(displayName));
            nameLbl.enableRichText = false;
            nameLbl.AddToClassList("auth-btn-label");
            nameLbl.AddToClassList("wallet-row-name");

            if (!string.IsNullOrEmpty(subText))
            {
                var body = new VisualElement();
                body.AddToClassList("auth-btn-body");
                body.Add(nameLbl);
                var sub = new Label(subText);
                sub.AddToClassList("auth-btn-sub");
                body.Add(sub);
                row.Add(body);
            }
            else
            {
                row.Add(nameLbl);
            }

            // One badge slot between name and chevron. Only RECENT ships today
            // (RECOMMENDED was dropped); the enum keeps the slot extensible.
            if (badge != WalletBadge.None)
            {
                var badgeLbl = new Label("RECENT");
                badgeLbl.AddToClassList("evm-badge");
                row.Add(badgeLbl);
            }

            var chevWrap = new VisualElement();
            chevWrap.AddToClassList("auth-btn-chevron");
            var chev = new VisualElement();
            chev.AddToClassList("auth-chevron");
            chevWrap.Add(chev);
            row.Add(chevWrap);

            return row;
        }

        /// Pop-in for rows added after discovery resolves: start faded + 6px low
        /// (.wallet-row--enter), then drop the class on a 40ms-staggered schedule so
        /// the USS opacity/translate transition (160ms ease-out) runs. Pera/Defly
        /// are added before discovery and never animate.
        private static void AnimateRowEnter(VisualElement row, int index)
        {
            row.AddToClassList("wallet-row--enter");
            row.schedule
               .Execute(() => row.RemoveFromClassList("wallet-row--enter"))
               .ExecuteLater(16 + index * 40);
        }

        // ── Discovery skeleton row ─────────────────────────────────────────────────

        /// One ghost row (row-shaped block + chip ghost + name-bar ghost) below
        /// Pera/Defly while DiscoverEvmWallets runs. Pulses opacity 0.55↔1.0 by
        /// toggling a --pulse class every 600ms — the USS opacity transition eases it.
        private void AddEvmSkeletonRow()
        {
            if (_evmWalletList == null || _evmSkeletonRow != null) return;

            var row = new VisualElement();
            row.AddToClassList("wallet-skeleton");
            var chip = new VisualElement();
            chip.AddToClassList("wallet-skeleton-chip");
            row.Add(chip);
            var bar = new VisualElement();
            bar.AddToClassList("wallet-skeleton-bar");
            row.Add(bar);

            _evmWalletList.Add(row);
            _evmSkeletonRow   = row;
            _evmSkeletonPulse = row.schedule
                .Execute(() => row.ToggleInClassList("wallet-skeleton--pulse"))
                .Every(EvmSkeletonPulseMs);
        }

        private void RemoveEvmSkeletonRow()
        {
            _evmSkeletonPulse?.Pause();
            _evmSkeletonPulse = null;
            if (_evmSkeletonRow != null)
            {
                _evmSkeletonRow.RemoveFromHierarchy();
                _evmSkeletonRow = null;
            }
        }

        /// <summary>
        /// Paints a wallet icon element: base64 PNG when it decodes, otherwise a
        /// letter-glyph chip (first letter of the name, deterministic hue from rdns).
        /// </summary>
        private void ApplyEvmWalletIcon(VisualElement icon, Label glyph, EvmWalletEntry wallet)
        {
            Texture2D texture = DecodeWalletIcon(wallet.icon);
            if (texture != null)
            {
                icon.style.backgroundImage = new StyleBackground(texture);
                icon.style.backgroundColor = Color.clear;
                if (glyph != null) glyph.style.display = DisplayStyle.None;
            }
            else
            {
                icon.style.backgroundImage = StyleKeyword.None;
                icon.style.backgroundColor = WalletFallbackColor(wallet.rdns ?? wallet.name);
                if (glyph != null)
                {
                    glyph.style.display = DisplayStyle.Flex;
                    glyph.text = WalletGlyphLetter(wallet.name);
                }
            }
        }

        private Texture2D DecodeWalletIcon(string base64Png)
        {
            if (string.IsNullOrEmpty(base64Png)) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64Png); }
            catch { return null; }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
            _evmIconTextures.Add(tex);
            return tex;
        }

        private static string ClampWalletName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Wallet";
            name = name.Trim();
            if (name.Length == 0) return "Wallet";
            if (name.Length <= EvmWalletNameMaxChars) return name;
            return name.Substring(0, EvmWalletNameMaxChars - 1) + "…";
        }

        private static string WalletGlyphLetter(string name)
        {
            if (!string.IsNullOrEmpty(name))
                foreach (char c in name)
                    if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
            return "W";
        }

        /// Deterministic per-wallet chip color (FNV-1a over rdns → hue), muted to sit
        /// on the dark card like the other icon chips.
        private static Color WalletFallbackColor(string key)
        {
            uint hash = 2166136261;
            if (!string.IsNullOrEmpty(key))
                foreach (char c in key) { hash ^= c; hash *= 16777619; }
            float hue = (hash % 360u) / 360f;
            return Color.HSVToRGB(hue, 0.42f, 0.38f);
        }

        /// Letter-glyph chip for a curated wallet: FIXED hue (same S/V as
        /// WalletFallbackColor) so the four keep stable brand-adjacent colors
        /// without shipping logo assets.
        private static void ApplyCuratedWalletIcon(VisualElement icon, Label glyph, string letter, float hue, string logoClass = null)
        {
            if (!string.IsNullOrEmpty(logoClass))
            {
                // Real brand logo (USS background-image); no letter chip.
                icon.style.backgroundColor = StyleKeyword.None;
                icon.AddToClassList(logoClass);
                if (glyph != null) glyph.style.display = DisplayStyle.None;
                return;
            }
            icon.style.backgroundImage = StyleKeyword.None;
            icon.style.backgroundColor = Color.HSVToRGB(hue / 360f, 0.42f, 0.38f);
            if (glyph != null)
            {
                glyph.style.display = DisplayStyle.Flex;
                glyph.text = letter;
            }
        }

        // ── EVM connecting pane ────────────────────────────────────────────────────

        private void BeginEvmWalletConnect(EvmWalletEntry wallet)
        {
            if (wallet == null) return;
            // Debounce double-clicks on a wallet row. OnEvmRetryClicked cancels the prior
            // attempt first (clearing IsAuthenticating) and re-enters from the Error state,
            // so a legitimate retry is not blocked by this guard.
            if (WalletConnectBusy()) return;
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }

            _evmSelectedWallet = wallet;

            string name = ClampWalletName(wallet.name);

            if (_evmConnectIcon != null && _lblEvmConnectGlyph != null)
                ApplyEvmWalletIcon(_evmConnectIcon, _lblEvmConnectGlyph, wallet);

            if (_lblEvmConnectTitle != null) _lblEvmConnectTitle.text = "Requesting connection…";
            SetEvmConnectBody($"Accept the request in {name}.", isError: false);
            if (_btnEvmRetry  != null) _btnEvmRetry.style.display = DisplayStyle.None;
            if (_btnEvmCancel != null) _btnEvmCancel.text = "CANCEL";

            ShowEvmState(EvmPageState.Connecting);

            // Give the pane a beat to register before the extension popup steals focus.
            CancelEvmConnectDelay();
            _evmConnectDelayCoroutine = StartCoroutine(EvmConnectAfterDelay(wallet));
        }

        private IEnumerator EvmConnectAfterDelay(EvmWalletEntry wallet)
        {
            yield return new WaitForSecondsRealtime(EvmConnectClickDelaySeconds);
            _evmConnectDelayCoroutine = null;

            if (_evmState != EvmPageState.Connecting || _evmSelectedWallet != wallet) yield break;

            StartConnectTimeout(msg =>
            {
                if (this == null) return;
                if (_evmState == EvmPageState.Connecting)
                    SetEvmConnectBody(msg, isError: false);
            });

            BlockmakerAuth.Instance.ConnectEvmWallet(
                wallet.rdns,
                onSuccess: _ =>
                {
                    if (this == null) return;
                    StopConnectTimeout();
                    // Success closes the prompt (or enters the step-2 wait state) via
                    // HandleIdentityChanged, exactly like the other wallets.
                },
                onError: err =>
                {
                    if (this == null) return;
                    StopConnectTimeout();
                    if (_evmState != EvmPageState.Connecting) return; // cancelled meanwhile
                    ShowEvmConnectError(err, wallet);
                }
            );
        }

        /// onError payload is "code|message" — code may be "" or an EIP-1193 number.
        private void ShowEvmConnectError(string payload, EvmWalletEntry wallet)
        {
            string code = string.Empty;
            string message = payload ?? string.Empty;
            int sep = message.IndexOf('|');
            if (sep >= 0)
            {
                code    = message.Substring(0, sep).Trim();
                message = message.Substring(sep + 1);
            }

            string name = ClampWalletName(wallet != null ? wallet.name : null);
            bool showRetry;
            string body;

            switch (code)
            {
                case "4001":     // EIP-1193: user rejected the request
                    body      = $"Request cancelled — you declined in {name}.";
                    showRetry = true;
                    break;
                case "-32002":   // EIP-1193: request already pending — do NOT auto re-request
                    body      = $"A connection request is already waiting — open {name} and approve it.";
                    showRetry = false;
                    break;
                default:
                    // Includes server-side signature-verify failures (e.g. unsupported
                    // smart-contract wallets) — show the message verbatim when we have one.
                    body      = string.IsNullOrEmpty(message)
                        ? $"Couldn't connect to {name}. Please try again."
                        : message;
                    showRetry = true;
                    break;
            }

            if (_lblEvmConnectTitle != null) _lblEvmConnectTitle.text = "Connection failed";
            SetEvmConnectBody(body, isError: true);
            if (_btnEvmRetry  != null) _btnEvmRetry.style.display = showRetry ? DisplayStyle.Flex : DisplayStyle.None;
            if (_btnEvmCancel != null) _btnEvmCancel.text = "BACK";

            ShowEvmState(EvmPageState.Error);
        }

        private void SetEvmConnectBody(string text, bool isError)
        {
            if (_lblEvmConnectBody == null) return;
            _lblEvmConnectBody.enableRichText = false; // may embed wallet names / server text
            _lblEvmConnectBody.text = text;
            _lblEvmConnectBody.EnableInClassList("evm-connect-body--error", isError);
        }

        private void OnEvmRetryClicked()
        {
            if (_evmSelectedWallet == null) return;
            BlockmakerAuth.Instance?.CancelEvmConnect();
            BeginEvmWalletConnect(_evmSelectedWallet);
        }

        private void OnEvmCancelClicked()
        {
            CancelEvmConnectDelay();
            StopConnectTimeout();
            BlockmakerAuth.Instance?.CancelEvmConnect();
            _evmSelectedWallet = null;

            // The connecting pane is only reachable from a row on the unified list,
            // and the rows are kept while it shows — return straight to that list.
            ShowEvmState(EvmPageState.List);
        }

        /// The nav back arrow: context-sensitive — a plain back to the options page
        /// from the list pane, a cancel on the connecting/error pane.
        private void OnEvmBackClicked()
        {
            switch (_evmState)
            {
                case EvmPageState.Connecting:
                case EvmPageState.Error:
                    OnEvmCancelClicked();
                    break;
                default:
                    ShowOptionsPage();
                    break;
            }
        }

        private void OnEvmFindWalletClicked()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Application.OpenURL on WebGL is a same-tab LOCATION CHANGE — it would
            // replace the running game with ethereum.org. Open a new tab instead.
            BlockmakerWalletBridge.OpenUrlInNewTab(EvmFindWalletUrl);
#else
            Application.OpenURL(EvmFindWalletUrl);
#endif
        }

        // ── EVM dots (connecting pane, same grammar as the step-2 dots) ───────────

        private void StartEvmDots()
        {
            if (_evmStateConnecting == null || _evmDots == null || _evmDots.Length == 0) return;
            _evmDotIndex = 0;
            if (_evmDotAnim == null)
                _evmDotAnim = _evmStateConnecting.schedule.Execute(AdvanceEvmDot).Every(360);
            else
                _evmDotAnim.Resume();
        }

        private void StopEvmDots()
        {
            _evmDotAnim?.Pause();
            if (_evmDots == null) return;
            foreach (var dot in _evmDots)
                dot?.RemoveFromClassList("auth-step2-dot--on");
        }

        private void AdvanceEvmDot()
        {
            if (_evmDots == null || _evmDots.Length == 0) return;
            for (int i = 0; i < _evmDots.Length; i++)
                _evmDots[i]?.EnableInClassList("auth-step2-dot--on", i == _evmDotIndex);
            _evmDotIndex = (_evmDotIndex + 1) % _evmDots.Length;
        }

        private void CancelEvmConnectDelay()
        {
            if (_evmConnectDelayCoroutine != null) { StopCoroutine(_evmConnectDelayCoroutine); _evmConnectDelayCoroutine = null; }
        }

        private void ClearEvmWalletState()
        {
            CancelEvmConnectDelay();
            StopEvmDots();
            RemoveEvmSkeletonRow();
            _evmWallets.Clear();
            _evmSelectedWallet = null;
            _evmFlowSeq++;
            _evmWalletList?.Clear();
            if (_evmListFade != null) _evmListFade.style.display = DisplayStyle.None;
            if (_evmConnectIcon != null) _evmConnectIcon.style.backgroundImage = StyleKeyword.None;
            foreach (var tex in _evmIconTextures)
                if (tex != null) Destroy(tex);
            _evmIconTextures.Clear();
        }

        /// <summary>
        /// The pre-picker EVM connect (auto-pick via Reown QR) — the path for the
        /// curated native/editor rows (parameterized with the wallet display name so
        /// the QR modal labels itself accordingly) and for older UXML without the
        /// unified page.
        /// </summary>
        private void BeginEvmConnectFallback(string walletName = "MetaMask")
        {
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }
            if (_peraCtrl != null)
            {
                _peraCtrl.Open(walletName, evmLogoIcon);
                StartConnectTimeout(msg => { if (this != null) _peraCtrl.SetStatus(msg); });

                BlockmakerAuth.Instance.ConnectEvm(
                    onSuccess: _ =>
                    {
                        if (this == null) return;
                        StopConnectTimeout();
                        // Keep the modal open while it shows approval 2 of 2.
                        if (_peraCtrl.IsShowingStepTwo) return;
                        _peraCtrl.Close();
                    },
                    onError: err =>
                    {
                        if (this == null) return;
                        StopConnectTimeout();
                        _peraCtrl.Close();
                        ShowOptionsPage();
                        SetStatus(err, isError: true);
                    }
                );
                return;
            }

            if (ReownWalletConnector.Instance != null && ReownWalletConnector.Instance.IsInitialized)
                ShowQrPage(walletName);   // the picked wallet's name, not the internal "X-Chain"
            else
                SetStatus("Looking for your wallet app…");

            StartConnectTimeout(msg => { if (this != null) SetStatus(msg); });

            BlockmakerAuth.Instance.ConnectEvm(
                onSuccess: _ => { if (this == null) return; StopConnectTimeout(); },
                onError: err =>
                {
                    if (this == null) return;
                    StopConnectTimeout();
                    ShowOptionsPage();
                    SetStatus(err, isError: true);
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

        // Called via BlockmakerAuth.OnAuthStatus — today this fires exactly once, when
        // the wallet connect approval is done and the SECOND approval (the free login
        // signature) is about to arrive in the wallet app. See BlockmakerAuth.TriggerWalletLogin.
        private void HandleAuthStatus(string msg)
        {
            // Session restores also trigger wallet logins — only react while visible.
            if (_overlay == null || _overlay.style.display == DisplayStyle.None) return;

            // Only take over the screen when the user is actually in a wallet-connect
            // flow (a background reconnect must not hijack the email page).
            bool inWalletFlow =
                (_peraCtrl != null && _peraCtrl.IsOpen) ||
                (_pageQr         != null && _pageQr.style.display         == DisplayStyle.Flex) ||
                (_pageEvmWallets != null && _pageEvmWallets.style.display == DisplayStyle.Flex) ||
                (_pageStep2      != null && _pageStep2.style.display      == DisplayStyle.Flex);

            // A declined/failed sign-in signature arrives on this same channel. Surface
            // THAT message in the step-2 skin instead of resetting to the generic
            // "approve the request…" prompt (which made a decline look like nothing
            // happened). Everything else is a "waiting/approve" status.
            bool isFailure = IsDeclineStatus(msg);

            // Preferred: the unmissable "STEP 2 OF 2" state in whichever skin is showing.
            if (inWalletFlow && EnterStepTwoState(CurrentWalletProviderName(), isFailure ? msg : null)) return;

            // Fallback (older UXML without the step-2 panel): plain status routing.
            if (_peraCtrl != null && _peraCtrl.IsOpen) _peraCtrl.SetStatus(msg);
            else SetStatus(msg, isError: isFailure);
        }

        /// OnAuthStatus is a single message channel (no status type), so distinguish a
        /// declined/failed sign-in from a "waiting/approve" prompt by its wording. The
        /// auth layer's failure copy always names the decline/failure explicitly.
        private static bool IsDeclineStatus(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            return msg.IndexOf("declined", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("failed",   StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// Strip a trailing " Wallet" (e.g. "Trust Wallet" → "Trust") so provider names
        /// compose cleanly into "Scan with {name} Wallet" without doubling.
        private static string StripWalletSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            name = name.Trim();
            if (name.EndsWith(" Wallet", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - " Wallet".Length).TrimEnd();
            return name;
        }

        private void HandleQRReady(WalletQREventArgs e)
        {
            _pendingWcUri    = e.WalletConnectUri;
            _pendingProvider = e.Provider;

            // Curated EVM rows begin on the connecting pane. Once the WC URI is
            // ready, move to the dedicated QR page before painting the image.
            if (_evmSelectedWallet != null &&
                !string.IsNullOrEmpty(_evmSelectedWallet.rdns) &&
                _evmSelectedWallet.rdns.StartsWith("wc:", StringComparison.Ordinal))
                ShowQrPage(e.Provider);

            // Decode base64 PNG → Texture2D
            byte[] pngBytes;
            try { pngBytes = Convert.FromBase64String(e.QRCodeBase64Png); }
            catch { SetStatus("QR code failed to load. Go back and try again."); return; }
            if (_qrTexture != null) { Destroy(_qrTexture); _qrTexture = null; }
            _qrTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _qrTexture.LoadImage(pngBytes);

            if (_qrImage != null)
            {
                _qrImage.style.backgroundImage = new StyleBackground(_qrTexture);
                _qrImage.style.display         = DisplayStyle.Flex;
            }

            if (_qrLoadingWrap != null) _qrLoadingWrap.style.display = DisplayStyle.None;
            if (_btnCopyLink   != null) _btnCopyLink.style.display   = DisplayStyle.Flex;
            UpdateOpenWalletButton();
        }

        private void HandleNativeQRReady(string provider, string wcUri, Texture2D qrTexture)
        {
            _pendingWcUri    = wcUri;
            _pendingProvider = provider;

            if (_qrTexture != null) { Destroy(_qrTexture); _qrTexture = null; }
            _qrTexture = new Texture2D(qrTexture.width, qrTexture.height, qrTexture.format, false);
            Graphics.CopyTexture(qrTexture, _qrTexture);

            if (_qrImage != null)
            {
                _qrImage.style.backgroundImage = new StyleBackground(_qrTexture);
                _qrImage.style.display         = DisplayStyle.Flex;
            }

            if (_qrLoadingWrap != null) _qrLoadingWrap.style.display = DisplayStyle.None;
            if (_btnCopyLink   != null) _btnCopyLink.style.display   = DisplayStyle.Flex;
            UpdateOpenWalletButton();
        }

        /// <summary>
        /// Show the "Open in wallet app" button only on mobile (native or mobile
        /// browser) and only while a WalletConnect URI is pending. The QR code
        /// stays visible as a fallback.
        /// </summary>
        private void UpdateOpenWalletButton()
        {
            if (_btnOpenWallet == null) return;
            bool show = !string.IsNullOrEmpty(_pendingWcUri) && WalletDeepLink.IsMobilePlatform;
            _btnOpenWallet.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OpenWalletApp()
        {
            if (string.IsNullOrEmpty(_pendingWcUri)) return;
            // Synchronous within the button's click event — see WalletDeepLink's note on
            // iOS Safari requiring the WebGL navigation to happen inside a user gesture.
            WalletDeepLink.OpenWallet(_pendingProvider, _pendingWcUri);
            SetStatus("Opening your wallet app… approve the connection there, then return here.");
        }

        private void CopyWcLink()
        {
            if (string.IsNullOrEmpty(_pendingWcUri)) return;
            GUIUtility.systemCopyBuffer = _pendingWcUri;
            SetStatus("Link copied — paste it in your wallet app.");
        }

        // ── OTP handlers ───────────────────────────────────────────────────────────

        private static readonly Regex EmailRegex = new Regex(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private void OnSendCodeClicked()
        {
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }

            string email = _inputEmail?.value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(email) || !EmailRegex.IsMatch(email))
            {
                SetStatus("Please enter a valid email address.", isError: true);
                return;
            }

            var cfg = BlockmakerClient.Instance?.config;
    #if UNITY_WEBGL && !UNITY_EDITOR
            if (cfg != null && cfg.enableMagicEmail && !string.IsNullOrEmpty(cfg.magicPublishableKey))
            {
    #else
    #pragma warning disable CS0162
            if (false) // Magic SDK requires a browser — native builds use OTP
            {
    #endif
                SetLoading(true);
                SetStatus("Waiting for verification…");

                BlockmakerAuth.Instance.ConnectMagicEmail(
                    email,
                    onSuccess: _ => { SetLoading(false); /* HandleIdentityChanged → closes */ },
                    onError: err =>
                    {
                        SetLoading(false);
                        SetStatus(err, isError: true);
                    }
                );
                return;
            }
    #pragma warning restore CS0162

            SetLoading(true);
            SetStatus("Sending code…");

            BlockmakerAuth.Instance.RequestEmailOTP(
                email,
                onSent: () =>
                {
                    SetLoading(false);
                    _pendingEmail = email;
                    ShowStep2();
                    SetStatus("Code sent — check your inbox.");
                },
                onError: err =>
                {
                    SetLoading(false);
                    SetStatus(err, isError: true);
                }
            );
        }

        private static bool IsDigitsOnly(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private void OnVerifyClicked()
        {
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }
            if (string.IsNullOrEmpty(_pendingEmail)) { SetStatus("Something went wrong. Please go back and re-enter your email.", isError: true); return; }

            string otp = _inputOtp?.value?.Trim() ?? string.Empty;
            if (otp.Length != 6 || !IsDigitsOnly(otp))
            {
                SetStatus("Please enter the 6-digit code.", isError: true);
                return;
            }

            SetLoading(true);
            SetStatus("Verifying…");

            BlockmakerAuth.Instance.VerifyEmailOTP(
                _pendingEmail,
                otp,
                onSuccess: _ =>
                {
                    SetLoading(false);
                    SetStatus("Signed in!");
                    // HandleIdentityChanged fires → closes prompt
                },
                onError: err =>
                {
                    SetLoading(false);
                    SetStatus(err, isError: true);
                }
            );
        }

        private void OnResendClicked()
        {
            if (string.IsNullOrEmpty(_pendingEmail) || BlockmakerAuth.Instance == null) return;

            SetLoading(true);
            SetStatus("Sending code…");

            BlockmakerAuth.Instance.RequestEmailOTP(
                _pendingEmail,
                onSent: () =>
                {
                    SetLoading(false);
                    if (_inputOtp != null) _inputOtp.value = "";
                    StartResendCountdown();
                    SetStatus("New code sent — check your inbox.");
                },
                onError: err =>
                {
                    SetLoading(false);
                    SetStatus(err, isError: true);
                }
            );
        }

        // ── Event handlers ─────────────────────────────────────────────────────────

        private void HandleIdentityChanged(IBlockmakerIdentity identity)
        {
            if (identity == null || identity.Tier == IdentityTier.Guest) return;
            // Only react if the overlay is currently visible — ignore session restores on scene load
            if (_overlay == null || _overlay.style.display == DisplayStyle.None) return;

            // A wallet just connected but still owes the login signature (approval 2 of 2,
            // fired via TriggerWalletLogin right after this event). Keep the prompt open in
            // the "STEP 2 OF 2" state instead of closing — closing here is exactly how
            // players ended up missing the second request. OnAuthSucceeded still fires now,
            // at the same moment it always has.
            if (NeedsLoginSignature(identity) && EnterStepTwoState(FriendlyWalletNameFor(identity)))
            {
                if (!_authAnnounced)
                {
                    _authAnnounced = true;
                    OnAuthSucceeded?.Invoke();
                }
                return;
            }

            // Fully signed in (or a skin without the step-2 UI) — close as before.
            // Capture before Hide(): ResetState clears the flag.
            bool alreadyAnnounced = _authAnnounced;
            Hide();
            if (!alreadyAnnounced) OnAuthSucceeded?.Invoke();
        }

        private void HandleAuthError(string error)
        {
            // If the connect modal is up (including its step-2 state), drop it so the
            // error is visible on the options page.
            _peraCtrl?.Close();
            ShowOptionsPage();
            SetStatus(error, isError: true);
        }

        // ── Resend countdown ───────────────────────────────────────────────────────

        private void StartResendCountdown()
        {
            if (_resendCoroutine != null) StopCoroutine(_resendCoroutine);
            _resendCoroutine = StartCoroutine(ResendCountdown(60));
        }

        private IEnumerator ResendCountdown(int seconds)
        {
            if (_btnResend != null) _btnResend.SetEnabled(false);

            for (int i = seconds; i > 0; i--)
            {
                if (_btnResend != null) _btnResend.text = $"Resend ({i}s)";
                yield return new WaitForSecondsRealtime(1f);
            }

            if (_btnResend != null)
            {
                _btnResend.text = "Resend";
                _btnResend.SetEnabled(true);
            }

            _resendCoroutine = null;
        }

        // ── UI helpers ─────────────────────────────────────────────────────────────

        private void SetStatus(string msg, bool isError = false)
        {
            if (_lblStatus == null) return;
            _lblStatus.text = msg;
            _lblStatus.EnableInClassList("auth-status--error", isError);
            _lblStatus.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ClearStatus() => SetStatus(string.Empty);

        private void SetLoading(bool loading)
        {
            if (_btnSendCode != null) _btnSendCode.SetEnabled(!loading);
            if (_btnVerify   != null) _btnVerify.SetEnabled(!loading);
            if (_btnResend   != null && (_resendCoroutine == null || loading))
                _btnResend.SetEnabled(!loading);
        }

        private void ResetState()
        {
            _pendingEmail    = null;
            _pendingWcUri    = null;
            _pendingProvider = null;
            _authAnnounced   = false;
            StopStepTwoDots();
            ResetStepTwoResend();
            ClearEvmWalletState();
            if (_btnOpenWallet != null) _btnOpenWallet.style.display = DisplayStyle.None;
            StopResendCountdown();
            StopConnectTimeout();
            SetLoading(false);
            DismissWalletWarning();
            if (_inputEmail != null) _inputEmail.value = string.Empty;
            if (_inputOtp   != null) _inputOtp.value   = string.Empty;
            if (_qrTexture  != null) { Destroy(_qrTexture); _qrTexture = null; }
            if (_qrImage    != null) _qrImage.style.backgroundImage = StyleKeyword.None;
        }

        private void StopResendCountdown()
        {
            if (_resendCoroutine != null) { StopCoroutine(_resendCoroutine); _resendCoroutine = null; }
            if (_btnResend != null) { _btnResend.text = "Resend"; _btnResend.SetEnabled(true); }
        }

        private void HandleWalletAddressChanged(WalletAddressChangedEventArgs e)
        {
            string warning = $"Heads up: you switched to {e.NewProvider}, so items linked to your old {e.OldProvider} account won't show here. To see them again, sign back in with {e.OldProvider}.";

            if (_walletWarningBanner != null && _lblWalletWarning != null)
            {
                _lblWalletWarning.text = warning;
                _walletWarningBanner.style.display = DisplayStyle.Flex;
            }
            else
            {
                SetStatus(warning, isError: true);
            }
            BlockmakerLog.Warning($"[AuthPrompt] Wallet address changed: {e.OldProvider} ({e.OldAddress}) → {e.NewProvider} ({e.NewAddress})");
        }

        private void DismissWalletWarning()
        {
            if (_walletWarningBanner != null)
                _walletWarningBanner.style.display = DisplayStyle.None;
        }

    }

}
