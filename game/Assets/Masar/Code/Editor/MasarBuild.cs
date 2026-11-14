using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Masar.EditorTools
{
    /// <summary>
    /// Command-line builds:
    ///   Unity.exe -batchmode -quit -projectPath game -executeMethod Masar.EditorTools.MasarBuild.BuildIOSXcode [-development] [-buildNumber N]
    ///   Unity.exe -batchmode -quit -projectPath game -executeMethod Masar.EditorTools.MasarBuild.BuildWindows
    /// The iOS build writes an Xcode project to build/ios-xcode (relative to the repo root).
    /// It is compiled and signed on a macOS runner (see .github/workflows/ios-game.yml).
    /// </summary>
    public static class MasarBuild
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        public static void BuildIOSXcode()
        {
            var args = Environment.GetCommandLineArgs();
            bool dev = args.Contains("-development");
            string buildNumber = ArgValue(args, "-buildNumber") ?? "1";
            PlayerSettings.iOS.buildNumber = buildNumber;

            string outDir = Path.Combine(RepoRoot, "build", "ios-xcode");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            EditorUserBuildSettings.iOSXcodeBuildConfig = dev ? XcodeBuildConfig.Debug : XcodeBuildConfig.Release;

            var opts = new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = outDir,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = dev ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
            };
            Report(BuildPipeline.BuildPlayer(opts), outDir);
        }

        public static void BuildWindows()
        {
            string outDir = Path.Combine(RepoRoot, "build", "windows");
            var opts = new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = Path.Combine(outDir, "MASAR.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            Report(BuildPipeline.BuildPlayer(opts), outDir);
        }

        private static string[] Scenes() => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        private static string ArgValue(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Report(BuildReport report, string outDir)
        {
            var s = report.summary;
            Debug.Log($"[MASAR] Build {s.result}: {s.platform} -> {outDir} | {s.totalSize / 1048576f:F1} MB | {s.totalTime.TotalSeconds:F0}s | errors {s.totalErrors} warnings {s.totalWarnings}");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

#if UNITY_IOS
        /// <summary>
        /// iOS post-process: exposes the app's Documents folder in the Files app (for the
        /// performance CSV logs) and declares the game-controller support.
        /// </summary>
        [PostProcessBuild(100)]
        public static void OnPostprocessIOS(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            root.SetBoolean("UIFileSharingEnabled", true);
            root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            root.SetBoolean("GCSupportsControllerUserInteraction", true);
            var profiles = root.CreateArray("GCSupportedGameControllers");
            profiles.AddDict().SetString("ProfileName", "ExtendedGamepad");
            plist.WriteToFile(plistPath);
        }
#endif
    }
}
