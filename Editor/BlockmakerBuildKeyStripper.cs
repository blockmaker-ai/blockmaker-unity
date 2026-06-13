using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Blockmaker
{
    /// <summary>
    /// Build safety: strips the developer API key out of every BlockmakerConfig
    /// during a player build so it NEVER ships inside a distributed game, then
    /// restores it afterwards so editor development keeps working.
    ///
    /// Keep your dev key in your local (gitignored) BlockmakerConfig for editor
    /// testing; shipped builds authenticate via player JWTs and don't need it.
    /// This makes it impossible to accidentally ship the key — no discipline
    /// required. Editor-only.
    /// </summary>
    public class BlockmakerBuildKeyStripper : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // Distinct from any game-local stripper so the two can coexist safely
        // (each captures/restores only its own SessionState).
        private const string StateKey = "blockmaker.sdk.strippedApiKeys";

        [System.Serializable] private class Entry   { public string path; public string key; }
        [System.Serializable] private class Entries { public List<Entry> items = new List<Entry>(); }

        public void OnPreprocessBuild(BuildReport report)
        {
            var saved = new Entries();
            foreach (var guid in AssetDatabase.FindAssets("t:BlockmakerConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cfg  = AssetDatabase.LoadAssetAtPath<BlockmakerConfig>(path);
                if (cfg == null || string.IsNullOrEmpty(cfg.apiKey)) continue;
                saved.items.Add(new Entry { path = path, key = cfg.apiKey });
                cfg.apiKey = "";
                EditorUtility.SetDirty(cfg);
            }

            if (saved.items.Count > 0)
            {
                AssetDatabase.SaveAssets();
                SessionState.SetString(StateKey, JsonUtility.ToJson(saved));
                Debug.LogWarning($"[Blockmaker] Stripped the dev apiKey from {saved.items.Count} BlockmakerConfig(s) for this build — it will not ship. Restored automatically after the build.");
            }
            else
            {
                SessionState.EraseString(StateKey);
            }
        }

        public void OnPostprocessBuild(BuildReport report) => RestoreKeys();

        // Safety net: if a build is cancelled/fails before OnPostprocessBuild runs,
        // restore on the next domain reload — but never mid-build (that would
        // re-bake the key into the player).
        [InitializeOnLoadMethod] private static void RestoreOnLoad()
        {
            if (!BuildPipeline.isBuildingPlayer) RestoreKeys();
        }

        private static void RestoreKeys()
        {
            var json = SessionState.GetString(StateKey, "");
            if (string.IsNullOrEmpty(json)) return;

            var saved = JsonUtility.FromJson<Entries>(json);
            if (saved?.items != null)
            {
                foreach (var e in saved.items)
                {
                    var cfg = AssetDatabase.LoadAssetAtPath<BlockmakerConfig>(e.path);
                    if (cfg != null && string.IsNullOrEmpty(cfg.apiKey))
                    {
                        cfg.apiKey = e.key;
                        EditorUtility.SetDirty(cfg);
                    }
                }
                AssetDatabase.SaveAssets();
            }
            SessionState.EraseString(StateKey);
        }
    }
}
