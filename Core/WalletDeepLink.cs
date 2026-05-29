using UnityEngine;

namespace Blockmaker;

/// <summary>
/// Converts WalletConnect URIs to mobile deep links for Pera and Defly wallets.
/// On mobile platforms, opens the wallet app directly. On desktop/WebGL, no-ops.
/// </summary>
public static class WalletDeepLink
{
    /// <summary>Open the Pera wallet app with a WalletConnect URI.</summary>
    public static void OpenPera(string wcUri)
    {
        if (!IsMobilePlatform) return;
        string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(wcUri);
        Application.OpenURL($"perawallet-wc://wc?uri={encoded}");
    }

    /// <summary>Open the Defly wallet app with a WalletConnect URI.</summary>
    public static void OpenDefly(string wcUri)
    {
        if (!IsMobilePlatform) return;
        string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(wcUri);
        Application.OpenURL($"algorand-wc://wc?uri={encoded}");
    }

    /// <summary>Open the appropriate wallet app based on provider name.</summary>
    public static void OpenWallet(string providerName, string wcUri)
    {
        if (string.IsNullOrEmpty(wcUri)) return;

        switch (providerName?.ToLower())
        {
            case "pera":
                OpenPera(wcUri);
                break;
            case "defly":
                OpenDefly(wcUri);
                break;
            default:
                BlockmakerLog.Warning($"[WalletDeepLink] Unknown provider: {providerName}");
                break;
        }
    }

    /// <summary>True on iOS and Android.</summary>
    public static bool IsMobilePlatform =>
        Application.platform == RuntimePlatform.IPhonePlayer ||
        Application.platform == RuntimePlatform.Android;

    /// <summary>Open a generic WalletConnect URI — lets the OS choose the handler.</summary>
    public static void OpenGeneric(string wcUri)
    {
        if (!IsMobilePlatform || string.IsNullOrEmpty(wcUri)) return;
        Application.OpenURL(wcUri);
    }
}
