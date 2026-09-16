using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker.Editor
{
    public static class BlockmakerSetup
    {
        [MenuItem("Blockmaker/Setup Wallet Demo")]
        public static void SetupWalletDemo()
        {
            if (UnityEngine.Object.FindFirstObjectByType<BlockmakerWalletDemo>() != null) {
                Debug.Log("A Blockmaker Wallet Demo is already in this scene. Configure its public Game ID in the Inspector."); return;
            }
            if (UnityEngine.Object.FindFirstObjectByType<BlockmakerWalletPackageWebGL>() != null)
                throw new InvalidOperationException("This scene already contains a wallet receiver. Use that integration instead of adding a second manager.");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BlockmakerSetup).Assembly);
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "Blockmaker Wallet Panel";
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(package.assetPath + "/Runtime/WalletDemo.tss");
            if (panel.themeStyleSheet == null) throw new InvalidOperationException("The wallet demo theme could not be loaded. Reimport the package.");
            string asset = AssetDatabase.GenerateUniqueAssetPath("Assets/BlockmakerWalletPanel.asset");
            AssetDatabase.CreateAsset(panel, asset);
            var go = new GameObject("Blockmaker Wallet Demo");
            Undo.RegisterCreatedObjectUndo(go, "Create Blockmaker Wallet Demo");
            var document = go.AddComponent<UIDocument>(); document.panelSettings = panel;
            var demo = go.AddComponent<BlockmakerWalletDemo>(); demo.Document = document;
            if (Camera.main == null) {
                var camera = new GameObject("Wallet Demo Camera");
                Undo.RegisterCreatedObjectUndo(camera, "Create Wallet Demo Camera");
                camera.tag = "MainCamera";
                var component = camera.AddComponent<Camera>(); component.clearFlags = CameraClearFlags.SolidColor;
                component.backgroundColor = new Color(0.08f, 0.11f, 0.17f);
            }
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Wallet demo created. Set Game ID, optional public email client ID and branding in the Inspector. Use Blockmaker > Install Fullscreen Web Template before a browser test.");
        }

        [MenuItem("Blockmaker/Install Fullscreen Web Template")]
        public static void InstallFullscreenWebTemplate()
        {
            string source = Path.Combine(BlockmakerWebGLBuild.PackageRoot, "Templates~/Blockmaker/index.html");
            string directory = "Assets/WebGLTemplates/Blockmaker";
            string target = directory + "/index.html";
            if (File.Exists(target) && File.ReadAllText(target) != File.ReadAllText(source))
                throw new InvalidOperationException("The Blockmaker WebGL template has local changes. Preserve it and merge the new template manually; nothing was overwritten.");
            Directory.CreateDirectory(directory);
            if (!File.Exists(target)) File.Copy(source, target);
            AssetDatabase.Refresh();
            PlayerSettings.WebGL.template = "PROJECT:Blockmaker";
            Debug.Log("Blockmaker fullscreen WebGL template selected. It fullscreens the document root so provider forms can receive input.");
        }
    }
}
