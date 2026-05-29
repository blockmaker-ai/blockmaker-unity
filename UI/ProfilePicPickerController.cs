using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Blockmaker;

/// <summary>
/// Reusable profile picture picker overlay.
/// Two tabs: "Default" (built-in avatar grid) and "My NFTs" (wallet search).
///
/// This is the recommended component for SDK users building their own profile
/// scenes. Game-specific profile controllers may have their own built-in
/// picker — use this class when building custom UI.
///
/// Usage:
///   var picker = new ProfilePicPickerController(root, defaultAvatarSet, this);
///   picker.Open();
///
/// Subscribe to OnPicChanged to react when the player picks a new avatar.
/// The picker calls BlockmakerProfileManager internally — no extra wiring needed.
/// </summary>
public class ProfilePicPickerController
{
    public event Action OnPicChanged;

    private readonly VisualElement  _overlay;
    private readonly VisualElement  _defaultsGrid;
    private readonly VisualElement  _nftGrid;
    private readonly VisualElement  _pageDefaults;
    private readonly VisualElement  _pageNfts;
    private readonly Button         _tabDefaults;
    private readonly Button         _tabNfts;
    private readonly TextField      _inputNftSearch;
    private readonly Label          _lblNftStatus;
    private readonly Label          _lblStatus;

    private readonly DefaultAvatarSet _avatarSet;
    private readonly MonoBehaviour    _coroutineHost;

    private Coroutine     _searchCoroutine;
    private VisualElement _selectedCard;
    private long          _selectedNftId;
    private string        _selectedDefaultId;
    private readonly List<Texture2D> _loadedThumbnails = new List<Texture2D>();
    private readonly List<Coroutine> _thumbnailCoroutines = new List<Coroutine>();

    private bool IsHostAlive => _coroutineHost != null && _coroutineHost.gameObject != null;

    public ProfilePicPickerController(VisualElement root, DefaultAvatarSet avatarSet, MonoBehaviour coroutineHost)
    {
        _avatarSet     = avatarSet;
        _coroutineHost = coroutineHost;

        _overlay       = root.Q<VisualElement>("overlay-pfp-picker");
        _defaultsGrid  = root.Q<VisualElement>("defaults-grid");
        _nftGrid       = root.Q<VisualElement>("nft-grid");
        _pageDefaults  = root.Q<VisualElement>("page-defaults");
        _pageNfts      = root.Q<VisualElement>("page-nfts");
        _tabDefaults   = root.Q<Button>("tab-defaults");
        _tabNfts       = root.Q<Button>("tab-nfts");
        _inputNftSearch = root.Q<TextField>("input-nft-search");
        _lblNftStatus  = root.Q<Label>("lbl-nft-status");
        _lblStatus     = root.Q<Label>("lbl-pfp-status");

        root.Q<Button>("btn-pfp-cancel")?.RegisterCallback<ClickEvent>(_ => Close());

        _tabDefaults?.RegisterCallback<ClickEvent>(_ => ShowTab(defaults: true));
        _tabNfts?.RegisterCallback<ClickEvent>(_ => ShowTab(defaults: false));

        root.Q<Button>("btn-nft-search")?.RegisterCallback<ClickEvent>(_ => TriggerNftSearch());
        _inputNftSearch?.RegisterValueChangedCallback(OnSearchChanged);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Open()
    {
        if (_overlay == null) return;
        _overlay.style.display = DisplayStyle.Flex;
        _selectedCard     = null;
        _selectedNftId    = 0;
        _selectedDefaultId = null;
        SetStatus("");
        ShowTab(defaults: true);
        PopulateDefaults();
    }

    public void Close()
    {
        if (_overlay != null) _overlay.style.display = DisplayStyle.None;
        if (_searchCoroutine != null && IsHostAlive)
        {
            _coroutineHost.StopCoroutine(_searchCoroutine);
            _searchCoroutine = null;
        }
        DestroyLoadedThumbnails();
    }

    public bool IsOpen => _overlay != null && _overlay.resolvedStyle.display != DisplayStyle.None;

    // ── Tabs ──────────────────────────────────────────────────────────────────

    private void ShowTab(bool defaults)
    {
        _tabDefaults?.EnableInClassList("pfp-tab--active", defaults);
        _tabNfts?.EnableInClassList("pfp-tab--active", !defaults);

        if (_pageDefaults != null)
        {
            _pageDefaults.RemoveFromClassList("pfp-page--hidden");
            if (!defaults) _pageDefaults.AddToClassList("pfp-page--hidden");
        }
        if (_pageNfts != null)
        {
            _pageNfts.RemoveFromClassList("pfp-page--hidden");
            if (defaults) _pageNfts.AddToClassList("pfp-page--hidden");
        }

        if (!defaults && _nftGrid != null && _nftGrid.childCount == 0)
            SetNftStatus("Type a name to find your collectibles.");
    }

    // ── Default avatars ───────────────────────────────────────────────────────

    private void PopulateDefaults()
    {
        if (_defaultsGrid == null) return;
        _defaultsGrid.Clear();

        if (_avatarSet == null || _avatarSet.avatars == null || _avatarSet.avatars.Count == 0)
        {
            var empty = new Label("No avatars available yet.");
            empty.AddToClassList("pfp-status");
            _defaultsGrid.Add(empty);
            return;
        }

        foreach (var entry in _avatarSet.avatars)
        {
            var card = new VisualElement();
            card.AddToClassList("pfp-default-card");

            var thumb = new VisualElement();
            thumb.AddToClassList("pfp-default-card__thumb");
            if (entry.thumbnail != null)
                thumb.style.backgroundImage = new StyleBackground(entry.thumbnail);
            card.Add(thumb);

            var nameLabel = new Label(entry.name ?? entry.id);
            nameLabel.AddToClassList("pfp-default-card__name");
            card.Add(nameLabel);

            string capturedId = entry.id;
            card.RegisterCallback<PointerUpEvent>(_ => SelectDefault(capturedId, card));

            _defaultsGrid.Add(card);
        }
    }

    private void SelectDefault(string avatarId, VisualElement card)
    {
        if (_selectedCard != null)
        {
            _selectedCard.RemoveFromClassList("pfp-default-card--selected");
            _selectedCard.RemoveFromClassList("pfp-nft-card--selected");
        }

        _selectedCard = card;
        _selectedDefaultId = avatarId;
        _selectedNftId = 0;
        card.AddToClassList("pfp-default-card--selected");

        ConfirmDefault(avatarId);
    }

    private void ConfirmDefault(string avatarId)
    {
        var mgr = BlockmakerProfileManager.Instance;
        if (mgr == null) { SetStatus("Unable to load your profile. Please try again."); return; }

        SetStatus("Setting avatar…");

        mgr.SetDefaultAvatar(avatarId,
            _ =>
            {
                SetStatus("");
                Close();
                OnPicChanged?.Invoke();
            },
            err =>
            {
                SetStatus(string.IsNullOrEmpty(err) ? "Could not set avatar. Please try again." : err);
            });
    }

    // ── NFT search ────────────────────────────────────────────────────────────

    private void OnSearchChanged(ChangeEvent<string> evt)
    {
        if (!IsHostAlive) return;
        if (_searchCoroutine != null)
            _coroutineHost.StopCoroutine(_searchCoroutine);
        _searchCoroutine = _coroutineHost.StartCoroutine(SearchDebounced(evt.newValue));
    }

    private void TriggerNftSearch()
    {
        string term = _inputNftSearch?.value?.Trim() ?? "";
        if (!IsHostAlive) return;
        if (_searchCoroutine != null)
            _coroutineHost.StopCoroutine(_searchCoroutine);
        _searchCoroutine = _coroutineHost.StartCoroutine(SearchDebounced(term, immediate: true));
    }

    private IEnumerator SearchDebounced(string term, bool immediate = false)
    {
        if (!immediate) yield return new WaitForSecondsRealtime(0.5f);

        term = term?.Trim() ?? "";
        if (string.IsNullOrEmpty(term))
        {
            _nftGrid?.Clear();
            SetNftStatus("Type a name to find your collectibles.");
            yield break;
        }

        SetNftStatus("Searching…");
        _nftGrid?.Clear();

        var client = BlockmakerClient.Instance;
        if (client == null)
        {
            BlockmakerLog.Error("[ProfilePicPicker] BlockmakerClient.Instance is null during NFT search");
            SetNftStatus("Can't reach the server right now. Try again in a moment.");
            yield break;
        }

        string wallet = BlockmakerAuth.Instance?.Address;
        if (string.IsNullOrEmpty(wallet))
        {
            SetNftStatus("No wallet connected. Please sign in first.");
            yield break;
        }

        bool done = false;
        WalletNFTSearchResult result = null;
        string error = null;

        client.SearchWalletNFTs(term,
            r => { result = r; done = true; },
            e => { error  = e; done = true; });

        float elapsed = 0f;
        while (!done && elapsed < 15f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!done)
        {
            SetNftStatus("Search timed out. Please try again.");
            yield break;
        }

        if (error != null)
        {
            BlockmakerLog.Error($"[ProfilePicPicker] NFT search error: {error}");
            SetNftStatus("Could not search your wallet. Please try again.");
            yield break;
        }

        if (result == null || result.assets == null || result.assets.Count == 0)
        {
            SetNftStatus("No NFTs found matching your search.");
            yield break;
        }

        SetNftStatus("");
        DisplayNftResults(result.assets);
    }

    private void DisplayNftResults(List<WalletNFTAsset> assets)
    {
        if (_nftGrid == null) return;
        DestroyLoadedThumbnails();
        _nftGrid.Clear();
        _selectedNftId = 0;
        _selectedCard  = null;

        foreach (var asset in assets)
        {
            var card = new VisualElement();
            card.AddToClassList("pfp-nft-card");

            var nameLabel = new Label(asset.name ?? "Unknown");
            nameLabel.AddToClassList("pfp-nft-card__name");
            card.Add(nameLabel);

            var idLabel = new Label($"#{asset.assetId}");
            idLabel.AddToClassList("pfp-nft-card__id");
            card.Add(idLabel);

            long capturedId = asset.assetId;
            card.RegisterCallback<PointerUpEvent>(_ => SelectNft(capturedId, card));

            _nftGrid.Add(card);
        }
    }

    private void SelectNft(long assetId, VisualElement card)
    {
        if (_selectedCard != null)
        {
            _selectedCard.RemoveFromClassList("pfp-nft-card--selected");
            _selectedCard.RemoveFromClassList("pfp-default-card--selected");
        }

        _selectedCard      = card;
        _selectedNftId     = assetId;
        _selectedDefaultId = null;
        card.AddToClassList("pfp-nft-card--selected");

        var client = BlockmakerClient.Instance;
        if (client == null) { ConfirmNft(assetId); return; }

        SetNftStatus("Loading preview…");
        client.GetNftImageUrl(assetId,
            result =>
            {
                if (!IsOpen) return;
                if (!string.IsNullOrEmpty(result.imageUrl) && IsHostAlive)
                {
                    var existing = card.Q(className: "pfp-nft-card__thumb");
                    if (existing == null)
                    {
                        var thumb = new VisualElement();
                        thumb.AddToClassList("pfp-nft-card__thumb");
                        card.Insert(0, thumb);
                        _thumbnailCoroutines.Add(_coroutineHost.StartCoroutine(LoadThumbnail(result.imageUrl, thumb)));
                    }
                }
                SetNftStatus("");
                ConfirmNft(assetId);
            },
            _ => { if (IsOpen) ConfirmNft(assetId); });
    }

    private void ConfirmNft(long assetId)
    {
        if (assetId <= 0) return;

        var mgr = BlockmakerProfileManager.Instance;
        if (mgr == null) { SetStatus("Unable to load your profile. Please try again."); return; }

        SetStatus("Setting profile picture…");

        mgr.SetProfilePicNft(assetId,
            _ =>
            {
                SetStatus("");
                Close();
                OnPicChanged?.Invoke();
            },
            err =>
            {
                SetStatus(string.IsNullOrEmpty(err)
                    ? "Something went wrong setting your picture. Please try a different one."
                    : err);
            });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private IEnumerator LoadThumbnail(string url, VisualElement thumb)
    {
        using var req = UnityWebRequestTexture.GetTexture(url);
        req.timeout = 15;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) yield break;
        var tex = DownloadHandlerTexture.GetContent(req);
        if (tex != null && thumb != null)
        {
            _loadedThumbnails.Add(tex);
            thumb.style.backgroundImage = new StyleBackground(tex);
        }
    }

    private void DestroyLoadedThumbnails()
    {
        if (IsHostAlive)
        {
            foreach (var co in _thumbnailCoroutines)
                if (co != null) _coroutineHost.StopCoroutine(co);
        }
        _thumbnailCoroutines.Clear();

        foreach (var tex in _loadedThumbnails)
            if (tex != null) UnityEngine.Object.Destroy(tex);
        _loadedThumbnails.Clear();
    }

    private void SetStatus(string msg)
    {
        if (_lblStatus == null) return;
        _lblStatus.text = msg;
        _lblStatus.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void SetNftStatus(string msg)
    {
        if (_lblNftStatus == null) return;
        _lblNftStatus.text = msg;
        _lblNftStatus.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex;
    }
}
