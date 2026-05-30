using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{
    /// <summary>
    /// Simple connect wallet button + connected status overlay.
    /// Shows a "Connect Wallet" button. When clicked, opens the auth modal.
    /// After connection, shows a "Wallet Connected" confirmation with the address.
    ///
    /// For custom UI, remove this and call AuthPromptController.Instance.Show() directly.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BlockmakerConnectButton : MonoBehaviour
    {
        private VisualElement _root;
        private Button _btn;
        private VisualElement _connectedOverlay;
        private Label _connectedAddress;
        private Label _connectedProvider;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            _root.Clear();

            BuildConnectButton();
            BuildConnectedOverlay();

            BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;
            UpdateState();
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
        }

        private void BuildConnectButton()
        {
            _btn = new Button();
            _btn.text = "Connect Wallet";
            _btn.clicked += OnConnectClicked;

            _btn.style.position = Position.Absolute;
            _btn.style.top = 24;
            _btn.style.right = 24;
            _btn.style.paddingLeft = 24;
            _btn.style.paddingRight = 24;
            _btn.style.paddingTop = 12;
            _btn.style.paddingBottom = 12;
            _btn.style.fontSize = 14;
            _btn.style.color = Color.white;
            _btn.style.backgroundColor = new Color(0.063f, 0.725f, 0.506f); // #10b981
            _btn.style.borderTopWidth = 0;
            _btn.style.borderBottomWidth = 0;
            _btn.style.borderLeftWidth = 0;
            _btn.style.borderRightWidth = 0;
            _btn.style.borderTopLeftRadius = 10;
            _btn.style.borderTopRightRadius = 10;
            _btn.style.borderBottomLeftRadius = 10;
            _btn.style.borderBottomRightRadius = 10;
            _btn.style.unityFontStyleAndWeight = FontStyle.Bold;

            _root.Add(_btn);
        }

        private void BuildConnectedOverlay()
        {
            // Full-screen overlay (hidden by default)
            _connectedOverlay = new VisualElement();
            _connectedOverlay.style.position = Position.Absolute;
            _connectedOverlay.style.left = 0;
            _connectedOverlay.style.top = 0;
            _connectedOverlay.style.right = 0;
            _connectedOverlay.style.bottom = 0;
            _connectedOverlay.style.alignItems = Align.Center;
            _connectedOverlay.style.justifyContent = Justify.Center;
            _connectedOverlay.style.backgroundColor = new Color(0, 0, 0, 0.6f);
            _connectedOverlay.style.display = DisplayStyle.None;

            // Card
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.11f, 0.11f, 0.11f);
            card.style.borderTopLeftRadius = 16;
            card.style.borderTopRightRadius = 16;
            card.style.borderBottomLeftRadius = 16;
            card.style.borderBottomRightRadius = 16;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = new Color(1, 1, 1, 0.08f);
            card.style.borderBottomColor = new Color(0, 0, 0, 0.3f);
            card.style.borderLeftColor = new Color(1, 1, 1, 0.05f);
            card.style.borderRightColor = new Color(1, 1, 1, 0.05f);
            card.style.paddingLeft = 40;
            card.style.paddingRight = 40;
            card.style.paddingTop = 36;
            card.style.paddingBottom = 36;
            card.style.alignItems = Align.Center;
            card.style.width = 360;

            // Checkmark
            var check = new Label("✓");
            check.style.fontSize = 36;
            check.style.color = new Color(0.063f, 0.725f, 0.506f);
            check.style.marginBottom = 12;
            check.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(check);

            // Title
            var title = new Label("Wallet Connected");
            title.style.fontSize = 20;
            title.style.color = new Color(0.94f, 0.94f, 0.96f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(title);

            // Provider
            _connectedProvider = new Label();
            _connectedProvider.style.fontSize = 13;
            _connectedProvider.style.color = new Color(0.53f, 0.53f, 0.53f);
            _connectedProvider.style.marginBottom = 16;
            _connectedProvider.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(_connectedProvider);

            // Address
            _connectedAddress = new Label();
            _connectedAddress.style.fontSize = 12;
            _connectedAddress.style.color = new Color(0.063f, 0.725f, 0.506f);
            _connectedAddress.style.backgroundColor = new Color(0.063f, 0.725f, 0.506f, 0.08f);
            _connectedAddress.style.paddingLeft = 16;
            _connectedAddress.style.paddingRight = 16;
            _connectedAddress.style.paddingTop = 8;
            _connectedAddress.style.paddingBottom = 8;
            _connectedAddress.style.borderTopLeftRadius = 8;
            _connectedAddress.style.borderTopRightRadius = 8;
            _connectedAddress.style.borderBottomLeftRadius = 8;
            _connectedAddress.style.borderBottomRightRadius = 8;
            _connectedAddress.style.unityTextAlign = TextAnchor.MiddleCenter;
            _connectedAddress.style.marginBottom = 24;
            card.Add(_connectedAddress);

            // Disconnect button
            var disconnectBtn = new Button();
            disconnectBtn.text = "Disconnect";
            disconnectBtn.style.paddingLeft = 24;
            disconnectBtn.style.paddingRight = 24;
            disconnectBtn.style.paddingTop = 10;
            disconnectBtn.style.paddingBottom = 10;
            disconnectBtn.style.fontSize = 13;
            disconnectBtn.style.color = new Color(0.94f, 0.94f, 0.96f);
            disconnectBtn.style.backgroundColor = new Color(1, 1, 1, 0.06f);
            disconnectBtn.style.borderTopWidth = 1;
            disconnectBtn.style.borderBottomWidth = 1;
            disconnectBtn.style.borderLeftWidth = 1;
            disconnectBtn.style.borderRightWidth = 1;
            disconnectBtn.style.borderTopColor = new Color(1, 1, 1, 0.08f);
            disconnectBtn.style.borderBottomColor = new Color(1, 1, 1, 0.03f);
            disconnectBtn.style.borderLeftColor = new Color(1, 1, 1, 0.06f);
            disconnectBtn.style.borderRightColor = new Color(1, 1, 1, 0.06f);
            disconnectBtn.style.borderTopLeftRadius = 8;
            disconnectBtn.style.borderTopRightRadius = 8;
            disconnectBtn.style.borderBottomLeftRadius = 8;
            disconnectBtn.style.borderBottomRightRadius = 8;
            disconnectBtn.clicked += () =>
            {
                BlockmakerAuth.Instance?.Logout();
                _connectedOverlay.style.display = DisplayStyle.None;
            };
            card.Add(disconnectBtn);

            _connectedOverlay.Add(card);
            _root.Add(_connectedOverlay);
        }

        private void OnConnectClicked()
        {
            if (BlockmakerAuth.Instance != null && BlockmakerAuth.Instance.HasWallet)
            {
                _connectedOverlay.style.display = DisplayStyle.Flex;
                return;
            }

            if (AuthPromptController.Instance != null)
                AuthPromptController.Instance.Show();
            else
                BlockmakerLog.Warning("[Blockmaker] AuthPromptController not found in scene.");
        }

        private void HandleIdentityChanged(IBlockmakerIdentity identity)
        {
            UpdateState();

            if (identity != null && identity.HasWallet)
            {
                var addr = identity.Address;
                _connectedAddress.text = addr.Length > 16
                    ? $"{addr[..8]}...{addr[^8..]}"
                    : addr;
                _connectedProvider.text = $"Connected via {identity.ProviderName}";
                _connectedOverlay.style.display = DisplayStyle.Flex;
            }
        }

        private void UpdateState()
        {
            if (BlockmakerAuth.Instance == null) return;

            if (BlockmakerAuth.Instance.HasWallet)
            {
                var addr = BlockmakerAuth.Instance.Address;
                _btn.text = addr.Length > 12
                    ? $"{addr[..6]}...{addr[^6..]}"
                    : addr;
            }
            else
            {
                _btn.text = "Connect Wallet";
                _connectedOverlay.style.display = DisplayStyle.None;
            }
        }
    }
}
