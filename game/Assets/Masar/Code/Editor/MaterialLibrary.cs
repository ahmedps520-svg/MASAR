using System.IO;
using UnityEditor;
using UnityEngine;

namespace Masar.EditorTools
{
    /// <summary>
    /// Creates the URP materials used by the generators. Photo-scanned CC0 sets
    /// (ambientCG) are converted to URP's conventions: base color, OpenGL normal map,
    /// and roughness packed into a metallic/smoothness map (smoothness = 1 - roughness, stored in alpha).
    /// </summary>
    public static class MaterialLibrary
    {
        public const string TexRoot = "Assets/Masar/Art/Textures";
        public const string MatRoot = "Assets/Masar/Art/Materials";

        private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");
        private static Shader ComplexLit => Shader.Find("Universal Render Pipeline/Complex Lit");

        public static void EnsureFolders()
        {
            foreach (var p in new[] { "Assets/Masar/Art", MatRoot, "Assets/Masar/Generated", "Assets/Masar/Generated/Meshes", "Assets/Masar/Prefabs", "Assets/Masar/Scenes", "Assets/Masar/Data" })
            {
                if (AssetDatabase.IsValidFolder(p)) continue;
                var parent = Path.GetDirectoryName(p).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(p));
            }
        }

        /// <summary>Textured PBR material from an ambientCG set. tile = meters per texture repeat.</summary>
        public static Material Scanned(string name, string setId, Color tint, float smoothnessScale = 1f, float normalScale = 1f)
        {
            string path = $"{MatRoot}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit;
            string dir = $"{TexRoot}/{setId}";
            var color = Load($"{dir}/{setId}_1K-JPG_Color.jpg", false);
            var normal = Load($"{dir}/{setId}_1K-JPG_NormalGL.jpg", true);
            var mask = PackSmoothness(dir, setId);
            var ao = Load($"{dir}/{setId}_1K-JPG_AmbientOcclusion.jpg", false, linear: true);
            m.SetColor("_BaseColor", tint);
            if (color) m.SetTexture("_BaseMap", color);
            if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
            if (mask)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", smoothnessScale);
                m.SetFloat("_SmoothnessTextureChannel", 0f); // metallic map alpha
            }
            if (ao) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP"); }
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Untextured lit material.</summary>
        public static Material Solid(string name, Color color, float smoothness, float metallic = 0f, bool doubleSided = false)
        {
            string path = $"{MatRoot}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.doubleSidedGI = doubleSided;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Lit material with emission enabled (the color is driven at runtime by property blocks).</summary>
        public static Material Emissive(string name, Color baseColor, Color emission, float smoothness = 0.8f)
        {
            var m = Solid(name, baseColor, smoothness);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Transparent glass (reflections come from the reflection probes).</summary>
        public static Material Glass(string name, Color color, float smoothness = 0.97f, bool doubleSided = true)
        {
            var m = Solid(name, color, smoothness, 0f, doubleSided);
            m.SetFloat("_Surface", 1f);       // transparent
            m.SetFloat("_Blend", 0f);         // alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Automotive paint: Complex Lit with a clear-coat layer over a metallic base.</summary>
        public static Material CarPaint(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MatRoot}/{name}.mat";
            var shader = ComplexLit != null ? ComplexLit : Lit;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (shader == ComplexLit)
            {
                m.SetFloat("_ClearCoat", 1f);
                m.SetFloat("_ClearCoatMask", 1f);
                m.SetFloat("_ClearCoatSmoothness", 0.96f);
                m.EnableKeyword("_CLEARCOAT");
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Date-palm frond material. The leaflet texture is generated procedurally (self-made, no
        /// external asset): a midrib with angled, tapering leaflets on both sides, alpha-clipped
        /// and double-sided.
        /// </summary>
        public static Material PalmFrond()
        {
            const string texPath = "Assets/Masar/Generated/PalmFrond.png";
            if (!File.Exists(texPath))
            {
                const int W = 256, H = 1024;
                var px = new Color32[W * H];
                var rnd = new System.Random(42);
                void Dot(int cx, int cy, float r, Color32 c)
                {
                    int ri = Mathf.CeilToInt(r);
                    for (int y = cy - ri; y <= cy + ri; y++)
                        for (int x = cx - ri; x <= cx + ri; x++)
                        {
                            if (x < 0 || y < 0 || x >= W || y >= H) continue;
                            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                            px[y * W + x] = c;
                        }
                }
                // Midrib
                for (int y = 0; y < H; y++) Dot(W / 2, y, Mathf.Lerp(3.5f, 1.2f, y / (float)H), new Color32(120, 112, 58, 255));
                // Leaflets, angled toward the tip, longest in the middle of the frond
                for (float v = 0.07f; v < 0.98f; v += 0.0105f)
                {
                    float env = Mathf.Sin(Mathf.Clamp01((v - 0.04f) / 0.96f) * Mathf.PI);
                    float len = (0.20f + 0.78f * env) * (W * 0.5f - 4f);
                    foreach (int side in new[] { -1, 1 })
                    {
                        float jitter = (float)rnd.NextDouble();
                        var col = new Color32((byte)(38 + 22 * jitter), (byte)(70 + 30 * jitter), (byte)(22 + 12 * jitter), 255);
                        int steps = Mathf.CeilToInt(len);
                        for (int s = 0; s <= steps; s++)
                        {
                            float t = s / (float)steps;
                            float x = W * 0.5f + side * t * len;
                            float y = v * H + t * len * 0.75f;              // swept toward the tip
                            Dot(Mathf.RoundToInt(x), Mathf.RoundToInt(y), Mathf.Lerp(3.2f, 0.8f, t), col);
                        }
                    }
                }
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.5f;
                imp.SaveAndReimport();
            }
            string path = $"{MatRoot}/City_PalmFrond.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Smoothness", 0.25f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ textures

        private static Texture2D Load(string path, bool normal, bool linear = false)
        {
            if (!File.Exists(path)) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool dirty = false;
                if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (linear && imp.sRGBTexture) { imp.sRGBTexture = false; dirty = true; }
                if (imp.maxTextureSize != 1024) { imp.maxTextureSize = 1024; dirty = true; }
                if (!imp.streamingMipmaps) { imp.streamingMipmaps = true; dirty = true; }
                if (imp.anisoLevel != 8) { imp.anisoLevel = 8; dirty = true; }
                var ios = imp.GetPlatformTextureSettings("iPhone");
                if (!ios.overridden || ios.format != (normal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6))
                {
                    ios.overridden = true;
                    ios.maxTextureSize = 1024;
                    ios.format = normal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
                    imp.SetPlatformTextureSettings(ios);
                    dirty = true;
                }
                if (dirty) imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Builds an RGBA mask: R = 0 (non-metal), A = 1 - roughness. Cached as a PNG next to the source.</summary>
        private static Texture2D PackSmoothness(string dir, string setId)
        {
            string rough = $"{dir}/{setId}_1K-JPG_Roughness.jpg";
            string outPath = $"{dir}/{setId}_MetallicSmoothness.png";
            if (!File.Exists(rough)) return null;
            if (!File.Exists(outPath))
            {
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                src.LoadImage(File.ReadAllBytes(rough));
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    byte r = px[i].r;
                    px[i] = new Color32(0, 0, 0, (byte)(255 - r));
                }
                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
                dst.SetPixels32(px);
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);
                AssetDatabase.ImportAsset(outPath);
            }
            var imp = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (imp != null && (imp.sRGBTexture || !imp.streamingMipmaps))
            {
                imp.sRGBTexture = false;
                imp.streamingMipmaps = true;
                imp.maxTextureSize = 1024;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
    }
}
