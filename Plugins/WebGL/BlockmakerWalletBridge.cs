using System.Runtime.InteropServices;
using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// C# declarations for BlockmakerWalletBridge.jslib.
    ///
    /// Primary entry point for wallet connection:
    ///   ConnectWalletQR — generates a QR code inside Unity's own UI using
    ///                     WalletConnect v2 directly (works with Pera, Defly,
    ///                     and any WC v2 Algorand wallet).
    ///
    /// Requires a free WalletConnect Project ID from https://cloud.walletconnect.com
    /// Set it on the BlockmakerAuth component in the Inspector.
    /// </summary>
    public static class BlockmakerWalletBridge
    {
    #if UNITY_WEBGL && !UNITY_EDITOR

        /// <summary>
        /// Starts a WalletConnect v2 session and generates a QR code.
        ///
        /// Callbacks (all called via SendMessage on gameObjectName):
        ///   qrCallback      — "Provider|wc:uri...|base64PNG"  (QR ready to display)
        ///   successCallback — "Provider:AlgorandAddress"       (user approved)
        ///   errorCallback   — "error message"
        /// </summary>
        [DllImport("__Internal")]
        public static extern void ConnectWalletQR(
            string projectId,
            string walletHint,
            string gameObjectName,
            string qrCallback,
            string successCallback,
            string errorCallback);

        /// <summary>Cancel an in-progress ConnectWalletQR without firing the error callback.</summary>
        [DllImport("__Internal")]
        public static extern void CancelWalletQR();

        [DllImport("__Internal")]
        public static extern void Disconnect(string provider);

        [DllImport("__Internal")]
        public static extern void SignTransaction(
            string provider,
            string txnBase64,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void SignGroupTransaction(
            string provider,
            string txnsJson,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void TryReconnect(
            string provider,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        // ── Magic SDK ──────────────────────────────────────────────────────────────

        [DllImport("__Internal")]
        public static extern void MagicLoginWithEmail(
            string apiKey,
            string email,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void MagicSignTransaction(
            string txnBase64,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void MagicSignGroupTransaction(
            string txnsJson,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void MagicLogout();

        [DllImport("__Internal")]
        public static extern void MagicTryRestore(
            string apiKey,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        // ── xChain EVM ─────────────────────────────────────────────────────────────

        [DllImport("__Internal")]
        public static extern void EvmConnect(
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void EvmTryRestore(
            string expectedEvmAddress,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void EvmSignTransaction(
            string txnBase64,
            string evmAddress,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void EvmDisconnect();

        // ── Fullscreen management ─────────────────────────────────────────────────

        [DllImport("__Internal")]
        public static extern int IsFullscreen();

        [DllImport("__Internal")]
        public static extern void ExitFullscreen();

        [DllImport("__Internal")]
        public static extern void RequestFullscreen();

    #else

        public static void ConnectWalletQR(string projectId, string walletHint, string go, string qr, string s, string e)
            => BlockmakerLog.Warning($"[BlockmakerWalletBridge] ConnectWalletQR({walletHint}) — not in WebGL. Set testWalletAddress on BlockmakerAuth to test in the Editor.");

        public static void CancelWalletQR()
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] CancelWalletQR — not in WebGL.");

        public static void Disconnect(string p)
            => BlockmakerLog.Warning($"[BlockmakerWalletBridge] Disconnect({p}) — not in WebGL.");

        public static void SignTransaction(string p, string txn, string go, string s, string e)
            => BlockmakerLog.Warning($"[BlockmakerWalletBridge] SignTransaction({p}) — not in WebGL.");

        public static void SignGroupTransaction(string p, string txnsJson, string go, string s, string e)
            => BlockmakerLog.Warning($"[BlockmakerWalletBridge] SignGroupTransaction({p}) — not in WebGL.");

        public static void TryReconnect(string p, string go, string s, string e)
            => BlockmakerLog.Warning($"[BlockmakerWalletBridge] TryReconnect({p}) — not in WebGL.");

        // ── Magic SDK stubs ────────────────────────────────────────────────────────

        public static void MagicLoginWithEmail(string key, string email, string go, string s, string e)
            => BlockmakerLog.Warning($"[BlockmakerWalletBridge] MagicLoginWithEmail({email}) — not in WebGL.");

        public static void MagicSignTransaction(string txn, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] MagicSignTransaction — not in WebGL.");

        public static void MagicSignGroupTransaction(string txnsJson, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] MagicSignGroupTransaction — not in WebGL.");

        public static void MagicLogout()
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] MagicLogout — not in WebGL.");

        public static void MagicTryRestore(string key, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] MagicTryRestore — not in WebGL.");

        // ── xChain EVM stubs ───────────────────────────────────────────────────────

        public static void EvmTryRestore(string evm, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmTryRestore — not in WebGL.");

        public static void EvmConnect(string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmConnect — not in WebGL. Use ReownWalletConnector for native.");

        public static void EvmSignTransaction(string txn, string evm, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmSignTransaction — not in WebGL.");

        public static void EvmDisconnect()
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmDisconnect — not in WebGL.");

        // ── Fullscreen stubs ──────────────────────────────────────────────────────

        public static int IsFullscreen() => 0;
        public static void ExitFullscreen() { }
        public static void RequestFullscreen() { }

    #endif
    }

}