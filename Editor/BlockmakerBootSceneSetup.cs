#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{

/// <summary>
/// Editor utility: Blockmaker > Setup Scene
/// Creates all required GameObjects with components and assets pre-wired.
/// Run this once in any scene. It's idempotent — safe to run again.
/// </summary>
public static class BlockmakerBootSceneSetup
{
    private const string PackagePath = "Packages/com.blockmaker.sdk";

    [MenuItem("Blockmaker/Setup Scene")]
    public static void RunSetup()
    {
        // ── 1. Find or create BlockmakerConfig ─────────────────────────────
        var config = FindOrCreateConfig();

        // ── 2. _Blockmaker GameObject ──────────────────────────────────────
        var blockmakerGo = GameObject.Find("_Blockmaker");
        if (blockmakerGo == null)
            blockmakerGo = new GameObject("_Blockmaker");

        var auth = blockmakerGo.GetComponent<BlockmakerAuth>();
        if (auth == null)
            auth = blockmakerGo.AddComponent<BlockmakerAuth>();

        if (auth.blockmakerConfig == null && config != null)
            auth.blockmakerConfig = config;

        // ── 3. _AuthUI GameObject ──────────────────────────────────────────
        var authUiGo = GameObject.Find("_AuthUI");
        if (authUiGo == null)
            authUiGo = new GameObject("_AuthUI");

        // Panel Settings
        var panelSettings = FindOrCreatePanelSettings();

        // UIDocument
        var uiDoc = authUiGo.GetComponent<UIDocument>();
        if (uiDoc == null)
            uiDoc = authUiGo.AddComponent<UIDocument>();

        uiDoc.sortingOrder = 100;

        if (uiDoc.panelSettings == null && panelSettings != null)
            uiDoc.panelSettings = panelSettings;

        // Assign AuthPrompt UXML as the source asset (the compact modal)
        var authPromptUxml = LoadAsset<VisualTreeAsset>("UI/AuthPrompt.uxml");
        if (authPromptUxml != null && uiDoc.visualTreeAsset == null)
            uiDoc.visualTreeAsset = authPromptUxml;

        // AuthPromptController component
        var authPrompt = authUiGo.GetComponent<AuthPromptController>();
        if (authPrompt == null)
            authPrompt = authUiGo.AddComponent<AuthPromptController>();

        // Wire up the Pera/Defly/X-Chain modal UXML
        if (authPrompt.peraConnectAsset == null)
            authPrompt.peraConnectAsset = LoadAsset<VisualTreeAsset>("UI/PeraConnectModal.uxml");

        // ── 4. _ConnectButton (example UI) ──────────────────────────────────
        var connectGo = GameObject.Find("_ConnectButton");
        if (connectGo == null)
        {
            connectGo = new GameObject("_ConnectButton");

            var btnDoc = connectGo.AddComponent<UIDocument>();
            btnDoc.sortingOrder = 200;
            if (btnDoc.panelSettings == null && panelSettings != null)
                btnDoc.panelSettings = panelSettings;

            connectGo.AddComponent<BlockmakerConnectButton>();
        }

        // ── 5. EventSystem ─────────────────────────────────────────────────
        var eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // ── Done ───────────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[Blockmaker] Scene setup complete. Save the scene and hit Play.");
        EditorGUIUtility.PingObject(authUiGo);
        Selection.activeGameObject = authUiGo;
    }

    static BlockmakerConfig FindOrCreateConfig()
    {
        // Check if one already exists in the project
        var guids = AssetDatabase.FindAssets("t:BlockmakerConfig");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<BlockmakerConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

        // Create one
        var config = ScriptableObject.CreateInstance<BlockmakerConfig>();
        string dir = "Assets";
        string path = $"{dir}/BlockmakerConfig.asset";
        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Blockmaker] Created config at {path}");
        return config;
    }

    static PanelSettings FindOrCreatePanelSettings()
    {
        // Check if one exists
        var guids = AssetDatabase.FindAssets("t:PanelSettings");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));

        // Create one
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        string path = "Assets/BlockmakerPanelSettings.asset";
        AssetDatabase.CreateAsset(ps, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Blockmaker] Created PanelSettings at {path}");
        return ps;
    }

    static T LoadAsset<T>(string relativePath) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>($"{PackagePath}/{relativePath}");
        if (asset == null)
            Debug.LogWarning($"[Blockmaker] Could not find {relativePath} in package.");
        return asset;
    }

    [MenuItem("Blockmaker/Setup Scene", validate = true)]
    public static bool RunSetupValidate()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid();
    }
}

} // namespace Blockmaker
#endif
