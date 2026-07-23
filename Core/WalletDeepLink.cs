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
            if (!IsMobilePlatform || string.IsNullOrEmpty(wcUri)) return;

            // @perawallet/connect adds this hint before handing the pairing URI to
            // Pera. Keep our headless Unity modal wire-compatible with Pera's own
            // mobile connect button.
    #if UNITY_WEBGL && !UNITY_EDITOR
            // Mirror @perawallet/connect 1.5.x exactly:
            //   Android browser -> raw wc: URI (Android resolves the wallet handler)
            //   iOS browser     -> Pera's registered perawallet-wc: scheme
            //
            // The old https://perawallet.app/qr/... link can open Pera's website on
            // Android without handing the live pairing to the app, leaving the player
            // with no connection or follow-up sign-in request.
            bool isAndroidBrowser = false;
            try
            {
                isAndroidBrowser =
                    SystemInfo.operatingSystem.IndexOf(
                        "Android",
                        System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { /* fall through to Pera's iOS/custom-scheme link */ }

            Application.OpenURL(BuildPeraLaunchUrl(wcUri, isAndroidBrowser));
    #elif UNITY_ANDROID && !UNITY_EDITOR
            Application.OpenURL(BuildPeraLaunchUrl(wcUri, true));
    #else
            // Native iOS and other mobile platforms: Pera's registered custom scheme.
            Application.OpenURL(BuildPeraLaunchUrl(wcUri, false));
    #endif
        }

        private static string BuildPeraLaunchUrl(string wcUri, bool isAndroid)
        {
            string peraUri = AddPeraAlgorandHint(wcUri);
            if (isAndroid) return peraUri;

            string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(peraUri);
            return $"perawallet-wc://wc?uri={encoded}";
        }

        private static string AddPeraAlgorandHint(string wcUri)
        {
            if (wcUri.IndexOf("algorand=", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return wcUri;

            return wcUri + (wcUri.IndexOf('?') >= 0 ? "&" : "?") + "algorand=true";
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
                case "metamask":
                    OpenUniversal("https://metamask.app.link/wc?uri=", wcUri);
                    break;
                case "rainbow":
                    OpenUniversal("https://rnbwapp.com/wc?uri=", wcUri);
                    break;
                case "coinbase wallet":
                case "coinbase":
                    OpenUniversal("https://go.cb-w.com/wc?uri=", wcUri);
                    break;
                case "trust wallet":
                case "trust":
                    OpenUniversal("https://link.trustwallet.com/wc?uri=", wcUri);
                    break;
                default:
                    // Android can route a raw WalletConnect URI to any registered
                    // wallet. On iOS this may show the app chooser or no-op, but is
                    // still a better fallback than a button that does nothing.
                    OpenGeneric(wcUri);
                    break;
            }
        }

        private static void OpenUniversal(string prefix, string wcUri)
        {
            if (!IsMobilePlatform || string.IsNullOrEmpty(wcUri)) return;
            string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(wcUri);
            Application.OpenURL(prefix + encoded);
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
