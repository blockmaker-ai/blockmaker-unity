using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// Stores Blockmaker connection settings.
    /// Create one via Assets → Create → Blockmaker → Config
    /// and assign it to BlockmakerClient in the scene.
    ///
    /// Never commit your API key to source control —
    /// use a separate Config asset that lives outside your Assets/Scenes folder
    /// and is listed in .gitignore, or load it from environment at build time.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockmakerConfig", menuName = "Blockmaker/Config")]
    public class BlockmakerConfig : ScriptableObject
    {
        [Header("Server")]
        [Tooltip("Optional — your Blockmaker server URL. Leave empty to use the shared default.")]
        public string serverUrl = "";

        internal const string DefaultServerUrl = "https://blockmaker-production.up.railway.app";

        [Tooltip("Short unique ID for your game (e.g. 'myrace'). Used to namespace saved sessions so multiple Blockmaker games on the same device don't conflict. If left empty, falls back to Application.identifier.")]
        public string gameId = "";

        [Header("Auth")]
        [Tooltip("Your Blockmaker API key — sk_ prefix, 48 hex chars")]
        public string apiKey = "";

        [Header("WalletConnect")]
        [Tooltip("Optional — your own WalletConnect project ID from https://cloud.walletconnect.com. Leave empty to use the Blockmaker shared ID (works out of the box).")]
        public string walletConnectProjectId = "";

        internal const string DefaultWalletConnectProjectId = "dc1f45e68a8fb53fa03adb645365c9bd";

        [Header("Magic SDK (Email Wallet)")]
        [Tooltip("Magic publishable API key (pk_live_ prefix). Get one at https://dashboard.magic.link")]
        public string magicPublishableKey = "";

        [Tooltip("Enable Magic SDK for email login. Creates a client-side wallet — private key stays on the player's device.")]
        public bool enableMagicEmail = true;

        [Header("xChain EVM")]
        [Tooltip("Enable xChain Accounts — lets EVM wallet users sign Algorand transactions from their existing wallet.")]
        public bool enableEvmXChain = true;

        [Header("Branding")]
        [Tooltip("URL shown in wallet apps when players approve connections. Defaults to your server URL if empty.")]
        public string dAppUrl = "";

        [Tooltip("Icon URL shown in wallet apps. Leave empty for default.")]
        public string dAppIconUrl = "";

        [Header("Timeouts (seconds)")]
        public float defaultTimeoutSeconds  = 10f;
        public float longRequestTimeoutSeconds   = 20f;
        [Tooltip("Timeout for wallet API queries (e.g. NFT search). Not for signing — see walletSignTimeoutSeconds.")]
        public float walletTimeoutSeconds   = 30f;
        [Tooltip("Timeout for interactive wallet operations (QR scan, transaction approval). Set high because the player must interact with their wallet app.")]
        public float walletSignTimeoutSeconds = 120f;

        private void OnValidate()
        {
            // Server URL and API key are optional — shared defaults are used when empty.
            if (!string.IsNullOrEmpty(serverUrl) &&
                !serverUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) &&
                !serverUrl.StartsWith("http://localhost", System.StringComparison.OrdinalIgnoreCase) &&
                !serverUrl.StartsWith("http://127.0.0.1", System.StringComparison.OrdinalIgnoreCase))
                BlockmakerLog.Warning("[BlockmakerConfig] Server URL does not use HTTPS. Use HTTPS in production.");

            if (!string.IsNullOrEmpty(apiKey) && !apiKey.StartsWith("sk_"))
                BlockmakerLog.Warning("[BlockmakerConfig] API key should start with 'sk_'.");

            // WalletConnect Project ID is optional — falls back to Blockmaker's shared ID.
            // Magic SDK is WebGL-only. Email login via OTP works on all platforms.
        }
    }
}