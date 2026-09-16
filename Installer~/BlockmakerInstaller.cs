#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>Optional drop-in installer for the same pinned UPM release.</summary>
[InitializeOnLoad]
public static class BlockmakerInstaller
{
    private const string PackageUrl = "https://github.com/blockmaker-ai/blockmaker-unity.git#v2.0.0";
    private static AddRequest request;
    static BlockmakerInstaller()
    {
        // Offer once per Editor session. A cancelled dialog never repeats on reload.
        if (SessionState.GetBool("BlockmakerInstaller.offered", false)) return;
        SessionState.SetBool("BlockmakerInstaller.offered", true);
        EditorApplication.delayCall += () => {
            if (HasPackage()) return;
            if (EditorUtility.DisplayDialog("Install Blockmaker Wallets", "Install the current Pera and optional email wallet package for Unity WebGL?", "Install", "Cancel")) Install();
        };
    }
    private static bool HasPackage()
    {
        var manifest = Path.Combine(Application.dataPath, "../Packages/manifest.json");
        return File.Exists(manifest) && File.ReadAllText(manifest).Contains("\"com.blockmaker.sdk\"");
    }
    [MenuItem("Blockmaker/Install or Update Wallet Package")]
    public static void Install()
    {
        if (request != null) return;
        if (Directory.GetFiles(Application.dataPath, "BlockmakerWalletPackageWebGL.cs", SearchOption.AllDirectories).Length > 0 ||
            Directory.GetFiles(Application.dataPath, "BlockmakerAuth.cs", SearchOption.AllDirectories).Length > 0) {
            EditorUtility.DisplayDialog("Existing Blockmaker files", "Blockmaker scripts already exist in Assets. Follow the README migration steps before installing the package. Your files were not changed.", "OK"); return;
        }
        request = Client.Add(PackageUrl);
        EditorApplication.update += Progress;
    }
    private static void Progress()
    {
        if (request == null || !request.IsCompleted) return;
        EditorApplication.update -= Progress;
        if (request.Status == StatusCode.Success)
            Debug.Log("Blockmaker Wallets installed. Use Blockmaker > Setup Wallet Demo. You can delete this installer file.");
        else Debug.LogError("Blockmaker installation failed: " + request.Error.message);
        request = null;
    }
}
#endif
