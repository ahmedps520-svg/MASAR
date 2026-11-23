using Masar.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Masar.EditorTools
{
    /// <summary>
    /// Builds the main menu scene: a dark showroom (asphalt floor with painted lane lines, a key
    /// light and two rim lights, the HDRI sky for reflections only) behind the code-built menu.
    ///   -executeMethod Masar.EditorTools.MenuSceneBuilder.Build
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string LogoTilePath = "Assets/Masar/Art/Brand/MasarLogoTile.png";
        const string PostPath = "Assets/Masar/Scenes/MenuPost.asset";
        const string LightingPath = "Assets/Masar/Scenes/MenuLighting.lighting";
        const string FloorMeshPath = "Assets/Masar/Scenes/MenuFloor.asset";

        [MenuItem("MASAR/Build/Main Menu Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 30f;          // a long lens, as in car photography
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            cam.allowHDR = true;
            var data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            // Lights: warm key from high front-left, cool rim behind, soft warm fill
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.93f, 0.84f);
            key.intensity = 1.9f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.9f;
            key.transform.rotation = Quaternion.Euler(42f, -128f, 0f);
            key.gameObject.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = key;
            Spot("RimLight", new Vector3(-5.5f, 4.5f, 6.5f), new Color(0.70f, 0.80f, 1f), 14f);
            Spot("FillLight", new Vector3(6.5f, 3.5f, -5f), new Color(1f, 0.82f, 0.60f), 6f);

            // Sky: not drawn (the camera clears to black), but it gives the paint and glass their reflections
            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialLibrary.MatRoot}/Sky_HDRI_Clear.mat");
            if (sky != null) RenderSettings.skybox = sky;
            else Debug.LogWarning("[MASAR] Sky_HDRI_Clear.mat missing: build the test block first for showroom reflections");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.22f, 0.26f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.11f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.04f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.75f;
            // Black fog: the floor dissolves into darkness a few car lengths away
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.black;
            RenderSettings.fogDensity = 0.03f;   // ~14% fog on the car at 13 m, near black at the floor's edge (60 m)

            // Floor: asphalt with lane paint echoing the logo (two yellow lines and a white stop line)
            var floor = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer));
            floor.GetComponent<MeshFilter>().sharedMesh = FloorMesh();
            floor.GetComponent<MeshRenderer>().sharedMaterial = Mat("City_Asphalt");
            Paint("YellowLine_A", new Vector3(-3.2f, 0f, 1.5f), 28f, 0.32f, -34f, "City_RoadPaintYellow");
            Paint("YellowLine_B", new Vector3(-2.4f, 0f, 2.3f), 28f, 0.32f, -34f, "City_RoadPaintYellow");
            Paint("StopLine", new Vector3(3.4f, 0f, 0f), 9f, 0.45f, 0f, "City_RoadPaintWhite");   // across the car's path

            // Post-processing
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath) != null) AssetDatabase.DeleteAsset(PostPath);
            AssetDatabase.CreateAsset(profile, PostPath);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(14f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.38f);
            vig.smoothness.Override(0.45f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // Menu, showroom and logo
            var showroom = new GameObject("Showroom").AddComponent<MenuShowroom>();
            showroom.cam = cam;
            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.logo = ImportLogo();

            // Bake only the sky reflection (no lightmaps)
            var ls = new LightingSettings { bakedGI = false, realtimeGI = false, name = "MenuLighting" };
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath) != null) AssetDatabase.DeleteAsset(LightingPath);
            AssetDatabase.CreateAsset(ls, LightingPath);
            Lightmapping.lightingSettings = ls;
            EditorSceneManager.SaveScene(scene, TestBlockBuilder.MenuScenePath);
            bool baked = Lightmapping.Bake();
            EditorSceneManager.SaveScene(scene, TestBlockBuilder.MenuScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MASAR] Menu scene built (reflection bake {(baked ? "OK" : "FAILED")})");
        }

        /// <summary>The rounded logo tile, imported for UI use (RawImage): full quality, no mipmaps.</summary>
        public static Texture2D ImportLogo()
        {
            AssetDatabase.ImportAsset(LogoTilePath);
            if (AssetImporter.GetAtPath(LogoTilePath) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.maxTextureSize = 1024;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoTilePath);
            if (tex == null) Debug.LogWarning($"[MASAR] Logo missing at {LogoTilePath}");
            return tex;
        }

        static Material Mat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialLibrary.MatRoot}/{name}.mat");
            if (m == null) Debug.LogWarning($"[MASAR] Material {name} missing: build the city first");
            return m;
        }

        static void Spot(string name, Vector3 pos, Color color, float intensity)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = 22f;
            l.spotAngle = 55f;
            l.innerSpotAngle = 20f;
            l.shadows = LightShadows.None;
            l.transform.position = pos;
            l.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.6f, 0f) - pos);
            l.gameObject.AddComponent<UniversalAdditionalLightData>();
        }

        /// <summary>A flat painted strip just above the floor.</summary>
        static void Paint(string name, Vector3 center, float length, float width, float yaw, string material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(center + Vector3.up * 0.006f, Quaternion.Euler(90f, yaw, 0f));
            go.transform.localScale = new Vector3(width, length, 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(material);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A 120 m square floor with UVs in meters / 4 (the city's asphalt tiling).</summary>
        static Mesh FloorMesh()
        {
            const float h = 60f, uv = 0.25f;
            var m = new Mesh { name = "MenuFloor" };
            m.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) };
            m.uv = new[] { new Vector2(-h, -h) * uv, new Vector2(-h, h) * uv, new Vector2(h, h) * uv, new Vector2(h, -h) * uv };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            if (AssetDatabase.LoadAssetAtPath<Mesh>(FloorMeshPath) != null) AssetDatabase.DeleteAsset(FloorMeshPath);
            AssetDatabase.CreateAsset(m, FloorMeshPath);
            return m;
        }
    }
}
