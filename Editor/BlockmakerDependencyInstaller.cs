#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Blockmaker
{

/// <summary>
/// Automatically checks if the Reown SDK is installed when the Blockmaker
/// SDK is first imported. If missing, offers to install it with one click.
/// </summary>
[InitializeOnLoad]
public static class BlockmakerDependencyInstaller
{
    private const string ReownPackage = "com.reown.sign.unity";
    private const string ReownVersion = "1.6.0";
    private const string RegistryName = "OpenUPM";
    private const string RegistryUrl = "https://package.openupm.com";
    private static readonly string[] RegistryScopes = { "com.reown", "com.nethereum", "com.cysharp" };
    private const string DismissedKey = "Blockmaker_ReownPromptDismissed";

    static BlockmakerDependencyInstaller()
    {
        if (SessionState.GetBool(DismissedKey, false)) return;
        if (IsReownInstalled()) return;

        EditorApplication.delayCall += ShowInstallPrompt;
    }

    static bool IsReownInstalled()
    {
        var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (!File.Exists(manifestPath)) return false;
        var content = File.ReadAllText(manifestPath);
        return content.Contains(ReownPackage);
    }

    static void ShowInstallPrompt()
    {
        var choice = EditorUtility.DisplayDialogComplex(
            "Blockmaker SDK — Install Dependency",
            "The Blockmaker SDK requires the Reown Sign Unity package for Defly and X-Chain wallet support.\n\nWould you like to install it now?",
            "Install Reown SDK",
            "Not now",
            "Don't ask again"
        );

        switch (choice)
        {
            case 0: // Install
                InstallReown();
                break;
            case 1: // Not now
                SessionState.SetBool(DismissedKey, true);
                break;
            case 2: // Don't ask again
                SessionState.SetBool(DismissedKey, true);
                break;
        }
    }

    [MenuItem("Blockmaker/Install Reown SDK")]
    public static void InstallReown()
    {
        var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Debug.LogError("[Blockmaker] Could not find Packages/manifest.json");
            return;
        }

        var content = File.ReadAllText(manifestPath);

        if (content.Contains(ReownPackage))
        {
            Debug.Log("[Blockmaker] Reown SDK is already installed.");
            return;
        }

        // Add scoped registry if not present
        if (!content.Contains(RegistryUrl))
        {
            var registryJson = $@",
  ""scopedRegistries"": [
    {{
      ""name"": ""{RegistryName}"",
      ""url"": ""{RegistryUrl}"",
      ""scopes"": [{string.Join(", ", RegistryScopes.Select(s => $"\"{s}\""))}]
    }}
  ]";

            // Insert before the final }
            var lastBrace = content.LastIndexOf('}');
            if (lastBrace >= 0)
                content = content.Substring(0, lastBrace) + registryJson + "\n}";
        }

        // Add the dependency
        var depsIndex = content.IndexOf("\"dependencies\"");
        if (depsIndex >= 0)
        {
            var firstBrace = content.IndexOf('{', depsIndex);
            if (firstBrace >= 0)
            {
                var insertPoint = firstBrace + 1;
                content = content.Substring(0, insertPoint) +
                    $"\n    \"{ReownPackage}\": \"{ReownVersion}\"," +
                    content.Substring(insertPoint);
            }
        }

        File.WriteAllText(manifestPath, content);
        Debug.Log("[Blockmaker] Reown SDK added to manifest.json. Unity will now resolve the package...");

        UnityEditor.PackageManager.Client.Resolve();
    }
}

}
#endif
