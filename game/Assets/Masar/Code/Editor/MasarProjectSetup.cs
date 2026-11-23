using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Masar.EditorTools
{
    /// <summary>
    /// One-shot, repeatable project configuration (run from the menu or with
    /// -executeMethod Masar.EditorTools.MasarProjectSetup.ConfigureAndBuildScenes).
    /// Keeps every important setting in code instead of hand-clicked binary settings.
    /// </summary>
    public static class MasarProjectSetup
    {
        public const string SettingsDir = "Assets/Masar/Settings";

        public struct Tier
        {
            public string Name;
            public float RenderScale;
            public float ShadowDistance;
            public int Cascades;
            public int ShadowRes;
            public bool SoftShadows;
            public bool Ssao;
            public bool Hdr;
            public int Msaa;
            public bool Stp;
            public int AdditionalLights;
            public float LodBias;
            public int TextureMipLimit;
            public bool GpuResidentDrawer;
            public bool AdditionalLightShadows;
        }

        // Starting points. Real values are tuned from measurements on the device (see PERFORMANCE_REPORT.md).
        public static readonly Tier[] Tiers =
        {
            new Tier { Name = "Low",    RenderScale = 0.60f, ShadowDistance = 60f,  Cascades = 1, ShadowRes = 1024, SoftShadows = false, Ssao = false, Hdr = false, Msaa = 1, Stp = false, AdditionalLights = 4,  LodBias = 0.6f, TextureMipLimit = 1, GpuResidentDrawer = false },
            new Tier { Name = "Medium", RenderScale = 0.72f, ShadowDistance = 90f,  Cascades = 2, ShadowRes = 2048, SoftShadows = true,  Ssao = false, Hdr = true,  Msaa = 1, Stp = false, AdditionalLights = 8, LodBias = 1.0f, TextureMipLimit = 0, GpuResidentDrawer = true },
            new Tier { Name = "High",   RenderScale = 0.82f, ShadowDistance = 130f, Cascades = 3, ShadowRes = 2048, SoftShadows = true,  Ssao = true,  Hdr = true,  Msaa = 1, Stp = true,  AdditionalLights = 16, LodBias = 1.5f, TextureMipLimit = 0, GpuResidentDrawer = true },
            new Tier { Name = "Ultra",  RenderScale = 1.00f, ShadowDistance = 180f, Cascades = 4, ShadowRes = 4096, SoftShadows = true,  Ssao = true,  Hdr = true,  Msaa = 1, Stp = true,  AdditionalLights = 32, LodBias = 2.0f, TextureMipLimit = 0, GpuResidentDrawer = true },
            // Added 2026-11-21: Ultra ran at ~90 fps on the iPad Pro M5 in the test block. Supersampled
            // (renders above native, then filters down), longest shadows, highest LOD bias.
            new Tier { Name = "Max",    RenderScale = 1.20f, ShadowDistance = 260f, Cascades = 4, ShadowRes = 4096, SoftShadows = true,  Ssao = true,  Hdr = true,  Msaa = 1, Stp = false, AdditionalLights = 32, LodBias = 3.0f, TextureMipLimit = 0, GpuResidentDrawer = true, AdditionalLightShadows = true },
        };

        [MenuItem("MASAR/Setup/Configure Project")]
        public static void Configure()
        {
            ConfigurePlayer();
            ConfigureRendering();
            AssetDatabase.SaveAssets();
            Debug.Log("[MASAR] Project configured.");
        }

        /// <summary>Command-line entry point: configure, then generate all content and scenes.</summary>
        public static void ConfigureAndBuildScenes()
        {
            Configure();
            TestBlockBuilder.BuildAll();
            ImportedCarBuilder.BuildAll();   // selectable cars + catalog (imported models are local only)
            City.OsmCityBuilder.BuildTahlia();   // real map from data/osm (OSM, ODbL)
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ player

        public const string IconPath = "Assets/Masar/Art/Icon/AppIcon.png";

        /// <summary>
        /// App icon (self-made, tools/make-icon.ps1) for every icon kind, including the 1024 px
        /// App Store icon that App Store Connect requires before it accepts an upload.
        /// </summary>
        public static void ConfigureIcons()
        {
            AssetDatabase.ImportAsset(IconPath);
            var imp = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (imp == null) { Debug.LogWarning($"[MASAR] App icon missing at {IconPath}. Run tools/make-icon.ps1"); return; }
            imp.textureType = TextureImporterType.Default;
            imp.alphaSource = TextureImporterAlphaSource.None;   // App Store icons must be opaque
            imp.mipmapEnabled = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.isReadable = true;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
            int n = 0;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                foreach (var icon in icons)
                    for (int layer = 0; layer < icon.maxLayerCount; layer++) { icon.SetTexture(tex, layer); n++; }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, icons);
            }
            Debug.Log($"[MASAR] App icon set ({n} iOS icon slots)");
            ConfigureLaunchScreen();
        }

        /// <summary>iPad launch screen: the rounded logo tile centered on black, which the intro then picks up.</summary>
        private static void ConfigureLaunchScreen()
        {
            var tile = MenuSceneBuilder.ImportLogo();
            if (tile == null) return;
            PlayerSettings.iOS.SetLaunchScreenImage(tile, iOSLaunchScreenImageType.iPadImage);
            PlayerSettings.iOS.SetiPadLaunchScreenType(iOSLaunchScreenType.ImageAndBackgroundRelative);
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps == null) return;
            var so = new SerializedObject(ps);
            Set(so, "iOSLaunchScreeniPadBackgroundColor", Color.black);
            Set(so, "iOSLaunchScreeniPadFillPct", 29f);   // the logo's size in the intro (380 of 1320 px)
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePlayer()
        {
            ConfigureIcons();
            PlayerSettings.companyName = "MASAR Project";
            PlayerSettings.productName = "MASAR";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "app.masar.game");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "app.masar.game");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, true);
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPadOnly;
            PlayerSettings.iOS.targetOSVersionString = "17.0";
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.hideHomeButton = true;
            PlayerSettings.iOS.appleEnableAutomaticSigning = false; // signing happens in CI
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            // Input System only (the legacy Input Manager is not used anywhere)
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 1) { prop.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); Debug.Log("[MASAR] Active input handling -> Input System (restart required)"); }
            }
        }

        // ------------------------------------------------------------------ rendering

        private static void ConfigureRendering()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDir)) AssetDatabase.CreateFolder("Assets/Masar", "Settings");

            var baseAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (baseAsset == null)
            {
                baseAsset = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset")
                    .Select(g => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(a => a != null && !AssetDatabase.GetAssetPath(a).StartsWith(SettingsDir));
            }
            if (baseAsset == null) { Debug.LogError("[MASAR] No URP asset found to base the tiers on. Is URP installed?"); return; }

            var baseRenderer = GetRendererData(baseAsset);
            var full = CopyRenderer(baseRenderer, "Renderer_Full", ssao: true);
            var lite = CopyRenderer(baseRenderer, "Renderer_Lite", ssao: false);

            var tierAssets = new List<UniversalRenderPipelineAsset>();
            foreach (var t in Tiers)
            {
                string path = $"{SettingsDir}/URP_{t.Name}.asset";
                if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path) == null)
                    AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseAsset), path);
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                ApplyTier(a, t, t.Ssao ? full : lite);
                tierAssets.Add(a);
            }

            GraphicsSettings.defaultRenderPipeline = tierAssets[2];
            ConfigureQualityLevels(tierAssets);
            SetBrgKeepAll();
        }

        private static ScriptableRendererData GetRendererData(UniversalRenderPipelineAsset a)
        {
            var so = new SerializedObject(a);
            var list = so.FindProperty("m_RendererDataList");
            if (list != null && list.arraySize > 0) return list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableRendererData;
            return null;
        }

        private static ScriptableRendererData CopyRenderer(ScriptableRendererData src, string name, bool ssao)
        {
            string path = $"{SettingsDir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path) == null && src != null)
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), path);
            var r = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (r == null) return src;
            var so = new SerializedObject(r);
            Set(so, "m_RenderingMode", 2);              // Forward+ (needed by the GPU Resident Drawer)
            so.ApplyModifiedPropertiesWithoutUndo();
            bool foundSsao = false;
            foreach (var f in r.rendererFeatures)
            {
                if (f == null) continue;
                if (f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"))
                {
                    f.SetActive(ssao);
                    foundSsao = true;
                    // Stronger, wider contact occlusion: grounds facades, ledges, AC units and the car
                    var fso = new SerializedObject(f);
                    Set(fso, "m_Settings.Intensity", 1.5f);
                    Set(fso, "m_Settings.Radius", 0.55f);
                    Set(fso, "m_Settings.DirectLightingStrength", 0.3f);
                    fso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(f);
                }
            }
            if (ssao && !foundSsao) Debug.LogWarning($"[MASAR] {name}: the template renderer has no SSAO feature. SSAO stays off.");
            EditorUtility.SetDirty(r);
            return r;
        }

        private static void ApplyTier(UniversalRenderPipelineAsset a, Tier t, ScriptableRendererData renderer)
        {
            var so = new SerializedObject(a);
            var list = so.FindProperty("m_RendererDataList");
            if (list != null) { list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = renderer; }
            Set(so, "m_DefaultRendererIndex", 0);
            Set(so, "m_RenderScale", t.RenderScale);
            Set(so, "m_ShadowDistance", t.ShadowDistance);
            Set(so, "m_ShadowCascadeCount", t.Cascades);
            Set(so, "m_MainLightShadowmapResolution", t.ShadowRes);
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_SoftShadowsSupported", t.SoftShadows);
            Set(so, "m_SupportsHDR", t.Hdr);
            Set(so, "m_MSAA", t.Msaa);
            Set(so, "m_UpscalingFilter", t.Stp ? 4 : 0);   // 4 = STP, 0 = Auto
            Set(so, "m_AdditionalLightsPerObjectLimit", Mathf.Min(8, t.AdditionalLights));
            Set(so, "m_AdditionalLightsRenderingMode", 1); // per pixel
            Set(so, "m_AdditionalLightShadowsSupported", t.AdditionalLightShadows);
            Set(so, "m_RequireDepthTexture", true);
            Set(so, "m_UseSRPBatcher", true);
            Set(so, "m_GPUResidentDrawerMode", t.GpuResidentDrawer ? 1 : 0);
            Set(so, "m_EnableLODCrossFade", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(a);
        }

        private static void ConfigureQualityLevels(List<UniversalRenderPipelineAsset> assets)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs == null) { Debug.LogError("[MASAR] QualitySettings.asset not found"); return; }
            var so = new SerializedObject(qs);
            var arr = so.FindProperty("m_QualitySettings");
            if (arr.arraySize == 0) arr.arraySize = 1;
            while (arr.arraySize < Tiers.Length) arr.InsertArrayElementAtIndex(arr.arraySize - 1);
            while (arr.arraySize > Tiers.Length) arr.DeleteArrayElementAtIndex(arr.arraySize - 1);
            for (int i = 0; i < Tiers.Length; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                var t = Tiers[i];
                SetRel(e, "name", t.Name);
                SetRel(e, "customRenderPipeline", assets[i]);
                SetRel(e, "lodBias", t.LodBias);
                SetRel(e, "globalTextureMipmapLimit", t.TextureMipLimit);
                SetRel(e, "anisotropicTextures", 2);
                SetRel(e, "vSyncCount", 0);
                SetRel(e, "realtimeReflectionProbes", true);
                SetRel(e, "streamingMipmapsActive", true);
                SetRel(e, "streamingMipmapsMemoryBudget", t.Name == "Low" ? 256f : t.Name == "Medium" ? 512f : 1024f);
                SetRel(e, "skinWeights", 4);
                // Every tier must exist on every platform. The URP template excludes its "Mobile" level
                // from desktop and its "PC" level from iOS. Inherited, that left the iPad with only "Low".
                var excluded = e.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null) excluded.ClearArray();
            }
            // Platform defaults: desktop Ultra, iPad High (until measured)
            var per = so.FindProperty("m_PerPlatformDefaultQuality");
            if (per != null)
            {
                for (int i = 0; i < per.arraySize; i++)
                {
                    var kv = per.GetArrayElementAtIndex(i);
                    var key = kv.FindPropertyRelative("first");
                    var val = kv.FindPropertyRelative("second");
                    if (key == null || val == null) continue;
                    if (key.stringValue == "iPhone") val.intValue = 2;
                    else if (key.stringValue == "Standalone") val.intValue = 3;
                    else val.intValue = Mathf.Min(val.intValue, Tiers.Length - 1);
                }
            }
            Set(so, "m_CurrentQuality", 3);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBrgKeepAll()
        {
            // The GPU Resident Drawer needs BatchRendererGroup shader variants to survive stripping.
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs == null) return;
            var so = new SerializedObject(gs);
            if (Set(so, "m_BrgStripping", 2)) so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ serialized helpers

        private static bool Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[MASAR] Setting '{name}' not found on {so.targetObject.GetType().Name}"); return false; }
            Assign(p, value);
            return true;
        }

        private static void SetRel(SerializedProperty parent, string name, object value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) { Debug.LogWarning($"[MASAR] Quality setting '{name}' not found"); return; }
            Assign(p, value);
        }

        private static void Assign(SerializedProperty p, object value)
        {
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i:
                    // Enums are written by VALUE, not declaration index: URP's MsaaQuality is
                    // {Disabled=1, _2x=2, ...}, so index 1 silently meant 2x MSAA (which also disabled STP).
                    if (p.propertyType == SerializedPropertyType.Enum) p.intValue = i;
                    else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = i != 0;
                    else p.intValue = i;
                    break;
                case float f: p.floatValue = f; break;
                case string s: p.stringValue = s; break;
                case Color c: p.colorValue = c; break;
                case Object o: p.objectReferenceValue = o; break;
            }
        }
    }
}
