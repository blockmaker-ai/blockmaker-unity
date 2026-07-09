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

        // ── Pera official JS SDK (@perawallet/connect) — WebGL Pera path ──────────

        /// <summary>
        /// Connect via Pera's official browser SDK. Pera renders its OWN modal
        /// (QR on desktop, deep links on mobile) — no QR is sent back to Unity.
        /// successCallback receives the bare Algorand address; errorCallback a
        /// message ("PERA_CONNECT_CANCELLED" when the user closed Pera's modal).
        /// </summary>
        [DllImport("__Internal")]
        public static extern void PeraJsConnect(
            string gameObjectName,
            string successCallback,
            string errorCallback, string qrCb);

        /// <summary>
        /// Silently restore Pera's localStorage session on load.
        /// successCallback receives "Pera:address" (same shape as TryReconnect).
        /// </summary>
        [DllImport("__Internal")]
        public static extern void PeraJsReconnect(
            string gameObjectName,
            string successCallback,
            string errorCallback);

        /// <summary>1 when a live Pera JS session exists, 0 otherwise.</summary>
        [DllImport("__Internal")]
        public static extern int PeraJsHasSession();

        [DllImport("__Internal")]
        public static extern void PeraJsSignTransaction(
            string txnBase64,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void PeraJsSignGroupTransaction(
            string txnsJson,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void PeraJsDisconnect();

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
        // Zero-bundle path: the jslib is a thin EIP-1193 transport (EIP-6963
        // discovery + account requests + signing); all address derivation and
        // signed-transaction assembly happens in C# (XChainAddressDeriver).

        /// <summary>
        /// Collect EIP-6963 wallet announcements (~300ms window). The callback
        /// receives "rdns|name;rdns|name;…" — one entry per announced wallet;
        /// "" when only the legacy window.ethereum fallback exists; "!none"
        /// when no EVM provider is available at all.
        /// </summary>
        [DllImport("__Internal")]
        public static extern void EvmDiscoverWallets(
            string gameObjectName,
            string callback);

        /// <summary>
        /// Connect an EVM wallet via eth_requestAccounts. rdns selects a specific
        /// EIP-6963 wallet ("" = last-used → first announced → window.ethereum).
        /// successCallback receives the raw EVM address ("0x…") — the Algorand
        /// LogicSig address is derived in C#.
        /// </summary>
        [DllImport("__Internal")]
        public static extern void EvmConnect(
            string rdns,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        /// <summary>
        /// Silently re-attach the EVM wallet after a page reload (eth_accounts,
        /// no popup). successCallback receives the raw EVM address ("0x…").
        /// </summary>
        [DllImport("__Internal")]
        public static extern void EvmTryRestore(
            string expectedEvmAddress,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        /// <summary>
        /// Sign an arbitrary UTF-8 message with an EVM wallet (personal_sign /
        /// sign-in proof). On success the successCallback receives the 0x-hex
        /// signature; on error the errorCallback receives a message.
        /// </summary>
        [DllImport("__Internal")]
        public static extern void EvmSignPersonal(
            string message,
            string evmAddress,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        /// <summary>
        /// Sign C#-built EIP-712 typed data via eth_signTypedData_v4. The
        /// successCallback receives the raw 0x-hex signature; C# parses it
        /// (ParseEvmSignature) and assembles the LogicSig-signed transaction.
        /// </summary>
        [DllImport("__Internal")]
        public static extern void EvmSignTypedData(
            string evmAddress,
            string typedDataJson,
            string gameObjectName,
            string successCallback,
            string errorCallback);

        [DllImport("__Internal")]
        public static extern void EvmDisconnect();

        // ── Fullscreen management ─────────────────────────────────────────────────

        [DllImport("__Internal")]
        public static extern int IsFullscreen();

        /// <summary>
        /// 1 when the WebGL build is running in a mobile browser (phone/tablet,
        /// including iPadOS masquerading as desktop Safari), 0 otherwise.
        /// </summary>
        [DllImport("__Internal")]
        public static extern int BmIsMobileBrowser();

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

        // ── Pera official JS SDK stubs ─────────────────────────────────────────────

        public static void PeraJsConnect(string go, string s, string e, string qr)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] PeraJsConnect — not in WebGL. Pera uses the native WCv1 flow on this platform.");

        public static void PeraJsReconnect(string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] PeraJsReconnect — not in WebGL.");

        public static int PeraJsHasSession() => 0;

        public static void PeraJsSignTransaction(string txn, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] PeraJsSignTransaction — not in WebGL.");

        public static void PeraJsSignGroupTransaction(string txnsJson, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] PeraJsSignGroupTransaction — not in WebGL.");

        public static void PeraJsDisconnect() { }

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

        public static void EvmDiscoverWallets(string go, string cb)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmDiscoverWallets — not in WebGL.");

        public static void EvmTryRestore(string evm, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmTryRestore — not in WebGL.");

        public static void EvmConnect(string rdns, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmConnect — not in WebGL. Use ReownWalletConnector for native.");

        public static void EvmSignPersonal(string message, string evm, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmSignPersonal — not in WebGL.");

        public static void EvmSignTypedData(string evm, string typedDataJson, string go, string s, string e)
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmSignTypedData — not in WebGL.");

        public static void EvmDisconnect()
            => BlockmakerLog.Warning("[BlockmakerWalletBridge] EvmDisconnect — not in WebGL.");

        // ── Fullscreen stubs ──────────────────────────────────────────────────────

        public static int IsFullscreen() => 0;
        public static int BmIsMobileBrowser() => 0;
        public static void ExitFullscreen() { }
        public static void RequestFullscreen() { }

    #endif
    }

}