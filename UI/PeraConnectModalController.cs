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

        private Texture2D _connectQrTexture;
        private Texture2D _downloadQrTexture;
        private bool      _isOpen;

        /// Whether the modal is currently showing (used by the auth prompt to route
        /// progress messages to whichever surface the user is looking at).
        public bool IsOpen => _isOpen;

        private string _downloadUrl;
        private string _iosUrl;
        private string _androidUrl;
        private string _pendingProvider;
        private string _pendingWcUri;

        public Action OnBackClicked { get; set; }
        public Action OnCloseClicked { get; set; }

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
            _btnShowDownload = root.Q<Button>("btn-show-download");
            _btnOpenWallet   = root.Q<Button>("btn-open-wallet");

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
            ShowConnectPanel();

            // Reset the deep-link state until a fresh WC URI arrives via HandleQRReady.
            _pendingProvider = provider;
            _pendingWcUri    = null;
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

            bool showDownload = provider != "MetaMask";

            if (_connectDivider != null)
                _connectDivider.style.display = showDownload ? DisplayStyle.Flex : DisplayStyle.None;

            if (provider == "MetaMask")
            {
                if (_connectFooter != null) _connectFooter.style.display = DisplayStyle.Flex;
                if (_lblFooterHint != null) _lblFooterHint.text = "Any EVM wallet — MetaMask, Rainbow + more";
                if (_btnShowDownload != null) _btnShowDownload.style.display = DisplayStyle.None;
            }
            else
            {
                if (_connectFooter != null) _connectFooter.style.display = DisplayStyle.Flex;
                if (_lblFooterHint != null) _lblFooterHint.text = $"Don’t have {provider}?";
                if (_btnShowDownload != null) _btnShowDownload.style.display = DisplayStyle.Flex;
            }

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
            CleanupTextures();
        }

        public void SetStatus(string text)
        {
            if (_lblStatus != null) _lblStatus.text = text;
        }

        private void HandleBack()
        {
            Close();
            OnBackClicked?.Invoke();
        }

        private void HandleClose()
        {
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

            DestroyTexture(ref _connectQrTexture);
            _connectQrTexture = QRTextureGenerator.Generate(uri, 512);

            if (_qrConnect != null && _connectQrTexture != null)
                _qrConnect.style.backgroundImage = new StyleBackground(_connectQrTexture);

            SetStatus($"Scan with {provider}");

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