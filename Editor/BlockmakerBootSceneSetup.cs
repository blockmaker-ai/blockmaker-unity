#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Blockmaker;

/// <summary>
/// Editor utility: Blockmaker > Setup Boot Scene
/// Configures the currently open scene as a Blockmaker Boot scene:
///   1. Removes any legacy uGUI Canvases tagged "BlockmakerUI" (optional prompt)
///   2. Ensures a _Blockmaker GameObject exists with BlockmakerAuth + BlockmakerClient
///   3. Creates _AuthUI with UIDocument (Sort Order 100) + BlockmakerAuthUI
///   4. Ensures an EventSystem exists
/// Run this once after opening the Boot scene. It's idempotent — safe to run again.
/// </summary>
public static class BlockmakerBootSceneSetup
{
    private const string MenuPath = "Blockmaker/Setup Boot Scene";

    [MenuItem(MenuPath)]
    public static void RunSetup()
    {
        bool dirty = false;

        // ── 1. _Blockmaker ──────────────────────────────────────────────────
        var blockmakerGo = GameObject.Find("_Blockmaker");
        if (blockmakerGo == null)
        {
            blockmakerGo = new GameObject("_Blockmaker");
            Debug.Log("[BlockmakerSetup] Created _Blockmaker.");
            dirty = true;
        }

        if (blockmakerGo.GetComponent<BlockmakerAuth>() == null)
        {
            blockmakerGo.AddComponent<BlockmakerAuth>();
            Debug.Log("[BlockmakerSetup] Added BlockmakerAuth.");
            dirty = true;
        }

        if (blockmakerGo.GetComponent<BlockmakerClient>() == null)
        {
            blockmakerGo.AddComponent<BlockmakerClient>();
            Debug.Log("[BlockmakerSetup] Added BlockmakerClient.");
            dirty = true;
        }

        // ── 2. _AuthUI ──────────────────────────────────────────────────────
        var authUiGo = GameObject.Find("_AuthUI");
        if (authUiGo == null)
        {
            authUiGo = new GameObject("_AuthUI");
            Debug.Log("[BlockmakerSetup] Created _AuthUI.");
            dirty = true;
        }

        var uiDoc = authUiGo.GetComponent<UIDocument>();
        if (uiDoc == null)
        {
            uiDoc = authUiGo.AddComponent<UIDocument>();
            uiDoc.sortingOrder = 100;
            Debug.Log("[BlockmakerSetup] Added UIDocument (Sort Order 100).");
            dirty = true;
        }

        if (authUiGo.GetComponent<BlockmakerAuthUI>() == null)
        {
            authUiGo.AddComponent<BlockmakerAuthUI>();
            Debug.Log("[BlockmakerSetup] Added BlockmakerAuthUI.");
            dirty = true;
        }

        // ── 3. EventSystem ──────────────────────────────────────────────────
        var eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Debug.Log("[BlockmakerSetup] Created EventSystem.");
            dirty = true;
        }

        // ── 4. Warn about legacy uGUI objects ───────────────────────────────
        var legacyNames = new[] { "LoginScreenUI", "OTPScreenUI", "WalletUpgradePrompt", "BootManager" };
        foreach (var name in legacyNames)
        {
            var go = GameObject.Find(name);
            if (go != null)
                Debug.LogWarning($"[BlockmakerSetup] Legacy object '{name}' still in scene — consider removing it.");
        }

        if (dirty)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[BlockmakerSetup] Boot scene setup complete. Save the scene to persist changes.");
        }
        else
        {
            Debug.Log("[BlockmakerSetup] Boot scene already configured — nothing to do.");
        }

        // Ping the AuthUI so it's easy to find in the hierarchy
        EditorGUIUtility.PingObject(authUiGo);
    }

    [MenuItem(MenuPath, validate = true)]
    public static bool RunSetupValidate()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid();
    }

    [MenuItem("Blockmaker/Create SDK Prefab")]
    public static void CreatePrefab()
    {
        var root = new GameObject("Blockmaker");
        root.AddComponent<BlockmakerAuth>();
        root.AddComponent<BlockmakerClient>();

        string dir = "Assets/Blockmaker";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "Blockmaker");

        string path = $"{dir}/Blockmaker.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[BlockmakerSetup] SDK prefab saved to {path}. Drag it into any scene to add Blockmaker.");
        Debug.Log("[BlockmakerSetup] AuthUI (UIDocument + BlockmakerAuthUI) must be added as a separate root-level GameObject — DontDestroyOnLoad does not work on child objects.");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }
}
#endif
