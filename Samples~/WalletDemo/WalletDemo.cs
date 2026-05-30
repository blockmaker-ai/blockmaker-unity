using UnityEngine;
using UnityEngine.UIElements;
using Blockmaker;

/// <summary>
/// Sample scene script — sets up the full wallet system at runtime.
/// Just add this to an empty GameObject and hit Play.
///
/// This is a demo — for production, use Blockmaker > Setup Scene instead.
/// </summary>
public class WalletDemo : MonoBehaviour
{
    private void Start()
    {
        // ── 1. Config ──────────────────────────────────────────────────────
        var config = ScriptableObject.CreateInstance<BlockmakerConfig>();

        // ── 2. BlockmakerAuth ──────────────────────────────────────────────
        var authGo = new GameObject("_Blockmaker");
        DontDestroyOnLoad(authGo);
        var auth = authGo.AddComponent<BlockmakerAuth>();
        auth.blockmakerConfig = config;

        // ── 3. Auth UI (the wallet selection modal) ────────────────────────
        var authUiGo = new GameObject("_AuthUI");
        DontDestroyOnLoad(authUiGo);

        var uiDoc = authUiGo.AddComponent<UIDocument>();
        uiDoc.sortingOrder = 100;

        // Create panel settings at runtime
        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        uiDoc.panelSettings = panelSettings;

        // Load the auth prompt UI from the package
        var authPromptAsset = Resources.Load<VisualTreeAsset>("AuthPrompt");
        if (authPromptAsset == null)
        {
            // Try loading from package path
            #if UNITY_EDITOR
            authPromptAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.blockmaker.sdk/UI/AuthPrompt.uxml");
            #endif
        }

        if (authPromptAsset != null)
            uiDoc.visualTreeAsset = authPromptAsset;

        var authPrompt = authUiGo.AddComponent<AuthPromptController>();

        // Load the QR modal
        #if UNITY_EDITOR
        authPrompt.peraConnectAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Packages/com.blockmaker.sdk/UI/PeraConnectModal.uxml");
        #endif

        // ── 4. Connect button ──────────────────────────────────────────────
        var btnGo = new GameObject("_ConnectButton");
        DontDestroyOnLoad(btnGo);
        var btnDoc = btnGo.AddComponent<UIDocument>();
        btnDoc.sortingOrder = 200;
        btnDoc.panelSettings = panelSettings;
        btnGo.AddComponent<BlockmakerConnectButton>();

        // ── 5. Listen for wallet connection ─────────────────────────────────
        BlockmakerAuth.OnIdentityChanged += identity =>
        {
            if (identity.HasWallet)
                Debug.Log($"[WalletDemo] Connected: {identity.ProviderName} — {identity.Address}");
            else
                Debug.Log("[WalletDemo] Disconnected — guest mode");
        };

        Debug.Log("[WalletDemo] Wallet system ready. Click 'Connect Wallet' to start.");
    }
}
