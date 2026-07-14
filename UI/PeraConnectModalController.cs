using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{

    /// <summary>
    /// Generic wallet connection modal — works for Pera, Defly, and EVM wallets.
    /// Two panels:
    ///   panel-connect  — QR code for scanning with the wallet app
    ///   panel-download — QR code and store links for downloading the app
    ///
    /// Subscribes to ReownWalletConnector.OnQRReady while open.
    /// Provider-specific branding (logo, download URLs) is set per Open() call.
    ///
    /// Owned and driven by BlockmakerAuthUI / AuthPromptController.
    /// </summary>
    public class PeraConnectModalController
    {
        private readonly VisualElement _root;
        private readonly VisualElement _panelConnect;
        private readonly VisualElement _panelDownload;
        private readonly VisualElement _qrConnect;
        private readonly VisualElement _qrDownload;
        private readonly VisualElement _logoEl;
        private readonly VisualElement _logoEl2;
        private readonly Label         _lblStatus;
        private readonly Label         _lblFooterHint;
        private readonly Label         _lblDlTitle;
        private readonly Label         _lblDlStatus;
        private readonly VisualElement _connectDivider;
        private readonly VisualElement _connectFooter;
        private readonly Button        _btnShowDownload;
        private readonly Button        _btnOpenWallet;

        // Step 2 of 2 — wallet sign-in approval state
        private readonly VisualElement   _qrFrame;
        private readonly Label           _lblTitle;
        private readonly Label           _lblStepsHint;
        private readonly VisualElement   _panelStep2;
        private readonly Label           _lblStep2Body;
        private readonly VisualElement[] _step2Dots;
        private IVisualElementScheduledItem _step2DotAnim;
        private int  _step2DotIndex;
        private bool _isStepTwo;

        // Step 2 recovery controls (resend the sign request / cancel the login)
        private readonly Button _btnStep2Resend;
        private readonly Button _btnStep2Cancel;
        private readonly Label  _lblStep2ResendHint;
        private IVisualElementScheduledItem _resendCooldown;
        private bool _resendCoolingDown;

        private const string ResendLabel      = "RESEND REQUEST";
        private const string ResendSentLabel  = "SENT - CHECK YOUR WALLET";
        private const long   ResendCooldownMs = 5000;

        private Texture2D _connectQrTexture;
        private Texture2D _downloadQrTexture;
        private bool      _isOpen;

        /// Whether the modal is currently showing (used by the auth prompt to route
        /// progress messages to whichever surface the user is looking at).
        public bool IsOpen => _isOpen;

        /// Whether the modal is showing the "STEP 2 OF 2 — approve the sign-in request"
        /// state. The auth prompt keeps the modal open (instead of closing on connect
        /// success) while this is true, so players can't miss the second approval.
        public bool IsShowingStepTwo => _isOpen && _isStepTwo;

        private string _downloadUrl;
        private string _iosUrl;
        private string _androidUrl;
        private string _pendingProvider;
        private string _pendingWcUri;

        // The display name Open() was called with. The Reown QR event's provider
        // argument is only a transport-level hint (e.g. "EVM Wallet") — the status
        // line must keep showing this name instead.
        private string _openProviderName;

        public Action OnBackClicked { get; set; }
        public Action OnCloseClicked { get; set; }

        /// Fired after the player cancels the pending sign-in (step-2 CANCEL button).
        /// The wallet login is already aborted by then — the host should route back
        /// to its sign-in options and may show a neutral "cancelled" note.
        /// When unset, OnBackClicked is invoked as a fallback.
        public Action OnSignInCancelled { get; set; }

        public PeraConnectModalController(VisualElement root)
        {
            _root          = root;
            _panelConnect  = root.Q("panel-connect");
            _panelDownload = root.Q("panel-download");
            _qrConnect     = root.Q("qr-connect");
            _qrDownload    = root.Q("qr-download");
            _logoEl        = root.Q("img-pera-icon");
            _logoEl2       = root.Q("img-pera-icon-2");
            _lblStatus     = root.Q<Label>("lbl-connect-status");
            _lblFooterHint = root.Q<Label>("lbl-footer-hint");
            _lblDlTitle    = root.Q<Label>("lbl-dl-title");
            _lblDlStatus   = root.Q<Label>("lbl-dl-status");

            _connectDivider  = _panelConnect?.Q(className: "pera-divider");
            _connectFooter   = _panelConnect?.Q(className: "pera-footer");
            _qrFrame         = _panelConnect?.Q(className: "pera-qr-frame");
            _lblTitle        = _panelConnect?.Q<Label>(className: "pera-title");
            _lblStepsHint    = root.Q<Label>("lbl-steps-hint");
            _panelStep2      = root.Q("panel-step2");
            _lblStep2Body    = root.Q<Label>("lbl-step2-body");
            _step2Dots       = new[]
            {
                root.Q("pera-step2-dot-1"),
                root.Q("pera-step2-dot-2"),
                root.Q("pera-step2-dot-3"),
            };
            _btnShowDownload = root.Q<Button>("btn-show-download");
            _btnOpenWallet   = root.Q<Button>("btn-open-wallet");

            _btnStep2Resend     = root.Q<Button>("btn-step2-resend");
            _btnStep2Cancel     = root.Q<Button>("btn-step2-cancel");
            _lblStep2ResendHint = root.Q<Label>("lbl-step2-resend-hint");
            if (_btnStep2Resend != null) _btnStep2Resend.clicked += HandleResendSignIn;
            if (_btnStep2Cancel != null) _btnStep2Cancel.clicked += HandleCancelSignIn;

            var btnBack         = root.Q<Button>("btn-back");
            var btnDownloadBack = root.Q<Button>("btn-download-back");
            var btnShowDownload = _btnShowDownload;
            var btnIos          = root.Q<Button>("btn-ios");
            var btnAndroid      = root.Q<Button>("btn-android");

            var btnClose       = root.Q<Button>("btn-close");
            var btnCloseDl     = root.Q<Button>("btn-close-dl");

            if (btnBack != null)         btnBack.clicked         += HandleBack;
            if (btnDownloadBack != null)  btnDownloadBack.clicked += ShowConnectPanel;
            if (btnClose != null)        btnClose.clicked        += HandleClose;
            if (btnCloseDl != null)      btnCloseDl.clicked      += HandleClose;
            if (btnShowDownload != null)  btnShowDownload.clicked += ShowDownloadPanel;
            if (btnIos != null)          btnIos.clicked          += () => { if (!string.IsNullOrEmpty(_iosUrl)) Application.OpenURL(_iosUrl); };
            if (btnAndroid != null)      btnAndroid.clicked      += () => { if (!string.IsNullOrEmpty(_androidUrl)) Application.OpenURL(_androidUrl); };
            // Must run synchronously inside the click handler: on WebGL the deep link is a
            // browser navigation, and iOS Safari only allows it from a user gesture.
            if (_btnOpenWallet != null)  _btnOpenWallet.clicked  += HandleOpenWalletClicked;
        }

        public void Open(string provider, Sprite logo = null)
        {
            _isOpen = true;
            DestroyTexture(ref _downloadQrTexture);
            _root.style.display = DisplayStyle.Flex;
            ExitStepTwo();
            ShowConnectPanel();

            // Reset the deep-link state until a fresh WC URI arrives via HandleQRReady.
            _openProviderName = provider;
            _pendingProvider  = provider;
            _pendingWcUri     = null;
            if (_btnOpenWallet != null) _btnOpenWallet.style.display = DisplayStyle.None;

            switch (provider)
            {
                case "Defly":
                    _downloadUrl = "https://defly.app";
                    _iosUrl      = "https://apps.apple.com/app/defly/id1602631504";
                    _androidUrl  = "https://play.google.com/store/apps/details?id=io.blockshake.defly.app";
                    break;
                case "MetaMask":
                    _downloadUrl = "https://metamask.io";
                    _iosUrl      = "https://apps.apple.com/app/metamask/id1438144202";
                    _androidUrl  = "https://play.google.com/store/apps/details?id=io.metamask";
                    break;
                case "Rainbow":
                    _downloadUrl = "https://rainbow.me";
                    _iosUrl      = "https://apps.apple.com/app/rainbow-ethereum-wallet/id1457119021";
                    _androidUrl  = "https://play.google.com/store/apps/details?id=me.rainbow";
                    break;
                case "Coinbase Wallet":
                    _downloadUrl = "https://wallet.coinbase.com";
                    _iosUrl      = "https://apps.apple.com/app/coinbase-wallet-nfts-crypto/id1278383455";
                    _androidUrl  = "https://play.google.com/store/apps/details?id=org.toshi";
                    break;
                case "Trust Wallet":
                    _downloadUrl = "https://trustwallet.com";
                    _iosUrl      = "https://apps.apple.com/app/trust-crypto-bitcoin-wallet/id1288339409";
                    _androidUrl  = "https://play.google.com/store/apps/details?id=com.wallet.crypto.trustapp";
                    break;
                default:
                    _downloadUrl = "https://perawallet.app";
                    _iosUrl      = "https://apps.apple.com/app/pera-wallet/id1459712753";
                    _androidUrl  = "https://play.google.com/store/apps/details?id=com.algorand.android";
                    break;
            }

            if (logo != null)
            {
                var bg = new StyleBackground(Background.FromSprite(logo));
                if (_logoEl  != null) _logoEl.style.backgroundImage  = bg;
                if (_logoEl2 != null) _logoEl2.style.backgroundImage = bg;
            }
            else
            {
                // No logo for this provider (e.g. Defly/EVM icon left unset in the scene).
                // CLEAR the image rather than leave the PREVIOUS provider's logo up — a
                // blank chip is better than showing the wrong brand (Pera logo on Defly).
                if (_logoEl  != null) _logoEl.style.backgroundImage  = StyleKeyword.None;
                if (_logoEl2 != null) _logoEl2.style.backgroundImage = StyleKeyword.None;
            }

            // Curated EVM wallets get a footer reminder that any EVM wallet works;
            // every provider keeps the Download flow ("Get {name}" panel).
            bool curatedEvm = provider == "MetaMask" || provider == "Rainbow" ||
                              provider == "Coinbase Wallet" || provider == "Trust Wallet";

            if (_connectDivider  != null) _connectDivider.style.display  = DisplayStyle.Flex;
            if (_connectFooter   != null) _connectFooter.style.display   = DisplayStyle.Flex;
            if (_lblFooterHint   != null)
                _lblFooterHint.text = curatedEvm
                    ? $"Don’t have {provider}? Any EVM wallet works"
                    : $"Don’t have {provider}?";
            if (_btnShowDownload != null) _btnShowDownload.style.display = DisplayStyle.Flex;

            if (_lblDlTitle  != null) _lblDlTitle.text  = $"Get {provider}";
            if (_lblDlStatus != null) _lblDlStatus.text = $"Scan to visit {_downloadUrl.Replace("https://", "")}";

            SetStatus("Loading QR code…");

            ReownWalletConnector.OnQRReady -= HandleQRReady;
            ReownWalletConnector.OnQRReady += HandleQRReady;
            BlockmakerAuth.OnWalletQRReady -= HandleBridgeQRReady;
            BlockmakerAuth.OnWalletQRReady += HandleBridgeQRReady;
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;

            ReownWalletConnector.OnQRReady -= HandleQRReady;
            BlockmakerAuth.OnWalletQRReady -= HandleBridgeQRReady;
            _root.style.display = DisplayStyle.None;
            _pendingWcUri = null;
            if (_btnOpenWallet != null) _btnOpenWallet.style.display = DisplayStyle.None;
            ExitStepTwo();
            CleanupTextures();
        }

        public void SetStatus(string text)
        {
            if (_lblStatus != null) _lblStatus.text = text;
        }

        // ── Step 2 of 2 (sign-in approval) state ─────────────────────────────────

        /// <summary>
        /// Switch the connect panel into the bold "STEP 2 OF 2" state: the wallet is
        /// connected and a second, free login-signature approval is waiting in the
        /// wallet app. Hides the QR and shows a pulsing "waiting on you" panel.
        /// Back and close stay available. Exited by Open()/Close().
        /// Returns false when the panel can't be shown (closed, or older UXML) so the
        /// caller can fall back to its previous behavior.
        /// </summary>
        public bool ShowStepTwo(string provider, string failureMessage = null)
        {
            if (!_isOpen) return false;

            string appName   = string.IsNullOrEmpty(provider) ? "your wallet" : provider;
            bool   isFailure = !string.IsNullOrEmpty(failureMessage);

            if (_panelStep2 == null)
            {
                // Older UXML without the step-2 panel — fall back to a status line. A
                // declined/failed sign-in shows its own message instead of the generic
                // "approve" prompt (which made a decline look like nothing happened).
                SetStatus(isFailure
                    ? failureMessage
                    : $"Connected! Now approve the sign-in request in {appName}.");
                return false;
            }

            _isStepTwo = true;
            ShowConnectPanel();

            // Hide the connect-phase furniture; keep back (<) and close (x) buttons.
            if (_qrFrame        != null) _qrFrame.style.display        = DisplayStyle.None;
            if (_lblStatus      != null) _lblStatus.style.display      = DisplayStyle.None;
            if (_lblStepsHint   != null) _lblStepsHint.style.display   = DisplayStyle.None;
            if (_btnOpenWallet  != null) _btnOpenWallet.style.display  = DisplayStyle.None;
            if (_connectDivider != null) _connectDivider.style.display = DisplayStyle.None;
            if (_connectFooter  != null) _connectFooter.style.display  = DisplayStyle.None;

            if (_lblTitle != null) _lblTitle.text = isFailure ? "Sign-in didn’t go through" : "One more step!";
            if (_lblStep2Body != null)
            {
                // A declined/failed signature shows its own message here — otherwise the
                // generic "approve" prompt overwrites it and the player sees no sign
                // anything went wrong. "in {name}" reads right for a desktop extension too.
                _lblStep2Body.text = isFailure
                    ? failureMessage
                    : $"Approve the sign-in request in {appName} — it's a free signature, nothing leaves your wallet.";
                _lblStep2Body.EnableInClassList("pera-step2-body--error", isFailure);
            }

            RefreshStepTwoActions();   // keeps RESEND / CANCEL visible after a decline

            _panelStep2.RemoveFromClassList("pera-hidden");
            _panelStep2.style.display = DisplayStyle.Flex;
            if (isFailure) StopStepTwoDots();   // stop the "still waiting" pulse on failure
            else           StartStepTwoDots();
            return true;
        }

        /// Show/hide the resend controls based on whether the auth layer can
        /// actually re-send the sign request. Re-entrant safe: a retry fires
        /// OnAuthStatus → ShowStepTwo again, and this must not wipe the
        /// "SENT - CHECK YOUR WALLET" cooldown state mid-count.
        private void RefreshStepTwoActions()
        {
            bool canRetry = BlockmakerAuth.CanRetryWalletLogin;

            if (_btnStep2Resend != null)
            {
                _btnStep2Resend.style.display = canRetry ? DisplayStyle.Flex : DisplayStyle.None;
                if (!_resendCoolingDown)
                {
                    _btnStep2Resend.text = ResendLabel;
                    _btnStep2Resend.SetEnabled(canRetry);
                }
            }

            if (_lblStep2ResendHint != null)
                _lblStep2ResendHint.style.display = canRetry ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleResendSignIn()
        {
            var auth = BlockmakerAuth.Instance;
            if (auth == null || !BlockmakerAuth.CanRetryWalletLogin) return;

            auth.RetryWalletLogin();

            if (_btnStep2Resend == null) return;
            _resendCoolingDown   = true;
            _btnStep2Resend.text = ResendSentLabel;
            _btnStep2Resend.SetEnabled(false);

            if (_resendCooldown == null)
                _resendCooldown = _btnStep2Resend.schedule.Execute(RestoreResendButton);
            _resendCooldown.ExecuteLater(ResendCooldownMs);
        }

        private void RestoreResendButton()
        {
            _resendCoolingDown = false;
            if (_btnStep2Resend == null) return;
            _btnStep2Resend.text = ResendLabel;
            _btnStep2Resend.SetEnabled(BlockmakerAuth.CanRetryWalletLogin);
        }

        private void CancelResendCooldown()
        {
            _resendCooldown?.Pause();
            _resendCoolingDown = false;
            if (_btnStep2Resend != null)
            {
                _btnStep2Resend.text = ResendLabel;
                _btnStep2Resend.SetEnabled(true);
            }
        }

        private void HandleCancelSignIn()
        {
            BlockmakerAuth.Instance?.CancelWalletLogin();
            Close();
            if (OnSignInCancelled != null) OnSignInCancelled.Invoke();
            else                           OnBackClicked?.Invoke();
        }

        /// Restore the normal connect-panel visuals. Divider/footer/open-wallet
        /// visibility is re-derived by the next Open()/HandleQRReady.
        private void ExitStepTwo()
        {
            _isStepTwo = false;
            StopStepTwoDots();
            CancelResendCooldown();
            if (_panelStep2 != null)
            {
                _panelStep2.AddToClassList("pera-hidden");
                _panelStep2.style.display = DisplayStyle.None;
            }
            if (_lblStep2Body != null) _lblStep2Body.RemoveFromClassList("pera-step2-body--error");
            if (_qrFrame      != null) _qrFrame.style.display      = DisplayStyle.Flex;
            if (_lblStatus    != null) _lblStatus.style.display    = DisplayStyle.Flex;
            if (_lblStepsHint != null) _lblStepsHint.style.display = DisplayStyle.Flex;
            if (_lblTitle     != null) _lblTitle.text = "Connect Wallet";
        }

        private void StartStepTwoDots()
        {
            if (_panelStep2 == null || _step2Dots == null || _step2Dots.Length == 0) return;
            _step2DotIndex = 0;
            if (_step2DotAnim == null)
                _step2DotAnim = _panelStep2.schedule.Execute(AdvanceStepTwoDot).Every(360);
            else
                _step2DotAnim.Resume();
        }

        private void StopStepTwoDots()
        {
            _step2DotAnim?.Pause();
            if (_step2Dots == null) return;
            foreach (var dot in _step2Dots)
                dot?.RemoveFromClassList("pera-step2-dot--on");
        }

        private void AdvanceStepTwoDot()
        {
            if (_step2Dots == null || _step2Dots.Length == 0) return;
            for (int i = 0; i < _step2Dots.Length; i++)
                _step2Dots[i]?.EnableInClassList("pera-step2-dot--on", i == _step2DotIndex);
            _step2DotIndex = (_step2DotIndex + 1) % _step2Dots.Length;
        }

        private void HandleBack()
        {
            // Leaving mid step-2 must abort the pending login signature — otherwise
            // the session lingers half-authenticated behind a dismissed modal.
            if (IsShowingStepTwo)
                BlockmakerAuth.Instance?.CancelWalletLogin();
            Close();
            OnBackClicked?.Invoke();
        }

        private void HandleClose()
        {
            // Same as back: x during step-2 also cancels the pending wallet login.
            if (IsShowingStepTwo)
                BlockmakerAuth.Instance?.CancelWalletLogin();
            Close();
            OnCloseClicked?.Invoke();
        }

        /// The in-house jslib bridge path fires BlockmakerAuth.OnWalletQRReady (with a
        /// base64 QR we don't need — the modal renders its own from the URI). Adapt it to
        /// the same handler the Reown path uses so the QR shows on either transport.
        private void HandleBridgeQRReady(WalletQREventArgs e) =>
            HandleQRReady(e.Provider, e.WalletConnectUri, null);

        private void HandleQRReady(string provider, string uri, Texture2D _)
        {
            _pendingProvider = provider;
            _pendingWcUri    = uri;

            // Already waiting on the sign-in approval — don't let a late QR event
            // pull the panel back to the scan state.
            if (_isStepTwo) return;

            DestroyTexture(ref _connectQrTexture);
            _connectQrTexture = QRTextureGenerator.Generate(uri, 512);

            if (_qrConnect != null && _connectQrTexture != null)
                _qrConnect.style.backgroundImage = new StyleBackground(_connectQrTexture);

            // Status shows the display name the modal was opened with — the event's
            // provider argument is a transport hint ("EVM Wallet") and only fills in
            // when Open() got an empty name.
            string displayName = string.IsNullOrEmpty(_openProviderName) ? provider : _openProviderName;
            SetStatus($"Scan with {displayName}");

            // On mobile (native or mobile browser) offer a one-tap deep link into the
            // wallet app; the QR code stays visible as a fallback.
            if (_btnOpenWallet != null)
                _btnOpenWallet.style.display =
                    WalletDeepLink.IsMobilePlatform ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleOpenWalletClicked()
        {
            if (string.IsNullOrEmpty(_pendingWcUri)) return;
            // Synchronous within the button's clicked event — see WalletDeepLink's note on
            // iOS Safari requiring the WebGL navigation to happen inside a user gesture.
            WalletDeepLink.OpenWallet(_pendingProvider, _pendingWcUri);
            SetStatus("Opening your wallet app… approve the connection there, then return here.");
        }

        private void ShowConnectPanel()
        {
            if (_panelConnect != null)
            {
                _panelConnect.RemoveFromClassList("pera-hidden");
                _panelConnect.style.display = DisplayStyle.Flex;
            }
            if (_panelDownload != null)
            {
                _panelDownload.AddToClassList("pera-hidden");
                _panelDownload.style.display = DisplayStyle.None;
            }
        }

        private void ShowDownloadPanel()
        {
            if (_panelConnect != null)
            {
                _panelConnect.AddToClassList("pera-hidden");
                _panelConnect.style.display = DisplayStyle.None;
            }
            if (_panelDownload != null)
            {
                _panelDownload.RemoveFromClassList("pera-hidden");
                _panelDownload.style.display = DisplayStyle.Flex;
            }

            if (_downloadQrTexture == null && !string.IsNullOrEmpty(_downloadUrl))
            {
                _downloadQrTexture = QRTextureGenerator.Generate(_downloadUrl, 512);
                if (_qrDownload != null)
                    _qrDownload.style.backgroundImage = new StyleBackground(_downloadQrTexture);
            }
        }

        private static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null)
            {
                UnityEngine.Object.Destroy(tex);
                tex = null;
            }
        }

        private void CleanupTextures()
        {
            DestroyTexture(ref _connectQrTexture);
            DestroyTexture(ref _downloadQrTexture);

            if (_qrConnect != null)  _qrConnect.style.backgroundImage  = StyleKeyword.None;
            if (_qrDownload != null) _qrDownload.style.backgroundImage = StyleKeyword.None;
        }
    }

}