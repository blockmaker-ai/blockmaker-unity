using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{
    /// <summary>
    /// Circular profile button — shows avatar initial or thumbnail.
    /// When clicked, opens the BlockmakerProfileController.
    /// When guest, opens the auth prompt instead.
    ///
    /// Add to a GameObject with a UIDocument component.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BlockmakerProfileButton : MonoBehaviour
    {
        private VisualElement _root;
        private Button _btn;
        private Label _initial;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            _root.Clear();

            _btn = new Button();
            _btn.clicked += OnClicked;

            _btn.style.position = Position.Absolute;
            _btn.style.top = 24;
            _btn.style.right = 180;
            _btn.style.width = 44;
            _btn.style.height = 44;
            _btn.style.borderTopLeftRadius = 22;
            _btn.style.borderTopRightRadius = 22;
            _btn.style.borderBottomLeftRadius = 22;
            _btn.style.borderBottomRightRadius = 22;
            _btn.style.borderTopWidth = 2;
            _btn.style.borderBottomWidth = 2;
            _btn.style.borderLeftWidth = 2;
            _btn.style.borderRightWidth = 2;
            _btn.style.borderTopColor = new Color(0.655f, 0.545f, 0.98f, 0.25f);
            _btn.style.borderBottomColor = new Color(0.655f, 0.545f, 0.98f, 0.25f);
            _btn.style.borderLeftColor = new Color(0.655f, 0.545f, 0.98f, 0.25f);
            _btn.style.borderRightColor = new Color(0.655f, 0.545f, 0.98f, 0.25f);
            _btn.style.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
            _btn.style.paddingLeft = 0;
            _btn.style.paddingRight = 0;
            _btn.style.paddingTop = 0;
            _btn.style.paddingBottom = 0;
            _btn.style.alignItems = Align.Center;
            _btn.style.justifyContent = Justify.Center;
            _btn.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;

            _initial = new Label("?");
            _initial.style.fontSize = 18;
            _initial.style.color = new Color(0.655f, 0.545f, 0.98f);
            _initial.style.unityFontStyleAndWeight = FontStyle.Bold;
            _initial.style.unityTextAlign = TextAnchor.MiddleCenter;
            _btn.Add(_initial);

            _root.Add(_btn);

            BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;
            BlockmakerProfileManager.OnProfileLoaded += HandleProfileChanged;
            BlockmakerProfileManager.OnProfileUpdated += HandleProfileChanged;

            UpdateState();
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
            BlockmakerProfileManager.OnProfileLoaded -= HandleProfileChanged;
            BlockmakerProfileManager.OnProfileUpdated -= HandleProfileChanged;
        }

        private void OnClicked()
        {
            if (BlockmakerAuth.Instance == null || !BlockmakerAuth.Instance.HasWallet)
            {
                if (AuthPromptController.Instance != null)
                    AuthPromptController.Instance.Show();
                return;
            }

            if (BlockmakerProfileController.Instance != null)
                BlockmakerProfileController.Instance.Show();
        }

        private void HandleIdentityChanged(IBlockmakerIdentity _) => UpdateState();
        private void HandleProfileChanged(BlockmakerProfile _) => UpdateState();

        private void UpdateState()
        {
            if (_btn == null) return;

            string name = BlockmakerProfileManager.DisplayName ?? "Guest";
            bool hasWallet = BlockmakerAuth.Instance?.HasWallet ?? false;

            if (_initial != null)
                _initial.text = hasWallet && name.Length > 0 ? name[0].ToString().ToUpper() : "?";

            if (!string.IsNullOrEmpty(BlockmakerProfileManager.ProfileImageUrl))
            {
                BlockmakerProfileManager.Instance?.LoadProfileTexture(tex =>
                {
                    if (tex == null || _btn == null) return;
                    _btn.style.backgroundImage = new StyleBackground(tex);
                    if (_initial != null) _initial.style.display = DisplayStyle.None;
                });
            }
            else
            {
                _btn.style.backgroundImage = StyleKeyword.None;
                if (_initial != null) _initial.style.display = DisplayStyle.Flex;
            }
        }
    }
}
