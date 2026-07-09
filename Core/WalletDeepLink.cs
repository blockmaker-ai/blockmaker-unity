using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// Converts WalletConnect URIs to mobile deep links for Pera and Defly wallets.
    /// On mobile platforms — including WebGL running in a mobile browser — opens the
    /// wallet app directly. On desktop, no-ops (the QR code is the desktop flow).
    ///
    /// Note for callers: on WebGL, Application.OpenURL performs a browser location
    /// change. To survive iOS Safari's popup/navigation blocking it must be invoked
    /// synchronously from a user-gesture handler (e.g. a button's clicked event) —
    /// which is how the AuthPrompt / PeraConnectModal "Open in wallet app" buttons
    /// call into this class.
    /// </summary>
    public static class WalletDeepLink
    {
        /// <summary>Open the Pera wallet app with a WalletConnect URI.</summary>
        public static void OpenPera(string wcUri)
        {
            if (!IsMobilePlatform) return;
            string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(wcUri);
    #if UNITY_WEBGL && !UNITY_EDITOR
            // Mobile browser: prefer Pera's https universal link — it opens the app when
            // installed and falls back to a Pera web page otherwise, whereas an
            // unregistered custom scheme fails silently in the browser. This is the same
            // link form Pera encodes in its own QR codes.
            Application.OpenURL($"https://perawallet.app/qr/perawallet-wc/?uri={encoded}");
    #else
            // Native platforms: Pera's registered custom scheme.
            Application.OpenURL($"perawallet-wc://wc?uri={encoded}");
    #endif
        }

        /// <summary>Open the Defly wallet app with a WalletConnect URI.</summary>
        public static void OpenDefly(string wcUri)
        {
            if (!IsMobilePlatform) return;
            string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(wcUri);
    #if UNITY_WEBGL && !UNITY_EDITOR
            // Defly has no documented https universal link. Mirror the official
            // defly-connect JS SDK (blockshake-io/defly-connect, deflyWalletUtils.ts):
            //   Android browser → open the raw wc: URI (Android intent resolution
            //                     routes it to the installed WC-capable wallet);
            //   iOS browser     → Defly's registered "defly-wc://wc?uri=" scheme.
            bool isAndroidBrowser = false;
            try { isAndroidBrowser = SystemInfo.operatingSystem.IndexOf("Android", System.StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { /* fall through to the iOS-style scheme link */ }

            if (isAndroidBrowser)
                Application.OpenURL(wcUri);
            else
                Application.OpenURL($"defly-wc://wc?uri={encoded}");
    #else
            Application.OpenURL($"algorand-wc://wc?uri={encoded}");
    #endif
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

        /// <summary>
        /// True on iOS and Android, and on WebGL when the browser reports a mobile
        /// device (see BmIsMobileBrowser in BlockmakerWalletBridge.jslib).
        /// </summary>
        public static bool IsMobilePlatform
        {
            get
            {
                if (Application.platform == RuntimePlatform.IPhonePlayer ||
                    Application.platform == RuntimePlatform.Android)
                    return true;

    #if UNITY_WEBGL && !UNITY_EDITOR
                try { return BlockmakerWalletBridge.BmIsMobileBrowser() != 0; }
                catch { return false; }
    #else
                return false;
    #endif
            }
        }

        /// <summary>Open a generic WalletConnect URI — lets the OS choose the handler.</summary>
        public static void OpenGeneric(string wcUri)
        {
            if (!IsMobilePlatform || string.IsNullOrEmpty(wcUri)) return;
            Application.OpenURL(wcUri);
        }
    }

}
