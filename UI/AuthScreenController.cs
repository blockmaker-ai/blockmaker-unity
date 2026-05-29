using System;
using UnityEngine.UIElements;

namespace Blockmaker
{

    /// <summary>
    /// Manages the AuthScreen UXML state machine.
    /// Panels:
    ///   panel-main    → login options (wallet buttons, email button, guest)
    ///   panel-address → connected state (address, continue, disconnect)
    ///
    /// Owned and driven by BlockmakerAuthUI.
    /// </summary>
    public class AuthScreenController
    {
        // ── Elements ───────────────────────────────────────────────────────────

        private readonly VisualElement _panelMain;
        private readonly VisualElement _panelAddress;
        private bool _showingMain = true;

        // panel-main
        private readonly Button _btnPera;
        private readonly Button _btnDefly;
        private readonly Button _btnMetamask;
        private readonly Button _btnEmail;
        private readonly Button _btnGuest;
        private readonly Label  _lblStatus;

        // panel-address
        private readonly Label  _lblAddress;
        private readonly Label  _lblProviderName;
        private readonly Label  _lblStatus2;
        private readonly Button _btnContinue;
        private readonly Button _btnDisconnect;

        // ── Callbacks ──────────────────────────────────────────────────────────

        public Action OnPeraClicked       { get; set; }
        public Action OnDeflyClicked      { get; set; }
        public Action OnMetamaskClicked   { get; set; }
        public Action OnEmailClicked      { get; set; }
        public Action OnGuestClicked      { get; set; }
        public Action OnContinueClicked   { get; set; }
        public Action OnDisconnectClicked { get; set; }

        // ── Constructor ────────────────────────────────────────────────────────

        public AuthScreenController(VisualElement root)
        {
            _panelMain    = root.Q<VisualElement>("panel-main");
            _panelAddress = root.Q<VisualElement>("panel-address");

            _btnPera          = root.Q<Button>("btn-pera");
            _btnDefly         = root.Q<Button>("btn-defly");
            _btnMetamask      = root.Q<Button>("btn-metamask");
            _btnEmail         = root.Q<Button>("btn-email");
            _btnGuest         = root.Q<Button>("btn-guest");
            _lblStatus        = root.Q<Label>("lbl-status");

            _lblAddress       = root.Q<Label>("lbl-address");
            _lblProviderName  = root.Q<Label>("lbl-provider-name");
            _lblStatus2       = root.Q<Label>("lbl-status-2");
            _btnContinue      = root.Q<Button>("btn-continue");
            _btnDisconnect    = root.Q<Button>("btn-disconnect");

            if (_btnPera != null)       _btnPera.clicked      += () => OnPeraClicked?.Invoke();
            if (_btnDefly != null)      _btnDefly.clicked     += () => OnDeflyClicked?.Invoke();
            if (_btnMetamask != null)   _btnMetamask.clicked  += () => OnMetamaskClicked?.Invoke();
            if (_btnEmail != null)      _btnEmail.clicked     += () => OnEmailClicked?.Invoke();
            if (_btnGuest != null)      _btnGuest.clicked     += () => OnGuestClicked?.Invoke();
            if (_btnContinue != null)   _btnContinue.clicked  += () => OnContinueClicked?.Invoke();
            if (_btnDisconnect != null) _btnDisconnect.clicked += () => OnDisconnectClicked?.Invoke();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Show login options panel. Clears any previous status.</summary>
        public void ShowMain(string statusText = "")
        {
            if (_lblStatus != null) _lblStatus.text = statusText;
            SetPanel(showMain: true);
        }

        /// <summary>Show connected panel with provider name and address.</summary>
        public void ShowAddress(string providerName, string address)
        {
            if (_lblProviderName != null) _lblProviderName.text = providerName;
            if (_lblAddress != null)      _lblAddress.text      = BlockmakerUIUtils.ShortenAddress(address);
            if (_lblStatus2 != null)      _lblStatus2.text      = "";
            SetPanel(showMain: false);
        }

        /// <summary>Display a status message on whichever panel is currently visible.</summary>
        public void SetStatus(string message, bool isError = true, bool isWarning = false)
        {
            Label visible = _showingMain ? _lblStatus : _lblStatus2;
            if (visible == null) return;

            visible.text  = message;
            if (isWarning)
                visible.style.color = new StyleColor(new UnityEngine.Color(0.984f, 0.749f, 0.141f)); // --bm-warning #FBBF24
            else if (isError)
                visible.style.color = new StyleColor(new UnityEngine.Color(0.973f, 0.443f, 0.443f)); // --bm-error #F87171
            else
                visible.style.color = new StyleColor(new UnityEngine.Color(0.737f, 0.737f, 0.765f)); // neutral/info
        }

        /// <summary>Disable interactive buttons while a request is in flight.</summary>
        public void SetLoading(bool loading)
        {
            _btnPera?.SetEnabled(!loading);
            _btnDefly?.SetEnabled(!loading);
            _btnMetamask?.SetEnabled(!loading);
            _btnEmail?.SetEnabled(!loading);
            _btnGuest?.SetEnabled(!loading);
            _btnContinue?.SetEnabled(!loading);
            _btnDisconnect?.SetEnabled(!loading);
        }

        /// <summary>Show or hide the xChain EVM button based on config flag.</summary>
        public void SetMetamaskVisible(bool visible)
        {
            if (_btnMetamask != null)
                _btnMetamask.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Private ────────────────────────────────────────────────────────────

        private void SetPanel(bool showMain)
        {
            _showingMain = showMain;
            _panelMain?.RemoveFromClassList("auth-panel-hidden");
            _panelAddress?.RemoveFromClassList("auth-panel-hidden");

            if (showMain)
            {
                if (_panelAddress != null)
                {
                    _panelAddress.AddToClassList("auth-panel-hidden");
                    _panelAddress.style.display = DisplayStyle.None;
                }
                if (_panelMain != null)
                    _panelMain.style.display = DisplayStyle.Flex;
            }
            else
            {
                if (_panelMain != null)
                {
                    _panelMain.AddToClassList("auth-panel-hidden");
                    _panelMain.style.display = DisplayStyle.None;
                }
                if (_panelAddress != null)
                    _panelAddress.style.display = DisplayStyle.Flex;
            }
        }

    }

}