using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{

    [RequireComponent(typeof(UIDocument))]
    public class BlockmakerAccountController : MonoBehaviour
    {
        public static BlockmakerAccountController Instance { get; private set; }
        public static event Action OnSetUsernameClicked;
        public static event Action OnChangeUsernameClicked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            OnSetUsernameClicked = null;
            OnChangeUsernameClicked = null;
        }

        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _backdrop;
        private VisualElement _avatar;
        private Label         _initial;
        private Label         _displayName;
        private Label         _tier;
        private Label         _address;
        private Label         _copyConfirm;
        private Label         _username;
        private VisualElement _usernameSection;
        private VisualElement _usernameRow;
        private Button        _btnSetUsername;
        private Button        _btnSignOut;

        private float _copyConfirmTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            var doc = GetComponent<UIDocument>();
            if (doc.visualTreeAsset == null) return;

            _root = doc.rootVisualElement;

            _panel           = _root.Q<VisualElement>("bm-account-panel");
            _backdrop        = _root.Q<VisualElement>("bm-account-backdrop");
            _avatar          = _root.Q<VisualElement>("bm-account-avatar");
            _initial         = _root.Q<Label>("bm-account-initial");
            _displayName     = _root.Q<Label>("bm-account-name");
            _tier            = _root.Q<Label>("bm-account-tier");
            _address         = _root.Q<Label>("bm-account-address");
            _copyConfirm     = _root.Q<Label>("bm-account-copy-confirm");
            _username        = _root.Q<Label>("bm-account-username");
            _usernameSection = _root.Q<VisualElement>("bm-account-username-section");
            _usernameRow     = _username?.parent;
            _btnSetUsername   = _root.Q<Button>("btn-account-set-username");
            _btnSignOut      = _root.Q<Button>("btn-account-signout");

            _root.Q<Button>("btn-account-close")?.RegisterCallback<ClickEvent>(_ => Hide());
            _backdrop?.RegisterCallback<ClickEvent>(_ => Hide());
            _root.Q<Button>("btn-account-copy")?.RegisterCallback<ClickEvent>(_ => CopyAddress());
            _btnSetUsername?.RegisterCallback<ClickEvent>(_ => OnSetUsernameClicked?.Invoke());
            _root.Q<Button>("btn-account-change-username")?.RegisterCallback<ClickEvent>(_ => OnChangeUsernameClicked?.Invoke());
            _btnSignOut?.RegisterCallback<ClickEvent>(_ =>
            {
                BlockmakerAuth.Instance?.Logout();
                Hide();
            });

            Hide();
        }

        private void OnEnable()
        {
            BlockmakerAuth.OnIdentityChanged          += HandleIdentityChanged;
            BlockmakerProfileManager.OnProfileLoaded   += HandleProfileChanged;
            BlockmakerProfileManager.OnProfileUpdated  += HandleProfileChanged;
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged          -= HandleIdentityChanged;
            BlockmakerProfileManager.OnProfileLoaded   -= HandleProfileChanged;
            BlockmakerProfileManager.OnProfileUpdated  -= HandleProfileChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (_copyConfirmTimer > 0)
            {
                _copyConfirmTimer -= Time.unscaledDeltaTime;
                if (_copyConfirmTimer <= 0 && _copyConfirm != null)
                    _copyConfirm.style.display = DisplayStyle.None;
            }
        }

        private void HandleIdentityChanged(IBlockmakerIdentity _) => Refresh();
        private void HandleProfileChanged(BlockmakerProfile _)    => Refresh();

        public void Show()
        {
            if (_root == null) return;
            Refresh();
            var overlay = _root.Q<VisualElement>("bm-account-root");
            if (overlay != null) overlay.style.display = DisplayStyle.Flex;
            _panel?.RemoveFromClassList("bm-account-panel--hidden");
        }

        public void Hide()
        {
            if (_root == null) return;
            var overlay = _root.Q<VisualElement>("bm-account-root");
            if (overlay != null) overlay.style.display = DisplayStyle.None;
            _panel?.AddToClassList("bm-account-panel--hidden");
        }

        private void Refresh()
        {
            if (_displayName == null) return;

            var identity = BlockmakerAuth.Instance?.Identity;
            var profile  = BlockmakerProfileManager.Profile;
            bool isGuest = identity == null || identity.Tier == IdentityTier.Guest;

            string name = BlockmakerProfileManager.DisplayName ?? string.Empty;
            _displayName.text = name;

            string initial = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
            if (_initial != null) _initial.text = initial;

            if (_tier != null)
            {
                string tierText = isGuest ? "Guest" : identity.ProviderName;
                _tier.text = tierText;
            }

            if (_address != null)
            {
                if (isGuest || string.IsNullOrEmpty(identity?.Address))
                    _address.text = "No wallet linked";
                else
                    _address.text = BlockmakerUIUtils.ShortenAddress(identity.Address);
            }

            bool hasUsername = BlockmakerProfileManager.HasUsername;
            if (_usernameRow != null)
                _usernameRow.style.display = hasUsername ? DisplayStyle.Flex : DisplayStyle.None;
            if (_btnSetUsername != null)
                _btnSetUsername.style.display = hasUsername ? DisplayStyle.None : DisplayStyle.Flex;
            if (_username != null && hasUsername)
                _username.text = profile?.username ?? "";

            if (_usernameSection != null)
                _usernameSection.style.display = isGuest ? DisplayStyle.None : DisplayStyle.Flex;

            _btnSignOut?.SetEnabled(!isGuest);
            if (_btnSignOut != null)
                _btnSignOut.style.opacity = isGuest ? 0.4f : 1f;

            if (!string.IsNullOrEmpty(BlockmakerProfileManager.ProfileImageUrl))
                BlockmakerProfileManager.Instance?.LoadProfileTexture(ApplyProfileTexture);
        }

        private void ApplyProfileTexture(Texture2D tex)
        {
            if (_avatar == null || tex == null) return;
            _avatar.style.backgroundImage = new StyleBackground(tex);
            if (_initial != null) _initial.style.display = DisplayStyle.None;
        }

        private void CopyAddress()
        {
            var identity = BlockmakerAuth.Instance?.Identity;
            if (identity == null || string.IsNullOrEmpty(identity.Address)) return;

            GUIUtility.systemCopyBuffer = identity.Address;
            if (_copyConfirm != null)
            {
                _copyConfirm.style.display = DisplayStyle.Flex;
                _copyConfirmTimer = 2f;
            }
        }

    }

}