using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{
    /// <summary>
    /// SDK profile screen — slide-in panel with avatar, username, wallet address,
    /// profile picture, and onboarding nudges. Configurable via BlockmakerConfig
    /// feature toggles.
    ///
    /// Call Show() to open, Hide() to close.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BlockmakerProfileController : MonoBehaviour
    {
        public static BlockmakerProfileController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            OnProfileScreenOpened = null;
            OnProfileScreenClosed = null;
        }

        public static event Action OnProfileScreenOpened;
        public static event Action OnProfileScreenClosed;

        // ── UI refs ───────────────────────────────────────────────────────────
        private VisualElement _root;
        private VisualElement _backdrop;
        private VisualElement _panel;
        private VisualElement _avatar;
        private Label _avatarInitial;
        private Label _displayName;
        private Label _tier;
        private VisualElement _picNudge;
        private VisualElement _addressSection;
        private Label _address;
        private Label _copyConfirm;
        private VisualElement _usernameSection;
        private VisualElement _hasUsername;
        private Label _username;
        private VisualElement _noUsername;
        private TextField _usernameInput;
        private Label _usernameStatus;
        private Button _claimBtn;
        private VisualElement _pictureSection;
        private Label _statusLabel;
        private Button _signOutBtn;

        private Coroutine _copyCoroutine;
        private Coroutine _checkUsernameCoroutine;
        private bool _isOpen;
        private bool _isChangingUsername;
        private ProfilePicPickerController _pickerCtrl;
        private VisualElement _pickerRoot;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;

            _root = root.Q("bm-profile-root");
            _backdrop = root.Q("bm-profile-backdrop");
            _panel = root.Q("bm-profile-panel");
            _avatar = root.Q("bm-profile-avatar");
            _avatarInitial = root.Q<Label>("bm-profile-avatar-initial");
            _displayName = root.Q<Label>("bm-profile-display-name");
            _tier = root.Q<Label>("bm-profile-tier");
            _picNudge = root.Q("bm-profile-pic-nudge");
            _addressSection = root.Q("bm-profile-address-section");
            _address = root.Q<Label>("bm-profile-address");
            _copyConfirm = root.Q<Label>("bm-profile-copy-confirm");
            _usernameSection = root.Q("bm-profile-username-section");
            _hasUsername = root.Q("bm-profile-has-username");
            _username = root.Q<Label>("bm-profile-username");
            _noUsername = root.Q("bm-profile-no-username");
            _usernameInput = root.Q<TextField>("input-profile-username");
            _usernameStatus = root.Q<Label>("bm-profile-username-status");
            _claimBtn = root.Q<Button>("btn-profile-claim-username");
            _pictureSection = root.Q("bm-profile-picture-section");
            _statusLabel = root.Q<Label>("bm-profile-status");
            _signOutBtn = root.Q<Button>("btn-profile-signout");

            // Wire buttons
            root.Q<Button>("btn-profile-close")?.RegisterCallback<ClickEvent>(_ => Hide());
            _backdrop?.RegisterCallback<ClickEvent>(_ => Hide());
            root.Q<Button>("btn-profile-copy")?.RegisterCallback<ClickEvent>(_ => CopyAddress());
            root.Q<Button>("btn-profile-set-picture")?.RegisterCallback<ClickEvent>(_ => OpenPicturePicker());
            root.Q<Button>("btn-profile-change-picture")?.RegisterCallback<ClickEvent>(_ => OpenPicturePicker());
            root.Q<Button>("btn-profile-change-username")?.RegisterCallback<ClickEvent>(_ => StartUsernameChange());
            _claimBtn?.RegisterCallback<ClickEvent>(_ => OnClaimPressed());
            _signOutBtn?.RegisterCallback<ClickEvent>(_ => OnSignOut());

            if (_usernameInput != null)
            {
                _usernameInput.maxLength = 20;
                _usernameInput.RegisterValueChangedCallback(OnUsernameInputChanged);
            }

            if (_root != null)
                _root.style.display = DisplayStyle.None;
        }

        private void OnEnable()
        {
            BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;
            BlockmakerProfileManager.OnProfileLoaded += HandleProfileChanged;
            BlockmakerProfileManager.OnProfileUpdated += HandleProfileChanged;
        }

        private void OnDisable()
        {
            BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
            BlockmakerProfileManager.OnProfileLoaded -= HandleProfileChanged;
            BlockmakerProfileManager.OnProfileUpdated -= HandleProfileChanged;

            if (_copyCoroutine != null) { StopCoroutine(_copyCoroutine); _copyCoroutine = null; }
            if (_checkUsernameCoroutine != null) { StopCoroutine(_checkUsernameCoroutine); _checkUsernameCoroutine = null; }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show()
        {
            if (_root == null) return;
            _isOpen = true;
            _root.style.display = DisplayStyle.Flex;
            Refresh();
            OnProfileScreenOpened?.Invoke();
        }

        public void Hide()
        {
            if (_root == null || !_isOpen) return;
            _isOpen = false;
            _root.style.display = DisplayStyle.None;
            OnProfileScreenClosed?.Invoke();
        }

        public bool IsOpen => _isOpen;

        // ── Refresh ───────────────────────────────────────────────────────────

        private void HandleIdentityChanged(IBlockmakerIdentity _) => Refresh();
        private void HandleProfileChanged(BlockmakerProfile _) => Refresh();

        private void Refresh()
        {
            if (!_isOpen || _root == null) return;

            var auth = BlockmakerAuth.Instance;
            var cfg = auth?.blockmakerConfig;
            bool loggedIn = auth != null && auth.Tier != IdentityTier.Guest && auth.HasWallet;

            // Identity
            string name = BlockmakerProfileManager.DisplayName ?? "Guest";
            if (_displayName != null) _displayName.text = name;
            if (_avatarInitial != null)
                _avatarInitial.text = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
            if (_tier != null)
                _tier.text = loggedIn ? (auth.Tier == IdentityTier.Email ? "Email" : auth.Identity?.ProviderName ?? "") : "Guest";

            // Avatar image
            if (!string.IsNullOrEmpty(BlockmakerProfileManager.ProfileImageUrl))
            {
                BlockmakerProfileManager.Instance?.LoadProfileTexture(tex =>
                {
                    if (tex == null || _avatar == null) return;
                    _avatar.style.backgroundImage = new StyleBackground(tex);
                    if (_avatarInitial != null) _avatarInitial.style.display = DisplayStyle.None;
                });
            }
            else
            {
                if (_avatar != null) _avatar.style.backgroundImage = StyleKeyword.None;
                if (_avatarInitial != null) _avatarInitial.style.display = DisplayStyle.Flex;
            }

            // Address
            string addr = BlockmakerProfileManager.WalletAddress;
            if (_address != null)
                _address.text = string.IsNullOrEmpty(addr) ? "Not connected" : BlockmakerUIUtils.ShortenAddress(addr);

            // Feature toggles
            bool showUsernames = cfg == null || cfg.enableUsernames;
            bool showPictures = cfg == null || cfg.enableProfilePictures;

            // Username section
            if (_usernameSection != null)
                _usernameSection.style.display = (loggedIn && showUsernames) ? DisplayStyle.Flex : DisplayStyle.None;

            bool hasUser = BlockmakerProfileManager.HasUsername;
            if (hasUser) _isChangingUsername = false;
            if (_hasUsername != null) _hasUsername.style.display = (hasUser && !_isChangingUsername) ? DisplayStyle.Flex : DisplayStyle.None;
            if (_noUsername != null) _noUsername.style.display = (!hasUser && BlockmakerProfileManager.ShouldShowUsernameNudge()) || _isChangingUsername ? DisplayStyle.Flex : DisplayStyle.None;
            if (_claimBtn != null) _claimBtn.text = _isChangingUsername ? "Change Username" : "Claim Username";
            if (hasUser && _username != null) _username.text = BlockmakerProfileManager.Profile?.username ?? "";

            // Profile picture nudge
            if (_picNudge != null)
                _picNudge.style.display = (loggedIn && BlockmakerProfileManager.ShouldShowProfilePicNudge()) ? DisplayStyle.Flex : DisplayStyle.None;

            // Picture section
            if (_pictureSection != null)
                _pictureSection.style.display = (loggedIn && showPictures) ? DisplayStyle.Flex : DisplayStyle.None;

            // Sign out
            if (_signOutBtn != null)
                _signOutBtn.style.display = loggedIn ? DisplayStyle.Flex : DisplayStyle.None;

            // Status
            SetStatus("");
        }

        // ── Username ──────────────────────────────────────────────────────────

        private void OnUsernameInputChanged(ChangeEvent<string> evt)
        {
            var val = evt.newValue?.Trim().ToLower() ?? "";
            if (_checkUsernameCoroutine != null) StopCoroutine(_checkUsernameCoroutine);
            if (val.Length < 3)
            {
                SetUsernameStatus(val.Length > 0 ? "At least 3 characters" : "", false);
                if (_claimBtn != null) _claimBtn.SetEnabled(false);
                return;
            }
            _checkUsernameCoroutine = StartCoroutine(CheckUsernameDebounced(val));
        }

        private IEnumerator CheckUsernameDebounced(string username)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            _checkUsernameCoroutine = null;

            if (BlockmakerClient.Instance == null) yield break;

            SetUsernameStatus("Checking...", false);
            bool done = false;
            bool available = false;
            string reason = null;
            string error = null;

            BlockmakerClient.Instance.Post<UsernameCheckRequest, UsernameCheckResult>(
                "/v1/profile/username/check",
                new UsernameCheckRequest { username = username },
                result => { available = result.available; reason = result.reason; done = true; },
                err => { error = err; done = true; }
            );

            float elapsed = 0f;
            while (!done && elapsed < 10f) { elapsed += Time.unscaledDeltaTime; yield return null; }

            if (error != null)
            {
                SetUsernameStatus(error, true);
                if (_claimBtn != null) _claimBtn.SetEnabled(false);
            }
            else if (available)
            {
                SetUsernameStatus("Available!", false);
                if (_claimBtn != null) _claimBtn.SetEnabled(true);
            }
            else
            {
                SetUsernameStatus(!string.IsNullOrEmpty(reason) ? reason : "Already taken", true);
                if (_claimBtn != null) _claimBtn.SetEnabled(false);
            }
        }

        private void OnClaimPressed()
        {
            var username = _usernameInput?.value?.Trim().ToLower();
            if (string.IsNullOrEmpty(username) || username.Length < 3) return;

            if (_claimBtn != null) _claimBtn.SetEnabled(false);

            if (_isChangingUsername)
            {
                SetStatus("Changing username...");
                BlockmakerProfileManager.Instance?.ChangeUsername(username,
                    profile => { _isChangingUsername = false; SetStatus(""); Refresh(); },
                    err => { SetStatus(err, true); if (_claimBtn != null) _claimBtn.SetEnabled(true); }
                );
            }
            else
            {
                SetStatus("Claiming username...");
                BlockmakerProfileManager.Instance?.ClaimUsername(username,
                    profile => { SetStatus(""); Refresh(); },
                    err => { SetStatus(err, true); if (_claimBtn != null) _claimBtn.SetEnabled(true); }
                );
            }
        }

        private void StartUsernameChange()
        {
            _isChangingUsername = true;
            if (_hasUsername != null) _hasUsername.style.display = DisplayStyle.None;
            if (_noUsername != null) _noUsername.style.display = DisplayStyle.Flex;
            if (_usernameInput != null) _usernameInput.value = "";
            if (_claimBtn != null) _claimBtn.text = "Change Username";
            SetUsernameStatus("", false);
        }

        private void SetUsernameStatus(string text, bool isError)
        {
            if (_usernameStatus == null) return;
            _usernameStatus.text = text;
            _usernameStatus.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            _usernameStatus.style.color = isError
                ? new Color(0.94f, 0.26f, 0.26f)
                : new Color(0.063f, 0.725f, 0.506f);
        }

        // ── Profile Picture ───────────────────────────────────────────────────

        private void OpenPicturePicker()
        {
            if (_pickerCtrl != null)
            {
                _pickerCtrl.Open();
                return;
            }

            // Load the picker UXML from the package
            VisualTreeAsset pickerAsset = null;
            #if UNITY_EDITOR
            pickerAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.blockmaker.sdk/UI/ProfilePicPicker.uxml");
            #endif

            if (pickerAsset == null)
            {
                SetStatus("Profile picture picker is not available.", true);
                return;
            }

            // Instantiate and add to our panel
            _pickerRoot = pickerAsset.Instantiate();
            _pickerRoot.style.position = Position.Absolute;
            _pickerRoot.style.left = 0;
            _pickerRoot.style.top = 0;
            _pickerRoot.style.right = 0;
            _pickerRoot.style.bottom = 0;
            _root.Add(_pickerRoot);

            // Get avatar set from config
            var avatarSet = BlockmakerAuth.Instance?.blockmakerConfig?.defaultAvatarSet;

            _pickerCtrl = new ProfilePicPickerController(_pickerRoot, avatarSet, this);
            _pickerCtrl.OnPicChanged += () =>
            {
                Refresh();
                BlockmakerProfileManager.Instance?.RefreshProfile();
            };
            _pickerCtrl.Open();
        }

        // ── Copy Address ──────────────────────────────────────────────────────

        private void CopyAddress()
        {
            var addr = BlockmakerProfileManager.WalletAddress;
            if (string.IsNullOrEmpty(addr)) return;
            GUIUtility.systemCopyBuffer = addr;
            if (_copyConfirm != null)
            {
                _copyConfirm.style.display = DisplayStyle.Flex;
                if (_copyCoroutine != null) StopCoroutine(_copyCoroutine);
                _copyCoroutine = StartCoroutine(HideCopyConfirm());
            }
        }

        private IEnumerator HideCopyConfirm()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (_copyConfirm != null) _copyConfirm.style.display = DisplayStyle.None;
            _copyCoroutine = null;
        }

        // ── Sign Out ──────────────────────────────────────────────────────────

        private void OnSignOut()
        {
            BlockmakerAuth.Instance?.Logout();
            Hide();
        }

        // ── Status ────────────────────────────────────────────────────────────

        private void SetStatus(string text, bool isError = false)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            _statusLabel.RemoveFromClassList("bm-profile-status--error");
            _statusLabel.RemoveFromClassList("bm-profile-status--success");
            if (isError)
                _statusLabel.AddToClassList("bm-profile-status--error");
            else if (!string.IsNullOrEmpty(text))
                _statusLabel.AddToClassList("bm-profile-status--success");
        }
    }

    [Serializable]
    internal class UsernameCheckRequest
    {
        public string username;
    }

    [Serializable]
    internal class UsernameCheckResult
    {
        public bool available;
        public string reason;
        public string error;
    }
}
