using System;
using System.Collections;
using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// Tier 1 identity. No wallet, no email. Uses a stable device ID so a guest's
    /// local progress (race points, settings) persists across sessions on the same
    /// device. Cannot sign transactions.
    ///
    /// When the game wants to prompt an upgrade:
    ///   if (identity.Tier == IdentityTier.Guest) ShowUpgradeUI();
    /// </summary>
    public class GuestIdentity : IBlockmakerIdentity
    {
        private static string DeviceIdKey => BlockmakerPrefs.Key("guest_device_id");

        // ── IBlockmakerIdentity ────────────────────────────────────────────────────

        public string        Address      { get; }
        public string        DisplayName  { get; }
        public IdentityTier  Tier         => IdentityTier.Guest;
        public string        ProviderName => "Guest";
        public bool          HasWallet    => false;
        public bool          CanSign      => false;

        // ── Constructor ────────────────────────────────────────────────────────────

        public GuestIdentity()
        {
            Address     = LoadOrCreateDeviceId();
            DisplayName = Address.Length >= 6 ? $"Guest_{Address[..6]}" : $"Guest_{Address}";
        }

        // ── Signing ────────────────────────────────────────────────────────────────

        public IEnumerator SignTransaction(
            string         unsignedTxnBase64,
            Action<string> onSigned,
            Action<string> onError)
        {
            onError?.Invoke("Guest accounts cannot sign transactions. Please connect a wallet or sign in with email.");
            yield break;
        }

        public IEnumerator SignTransactions(
            string[]         unsignedTxnsBase64,
            Action<string[]> onSigned,
            Action<string>   onError)
        {
            onError?.Invoke("Guest accounts cannot sign transactions. Please connect a wallet or sign in with email.");
            yield break;
        }

        // ── Session ────────────────────────────────────────────────────────────────

        public void SaveSession()
        {
            // Device ID is already persisted in PlayerPrefs — nothing extra to save
        }

        public void ClearSession()
        {
            // Guests don't have a session to clear, but we keep the device ID
            // so local progress (offline race points etc.) isn't lost on "logout"
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static string LoadOrCreateDeviceId()
        {
            string id = PlayerPrefs.GetString(DeviceIdKey, string.Empty);

            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdKey, id);
                PlayerPrefs.Save();
                BlockmakerLog.Info($"[GuestIdentity] Created new device ID: {id[..8]}…");
            }

            return id;
        }
    }
}