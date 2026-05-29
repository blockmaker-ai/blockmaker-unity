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
        [Tooltip("Base URL of your Blockmaker server e.g. https://your-server.up.railway.app")]
        public string serverUrl = "";

        [Tooltip("Short unique ID for your game (e.g. 'myrace'). Used to namespace saved sessions so multiple Blockmaker games on the same device don't conflict. If left empty, falls back to Application.identifier.")]
        public string gameId = "";

        [Header("Auth")]
        [Tooltip("Your Blockmaker API key — sk_ prefix, 48 hex chars")]
        public string apiKey = "";

        [Header("WalletConnect")]
        [Tooltip("Free project ID from https://cloud.walletconnect.com — required for Pera/Defly wallet QR connection.")]
        public string walletConnectProjectId = "";

        [Header("Magic SDK (Email Wallet)")]
        [Tooltip("Magic publishable API key (pk_live_ prefix). Get one at https://dashboard.magic.link")]
        public string magicPublishableKey = "";

        [Tooltip("Enable Magic SDK for email login. Creates a client-side wallet — private key stays on the player's device.")]
        public bool enableMagicEmail = true;

        [Header("xChain EVM")]
        [Tooltip("Enable xChain Accounts — lets EVM wallet users sign Algorand transactions from their existing wallet.")]
        public bool enableEvmXChain = true;

        [Header("Timeouts (seconds)")]
        public float defaultTimeoutSeconds  = 10f;
        public float rewardTimeoutSeconds   = 20f;
        [Tooltip("Timeout for wallet API queries (e.g. NFT search). Not for signing — see walletSignTimeoutSeconds.")]
        public float walletTimeoutSeconds   = 30f;
        [Tooltip("Timeout for interactive wallet operations (QR scan, transaction approval). Set high because the player must interact with their wallet app.")]
        public float walletSignTimeoutSeconds = 120f;

        [Header("Game-specific (optional)")]
        [Tooltip("Marketplace fee percentage deducted from each sale (e.g. 3 = 3%)")]
        public float marketplaceFeePercent = 3f;

        [Tooltip("Return mock marketplace data instead of hitting the server")]
        public bool marketplaceTestMode = false;

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(serverUrl))
                BlockmakerLog.Warning("[BlockmakerConfig] Server URL is empty — all API calls will fail. Set it to your server's base URL (e.g. https://your-server.up.railway.app).");
            else if (!serverUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) &&
                     !serverUrl.StartsWith("http://localhost", System.StringComparison.OrdinalIgnoreCase) &&
                     !serverUrl.StartsWith("http://127.0.0.1", System.StringComparison.OrdinalIgnoreCase))
                BlockmakerLog.Warning("[BlockmakerConfig] Server URL does not use HTTPS. All tokens and API keys will be sent in plaintext. Use HTTPS in production.");

            if (string.IsNullOrEmpty(apiKey))
                BlockmakerLog.Warning("[BlockmakerConfig] API key is empty — authenticated API calls will fail. Set your sk_ key from the dashboard.");
            else if (!apiKey.StartsWith("sk_"))
                BlockmakerLog.Warning("[BlockmakerConfig] API key should start with 'sk_'. Check your key at the dashboard.");

            if (string.IsNullOrEmpty(walletConnectProjectId))
                BlockmakerLog.Warning("[BlockmakerConfig] WalletConnect Project ID is empty — wallet QR connection will not work. Get a free ID at https://cloud.walletconnect.com");

            // Magic SDK is WebGL-only. Email login via OTP works on all platforms.
            // No warning needed — Magic is silently skipped on non-WebGL.
        }
    }
}