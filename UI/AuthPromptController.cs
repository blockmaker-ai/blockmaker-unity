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
    /// The prompt manages four pages:
    ///   page-options       — Email / Algorand / xChain buttons
    ///   page-algo-wallets  — Pera / Defly wallet picker
    ///   page-otp           — step1 (email entry) → step2 (6-digit code)
    ///   page-qr            — WalletConnect QR code display
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
        private VisualElement _pageAlgoWallets;
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

        private Label _lblStatus;

        // Wallet warning
        private VisualElement _walletWarningBanner;
        private Label         _lblWalletWarning;

        private Button    _btnMetamask;

        private string    _pendingEmail;
        private string    _pendingWcUri;
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
            _pageAlgoWallets = root.Q("page-algo-wallets");
            _pageOtp         = root.Q("page-otp");
            _pageQr          = root.Q("page-qr");

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

            _lblStatus = root.Q<Label>("lbl-auth-status");

            _walletWarningBanner = root.Q("wallet-warning-banner");
            _lblWalletWarning    = root.Q<Label>("lbl-wallet-warning");

            // Placeholder text
            if (_inputEmail != null) _inputEmail.textEdition.placeholder = "your@email.com";
            if (_inputOtp   != null) _inputOtp.textEdition.placeholder   = "6-digit code";

            // Button wiring
            root.Q<Button>("btn-close")?.RegisterCallback<ClickEvent>(_        => Hide());
            root.Q<Button>("btn-email")?.RegisterCallback<ClickEvent>(_        => ShowOtpPage());
            root.Q<Button>("btn-algorand")?.RegisterCallback<ClickEvent>(_       => SetPage(_pageAlgoWallets));
            root.Q<Button>("btn-back-from-wallets")?.RegisterCallback<ClickEvent>(_ => ShowOptionsPage());
            root.Q<Button>("btn-pera")?.RegisterCallback<ClickEvent>(_         => BeginWalletConnect("Pera"));
            root.Q<Button>("btn-defly")?.RegisterCallback<ClickEvent>(_        => BeginWalletConnect("Defly"));
            var btnMm = root.Q<Button>("btn-metamask");
            if (btnMm != null) btnMm.RegisterCallback<ClickEvent>(_ => BeginEvmConnect());
            root.Q<Button>("btn-back-options")?.RegisterCallback<ClickEvent>(_ => ShowOptionsPage());

            _btnMetamask = btnMm;
            if (_btnMetamask != null) _btnMetamask.style.display = DisplayStyle.None;
            root.Q<Button>("btn-cancel-connect")?.RegisterCallback<ClickEvent>(_ => ShowOptionsPage());
            _btnCopyLink?.RegisterCallback<ClickEvent>(_ => CopyWcLink());
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
                _peraRoot.style.display = DisplayStyle.None;
            }

            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        }

        private void OnEnable()
        {
            BlockmakerAuth.OnIdentityChanged      += HandleIdentityChanged;
            BlockmakerAuth.OnAuthError             += HandleAuthError;
            BlockmakerAuth.OnWalletQRReady         += HandleQRReady;
            BlockmakerAuth.OnWalletAddressChanged  += HandleWalletAddressChanged;
            ReownWalletConnector.OnQRReady         += HandleNativeQRReady;
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged      -= HandleIdentityChanged;
            BlockmakerAuth.OnAuthError             -= HandleAuthError;
            BlockmakerAuth.OnWalletQRReady         -= HandleQRReady;
            BlockmakerAuth.OnWalletAddressChanged  -= HandleWalletAddressChanged;
            ReownWalletConnector.OnQRReady         -= HandleNativeQRReady;

            StopResendCountdown();
            StopConnectTimeout();
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

            var cfg = BlockmakerClient.Instance?.config;

            if (_btnMetamask != null)
            {
                bool showMm = cfg == null || cfg.enableEvmXChain;
                _btnMetamask.style.display = showMm ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_overlay != null)
                _overlay.style.display = DisplayStyle.Flex;
            else
                BlockmakerLog.Error("[AuthPromptController] auth-overlay not found in AuthPrompt.uxml");
        }

        public void Hide()
        {
            _peraCtrl?.Close();
            StopConnectTimeout();
            BlockmakerAuth.Instance?.CancelWalletConnect();
            BlockmakerAuth.Instance?.CancelEvmConnect();
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            ResetState();
        }

        // ── Page switching ─────────────────────────────────────────────────────────

        private void ShowOptionsPage()
        {
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

            // Update provider label
            if (_lblQrProvider != null)
                _lblQrProvider.text = $"Scan with {provider} Wallet";

            // Show loading state; hide QR image until received
            if (_qrLoadingWrap != null) _qrLoadingWrap.style.display = DisplayStyle.Flex;
            if (_qrImage        != null) _qrImage.style.display       = DisplayStyle.None;
            if (_btnCopyLink    != null) _btnCopyLink.style.display    = DisplayStyle.None;
        }

        private void SetPage(VisualElement activePage)
        {
            if (_pageOptions     != null) _pageOptions.style.display     = DisplayStyle.None;
            if (_pageAlgoWallets != null) _pageAlgoWallets.style.display = DisplayStyle.None;
            if (_pageOtp         != null) _pageOtp.style.display         = DisplayStyle.None;
            if (_pageQr          != null) _pageQr.style.display          = DisplayStyle.None;

            if (activePage != null) activePage.style.display = DisplayStyle.Flex;
        }

        // ── Wallet connect ─────────────────────────────────────────────────────────

        private const float WalletConnectTimeoutSeconds = 45f;

        private void BeginWalletConnect(string provider)
        {
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

        private void BeginEvmConnect()
        {
            if (BlockmakerAuth.Instance == null) { SetStatus("Unable to connect right now. Please restart the game.", isError: true); return; }
            if (_peraCtrl != null)
            {
                _peraCtrl.Open("MetaMask", evmLogoIcon);
                StartConnectTimeout(msg => { if (this != null) _peraCtrl.SetStatus(msg); });

                BlockmakerAuth.Instance.ConnectEvm(
                    onSuccess: _ =>
                    {
                        if (this == null) return;
                        StopConnectTimeout();
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
                ShowQrPage("Ethereum");
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

        // Called via BlockmakerAuth.OnWalletQRReady
        private void HandleQRReady(WalletQREventArgs e)
        {
            _pendingWcUri = e.WalletConnectUri;

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
        }

        private void HandleNativeQRReady(string provider, string wcUri, Texture2D qrTexture)
        {
            _pendingWcUri = wcUri;

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
            Hide();
            OnAuthSucceeded?.Invoke();
        }

        private void HandleAuthError(string error)
        {
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
            _pendingEmail = null;
            _pendingWcUri = null;
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