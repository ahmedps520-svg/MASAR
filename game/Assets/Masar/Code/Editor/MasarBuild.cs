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
            MasarProjectSetup.Configure();        // graphics tiers (incl. Max), player settings
            string buildNumber = ArgValue(args, "-buildNumber") ?? "1";
            PlayerSettings.iOS.buildNumber = buildNumber;
            MasarProjectSetup.ConfigureIcons();   // App Store Connect rejects uploads without the 1024 px icon
            WriteBuildInfo(buildNumber);

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
            MasarProjectSetup.Configure();
            WriteBuildInfo("win-" + DateTime.UtcNow.ToString("yyyyMMddHHmm"));
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

        /// <summary>Writes StreamingAssets/build_info.json (build number, commit, date), read by diagnostics reports.</summary>
        private static void WriteBuildInfo(string buildNumber)
        {
            string commit = "unknown";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
                { WorkingDirectory = RepoRoot, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi)) { commit = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(5000); }
            }
            catch { }
            string dir = Path.Combine(Application.dataPath, "StreamingAssets");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "build_info.json"),
                $"{{\"build\":\"{buildNumber}\",\"commit\":\"{commit}\",\"date\":\"{DateTime.UtcNow:o}\"}}");
            AssetDatabase.Refresh();
        }

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
            // The wheel bridge streams the racing wheel from a PC over the local network
            root.SetString("NSLocalNetworkUsageDescription",
                "MASAR receives your racing wheel from the MASAR Wheel Bridge running on a computer on this network, and sends force feedback back to it.");
            var profiles = root.CreateArray("GCSupportedGameControllers");
            profiles.AddDict().SetString("ProfileName", "ExtendedGamepad");
            plist.WriteToFile(plistPath);
        }
#endif
    }
}
