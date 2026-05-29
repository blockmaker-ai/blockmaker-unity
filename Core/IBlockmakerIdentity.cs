using System;
using System.Collections;

namespace Blockmaker
{

    /// <summary>
    /// The single identity abstraction used throughout Blockmaker and any game
    /// built on top of it. Game code never references a specific wallet or
    /// auth provider — it always talks to IBlockmakerIdentity.
    ///
    /// Three tiers exist (see IdentityTier):
    ///   Guest      — device ID only, no signing capability
    ///   Email      — email login, server-managed wallet, transparent signing
    ///   SelfCustody — user-owned wallet (Pera, Defly, etc.), in-browser signing
    /// </summary>
    public interface IBlockmakerIdentity
    {
        // ── Identity ───────────────────────────────────────────────────────────────

        /// <summary>Algorand wallet address, or a local device ID for guests.</summary>
        string Address { get; }

        /// <summary>Human-readable name for UI display.</summary>
        string DisplayName { get; }

        /// <summary>Which auth tier this identity represents.</summary>
        IdentityTier Tier { get; }

        /// <summary>Provider label e.g. "Pera", "Defly", "Email", "Guest".</summary>
        string ProviderName { get; }

        /// <summary>True if this identity has a real on-chain Algorand address.</summary>
        bool HasWallet { get; }

        /// <summary>True if this identity can sign transactions (Email + SelfCustody).</summary>
        bool CanSign { get; }

        /// <summary>
        /// The player's Algorand address if they have a wallet, null otherwise.
        /// Convenience property — same as checking HasWallet then reading Address.
        /// </summary>
        string AlgorandAddress => HasWallet ? Address : null;

        /// <summary>
        /// True if this identity can sign an atomic group of transactions in a single
        /// wallet approval. Override to false for identities that require N separate
        /// wallet popups (e.g. EVM xChain on native platforms).
        /// </summary>
        bool SupportsAtomicGroupSign => true;

        // ── Signing ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sign a single base64-encoded unsigned msgpack transaction.
        /// For Email tier: the server signs on the player's behalf.
        /// For SelfCustody: opens the wallet app for user approval.
        /// For Guest: always calls onError — check CanSign first.
        /// </summary>
        IEnumerator SignTransaction(
            string           unsignedTxnBase64,
            Action<string>   onSigned,
            Action<string>   onError);

        /// <summary>
        /// Sign a group of base64-encoded unsigned msgpack transactions.
        /// All transactions in the group are signed atomically.
        /// </summary>
        IEnumerator SignTransactions(
            string[]           unsignedTxnsBase64,
            Action<string[]>   onSigned,
            Action<string>     onError);

        // ── Session ────────────────────────────────────────────────────────────────

        /// <summary>Persist session data so it can be restored on next launch.</summary>
        void SaveSession();

        /// <summary>Clear all session data (logout).</summary>
        void ClearSession();
    }

    // ── Supporting types ───────────────────────────────────────────────────────────

    public enum IdentityTier
    {
        /// <summary>No account. Local device ID only. Cannot sign transactions.</summary>
        Guest = 0,

        /// <summary>Email login with a server-managed Algorand wallet.</summary>
        Email = 1,

        /// <summary>Self-custody wallet (Pera, Defly). User signs in-browser.</summary>
        SelfCustody = 2
    }
}