#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Drop this file into your Unity project's Assets folder.
/// It will install the Blockmaker SDK and its dependencies automatically.
/// You can delete this file after installation.
/// </summary>
[InitializeOnLoad]
public static class BlockmakerInstaller
{
    static BlockmakerInstaller()
    {
        var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (!File.Exists(manifestPath)) return;

        var content = File.ReadAllText(manifestPath);
        if (content.Contains("com.blockmaker.sdk")) return; // Already installed

        EditorApplication.delayCall += () =>
        {
            if (EditorUtility.DisplayDialog(
                "Install Blockmaker SDK",
                "This will install the Blockmaker wallet SDK and the Reown dependency.\n\nYour project will reload after installation.",
                "Install",
                "Cancel"))
            {
                Install(manifestPath);
            }
        };
    }

    [MenuItem("Blockmaker/Install SDK")]
    public static void InstallFromMenu()
    {
        var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            EditorUtility.DisplayDialog("Error", "Could not find Packages/manifest.json", "OK");
            return;
        }

        var content = File.ReadAllText(manifestPath);
        if (content.Contains("com.blockmaker.sdk"))
        {
            EditorUtility.DisplayDialog("Blockmaker SDK", "Already installed!", "OK");
            return;
        }

        Install(manifestPath);
    }

    static void Install(string manifestPath)
    {
        var content = File.ReadAllText(manifestPath);

        // Add scoped registry if not present
        if (!content.Contains("package.openupm.com"))
        {
            var lastBrace = content.LastIndexOf('}');
            var registry = @",
  ""scopedRegistries"": [
    {
      ""name"": ""OpenUPM"",
      ""url"": ""https://package.openupm.com"",
      ""scopes"": [""com.reown"", ""com.nethereum"", ""com.cysharp""]
    }
  ]
}";
            content = content.Substring(0, lastBrace) + registry;
        }

        // Add dependencies
        var depsIdx = content.IndexOf("\"dependencies\"");
        if (depsIdx >= 0)
        {
            var braceIdx = content.IndexOf('{', depsIdx);
            if (braceIdx >= 0)
            {
                var insert = "\n    \"com.blockmaker.sdk\": \"https://github.com/blockmaker-ai/blockmaker-unity.git\",\n    \"com.nethereum.unity\": \"4.19.2\",\n    \"com.reown.sign.nethereum\": \"1.6.0\",\n    \"com.reown.sign.unity\": \"1.6.0\",";
                content = content.Substring(0, braceIdx + 1) + insert + content.Substring(braceIdx + 1);
            }
        }

        File.WriteAllText(manifestPath, content);
        Debug.Log("[Blockmaker] SDK installed! Unity is resolving packages...");

        UnityEditor.PackageManager.Client.Resolve();
    }
}
#endif
