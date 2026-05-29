using System;
using UnityEngine.UIElements;

namespace Blockmaker
{

    /// <summary>
    /// Manages the WalletUpgradePrompt UXML.
    /// Shown contextually when a guest or email-only player needs a self-custody
    /// wallet. The copy adapts to the reason it was triggered.
    /// </summary>
    public class WalletUpgradeController
    {
        // ── Upgrade reasons ────────────────────────────────────────────────────

        public enum UpgradeReason
        {
            /// <summary>Player is trying to claim an on-chain reward.</summary>
            ClaimReward,
            /// <summary>Player reached a personal-best podium position.</summary>
            PodiumFinish,
            /// <summary>Feature requires a verifiable on-chain wallet.</summary>
            NFTVerification,
            /// <summary>Player is about to make an in-game purchase.</summary>
            Purchase,
            /// <summary>Generic prompt — no specific context.</summary>
            Generic,
        }

        private static readonly (string title, string body)[] Copies =
        {
            // ClaimReward
            ("Claim Your Reward", "Connect a wallet app to receive your crypto reward. Takes less than a minute."),
            // PodiumFinish
            ("You Made the Podium!", "Great finish! Connect a wallet to receive your podium reward and appear on the official leaderboard."),
            // NFTVerification
            ("Verify Your Items", "Connect a wallet app so we can confirm which collectibles you own."),
            // Purchase
            ("Connect to Purchase", "Connect a wallet to complete your in-game purchase securely."),
            // Generic
            ("Connect a Wallet", "Unlock the full experience — prove you own your items, claim rewards, and appear on the leaderboard."),
        };

        // ── Elements ───────────────────────────────────────────────────────────

        private readonly VisualElement _root;
        private readonly VisualElement _card;
        private readonly VisualElement _backdrop;
        private readonly Label         _lblTitle;
        private readonly Label         _lblBody;
        private readonly Button        _btnPera;
        private readonly Button        _btnDefly;
        private readonly Button        _btnMetamask;
        private readonly Button        _btnSkip;

        // ── Callbacks ──────────────────────────────────────────────────────────

        public Action OnPeraClicked     { get; set; }
        public Action OnDeflyClicked    { get; set; }
        public Action OnMetamaskClicked { get; set; }
        public Action OnSkipped         { get; set; }

        // ── Constructor ────────────────────────────────────────────────────────

        public WalletUpgradeController(VisualElement root)
        {
            _root     = root;
            _card     = root.Q<VisualElement>("upgrade-card");
            _backdrop = root.Q<VisualElement>("upgrade-backdrop");
            _lblTitle = root.Q<Label>("lbl-upgrade-title");
            _lblBody  = root.Q<Label>("lbl-upgrade-body");
            _btnPera     = root.Q<Button>("btn-pera-upgrade");
            _btnDefly    = root.Q<Button>("btn-defly-upgrade");
            _btnMetamask = root.Q<Button>("btn-metamask-upgrade");
            _btnSkip     = root.Q<Button>("btn-skip-upgrade");

            var btnUpgrade = root.Q<Button>("btn-upgrade-wallet");
            if (btnUpgrade != null)
                btnUpgrade.clicked += () => OnPeraClicked?.Invoke();

            if (_btnPera != null)     _btnPera.clicked  += () => OnPeraClicked?.Invoke();
            if (_btnDefly != null)    _btnDefly.clicked += () => OnDeflyClicked?.Invoke();
            if (_btnMetamask != null) _btnMetamask.clicked += () => OnMetamaskClicked?.Invoke();
            if (_btnSkip != null)     _btnSkip.clicked  += () => OnSkipped?.Invoke();
            _backdrop?.RegisterCallback<ClickEvent>(_ => OnSkipped?.Invoke());
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Show the prompt with reason-specific copy.</summary>
        public void Show(UpgradeReason reason = UpgradeReason.Generic)
        {
            int idx = (int)reason;
            if (idx < 0 || idx >= Copies.Length) idx = (int)UpgradeReason.Generic;
            var (title, body) = Copies[idx];
            if (_lblTitle != null) _lblTitle.text = title;
            if (_lblBody  != null) _lblBody.text  = body;

            if (_btnMetamask != null)
            {
                var cfg = BlockmakerClient.Instance?.config;
                _btnMetamask.style.display = (cfg != null && cfg.enableEvmXChain)
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_root != null) _root.style.display = DisplayStyle.Flex;
            _card?.RemoveFromClassList("upgrade-card--hidden");
        }

        /// <summary>Hide the prompt.</summary>
        public void Hide()
        {
            _card?.AddToClassList("upgrade-card--hidden");
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }

}