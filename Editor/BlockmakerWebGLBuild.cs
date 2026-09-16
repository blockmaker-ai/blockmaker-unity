using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Blockmaker.Editor
{
    /// <summary>Adds the package's exact browser payload without modifying Assets.</summary>
    public sealed class BlockmakerWebGLBuild : BuildPlayerProcessor
    {
        public const string CanonicalPackageId = "unity-webgl-package-sha256-__-tqLYjkYy9Vb6vY_mx5BlxdUun1GRW73T0PCN2I3U";
        public override int callbackOrder => 0;
        private static readonly Dictionary<string, string> Paths = new Dictionary<string, string> {
            { "BlockmakerClient.cs", "Runtime/BlockmakerClient.cs" },
            { "BlockmakerWalletPackageWebGL.cs", "Runtime/BlockmakerWalletPackageWebGL.cs" },
            { "BlockmakerUnityPresentation.cs", "Runtime/BlockmakerUnityPresentation.cs" },
            { "BlockmakerWalletPackageWebGL.jslib", "Plugins/WebGL/BlockmakerWalletPackageWebGL.jslib" },
            { "blockmaker.js", "Browser~/blockmaker.js" },
            { "blockmaker-unity-webgl-wallet-host.mjs", "Browser~/blockmaker-unity-webgl-wallet-host.mjs" },
            { "blockmaker-txnlab-wallet.mjs", "Browser~/blockmaker-txnlab-wallet.mjs" },
            { "blockmaker-txnlab-wallet.d.mts", "Browser~/blockmaker-txnlab-wallet.d.mts" },
            { "blockmaker-txnlab-wallet.NOTICES.txt", "Browser~/blockmaker-txnlab-wallet.NOTICES.txt" },
        };
        [Serializable] private sealed class Manifest { public string packageId; public Member[] members; }
        [Serializable] private sealed class Member { public string file, integrity; public int rawBytes; }

        public static string PackageRoot {
            get {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BlockmakerWebGLBuild).Assembly);
                if (package == null) throw new BuildFailedException("Install Blockmaker with Unity Package Manager before building.");
                return package.resolvedPath;
            }
        }

        public static void VerifyPackage(string root)
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(root, "package-manifest.json")));
            if (manifest == null || manifest.packageId != CanonicalPackageId || manifest.members == null || manifest.members.Length != Paths.Count)
                throw new BuildFailedException("Blockmaker's package is incomplete. Reinstall the pinned package version.");
            var seen = new HashSet<string>();
            foreach (var member in manifest.members) {
                if (member == null || string.IsNullOrEmpty(member.file) || !Paths.TryGetValue(member.file, out var path) || !seen.Add(member.file))
                    throw new BuildFailedException("Blockmaker's package member list is invalid.");
                var bytes = File.ReadAllBytes(Path.Combine(root, path));
                string integrity;
                using (var hash = SHA256.Create()) integrity = "sha256-" + Convert.ToBase64String(hash.ComputeHash(bytes));
                if (bytes.Length != member.rawBytes || integrity != member.integrity)
                    throw new BuildFailedException("Blockmaker package verification failed for " + member.file + ". Reinstall the complete package; do not patch individual vendor files.");
            }
        }

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            if (context.BuildPlayerOptions.target != BuildTarget.WebGL)
                throw new BuildFailedException("Blockmaker Wallets 2 supports Unity WebGL only. Choose Web in Build Profiles. Native wallet exports need a separate integration.");
            string root = PackageRoot;
            VerifyPackage(root);
            foreach (var pair in Paths) {
                if (!pair.Value.StartsWith("Browser~/", StringComparison.Ordinal)) continue;
                string destination = "Blockmaker/" + pair.Key;
                if (File.Exists(Path.Combine(Application.streamingAssetsPath, destination)))
                    throw new BuildFailedException("A previous Blockmaker browser file remains in Assets/StreamingAssets: " + pair.Key + ". Follow the migration guide; the package will not overwrite it.");
                context.AddAdditionalPathToStreamingAssets(Path.Combine(root, pair.Value), destination);
            }
        }

        [MenuItem("Blockmaker/Verify Installed Package")]
        public static void VerifyInstalledPackage()
        {
            VerifyPackage(PackageRoot);
            Debug.Log("Blockmaker Wallets: all nine canonical package files verified.");
        }
    }
}
