using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker;

/// <summary>
/// Controls the WalletBar.uxml widget. Binds to BlockmakerProfileManager
/// and BlockmakerAuth events to keep identity info up to date.
///
/// Usage: attach a UIDocument to a GameObject (SortOrder > 0), assign
/// WalletBar.uxml as the Source Asset, then add this component.
/// The bar auto-updates whenever identity or profile changes.
///
/// To respond to "Set username" taps, subscribe to OnSetUsernameClicked.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class WalletBarController : MonoBehaviour
{
    // ── Public event ───────────────────────────────────────────────────────────
    public static event Action OnSetUsernameClicked;

    // ── UI element refs ────────────────────────────────────────────────────────
    private VisualElement _avatar;
    private Label         _avatarInitial;
    private Label         _displayName;
    private Label         _subLabel;
    private Button        _btnUsername;
    private Button        _btnDisconnect;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        var doc = GetComponent<UIDocument>();
        if (doc.visualTreeAsset == null)
        {
            BlockmakerLog.Error("[WalletBarController] UIDocument has no Source Asset assigned. Assign WalletBar.uxml.");
            return;
        }

        var root = doc.rootVisualElement;

        _avatar        = root.Q<VisualElement>("wb-avatar");
        _avatarInitial = root.Q<Label>("wb-avatar-initial");
        _displayName   = root.Q<Label>("wb-display-name");
        _subLabel      = root.Q<Label>("wb-sub-label");
        _btnUsername   = root.Q<Button>("wb-btn-username");
        _btnDisconnect = root.Q<Button>("wb-btn-disconnect");

        _btnUsername?.RegisterCallback<ClickEvent>(_ => OnSetUsernameClicked?.Invoke());
        _btnDisconnect?.RegisterCallback<ClickEvent>(_ => BlockmakerAuth.Instance?.Logout());
    }

    private void OnEnable()
    {
        BlockmakerAuth.OnIdentityChanged                += HandleIdentityChanged;
        BlockmakerProfileManager.OnProfileLoaded        += HandleProfileChanged;
        BlockmakerProfileManager.OnProfileUpdated       += HandleProfileChanged;

        Refresh();
    }

    private void OnDisable()
    {
        BlockmakerAuth.OnIdentityChanged                -= HandleIdentityChanged;
        BlockmakerProfileManager.OnProfileLoaded        -= HandleProfileChanged;
        BlockmakerProfileManager.OnProfileUpdated       -= HandleProfileChanged;
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void HandleIdentityChanged(IBlockmakerIdentity _) => Refresh();
    private void HandleProfileChanged(BlockmakerProfile _)    => Refresh();

    // ── Refresh ────────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (_displayName == null) return;   // Awake not yet called or missing asset

        var auth     = BlockmakerAuth.Instance;
        var profile  = BlockmakerProfileManager.Profile;
        var identity = auth?.Identity;

        string name     = BlockmakerProfileManager.DisplayName ?? string.Empty;
        string subLabel = BuildSubLabel(identity, profile);
        bool   hasUser  = BlockmakerProfileManager.HasUsername;
        bool   isGuest  = (identity == null || identity.Tier == IdentityTier.Guest);

        _displayName.text = name;
        if (_subLabel != null) _subLabel.text = subLabel;

        // Avatar initial: first char of display name, uppercase
        string initial = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
        if (_avatarInitial != null) _avatarInitial.text = initial;

        // "Set username" button — only for non-guest players without a username
        if (_btnUsername != null)
        {
            bool showUsernameBtn = !isGuest && !hasUser;
            _btnUsername.EnableInClassList("wb-btn-username--visible", showUsernameBtn);
        }

        // Disconnect button — hide for guests (nothing to disconnect)
        _btnDisconnect?.SetEnabled(!isGuest);
        if (_btnDisconnect != null)
            _btnDisconnect.style.opacity = isGuest ? 0.3f : 1f;

        // Load profile image if available
        if (!string.IsNullOrEmpty(BlockmakerProfileManager.ProfileImageUrl))
            BlockmakerProfileManager.Instance?.LoadProfileTexture(ApplyProfileTexture);
    }

    private void ApplyProfileTexture(Texture2D tex)
    {
        if (_avatar == null || tex == null) return;
        _avatar.style.backgroundImage = new StyleBackground(tex);
        if (_avatarInitial != null) _avatarInitial.style.display = DisplayStyle.None;
    }

    private static string BuildSubLabel(IBlockmakerIdentity identity, BlockmakerProfile profile)
    {
        if (identity == null || identity.Tier == IdentityTier.Guest)
            return "Guest — sign in to save progress";

        if (identity is EvmXChainIdentity evm)
        {
            string shortEvm = evm.EvmAddress.Length > 10
                ? $"{evm.EvmAddress[..6]}...{evm.EvmAddress[^4..]}"
                : evm.EvmAddress;
            return $"ETH · {shortEvm}";
        }

        if (identity is MagicIdentity magic)
        {
            string addr = BlockmakerUIUtils.ShortenAddress(magic.Address);
            return $"Email wallet · {addr}";
        }

        string tier = profile?.tier ?? string.Empty;

        if (string.Equals(tier, "email", StringComparison.OrdinalIgnoreCase))
        {
            string addr = BlockmakerUIUtils.ShortenAddress(profile?.walletAddress ?? string.Empty);
            return $"Email wallet · {addr}";
        }

        if (profile != null && !string.IsNullOrEmpty(profile.nfdName))
            return profile.nfdName;

        return BlockmakerUIUtils.ShortenAddress(identity.Address);
    }

}
