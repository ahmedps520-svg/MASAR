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
        public static Material Scanned(string name, string setId, Color tint, float smoothnessScale = 1f, float normalScale = 1f, int maxSize = 1024)
        {
            string path = $"{MatRoot}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit;
            string dir = $"{TexRoot}/{setId}";
            // ambientCG sets use "<id>_1K-JPG_<Map>.jpg", Poly Haven sets (PH_*) use "<id>_<Map>.jpg"
            string pre = File.Exists($"{dir}/{setId}_Color.jpg") ? $"{dir}/{setId}_" : $"{dir}/{setId}_1K-JPG_";
            var color = Load(pre + "Color.jpg", false, maxSize: maxSize);
            var normal = Load(pre + "NormalGL.jpg", true, maxSize: maxSize);
            var mask = PackSmoothness(dir, setId, maxSize);
            var ao = Load(pre + "AmbientOcclusion.jpg", false, linear: true, maxSize: maxSize);
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

        // ------------------------------------------------------------------ realism helpers

        private const string GenTex = "Assets/Masar/Generated";

        /// <summary>
        /// Adds URP Lit's detail layer from a scanned set: a grey-normalized copy of its color map
        /// (multiply x2) plus its normal map, tiled `detailTiling` times per base-map repeat.
        /// The base map carries the large-scale look (cracks, stains), and the detail layer keeps the
        /// surface crisp at the camera, so tiling isn't obvious from either distance.
        /// </summary>
        public static void AddDetail(Material m, string setId, float detailTiling, float albedoStrength = 1f, float normalStrength = 0.6f)
        {
            string dir = $"{TexRoot}/{setId}";
            string pre = File.Exists($"{dir}/{setId}_Color.jpg") ? $"{dir}/{setId}_" : $"{dir}/{setId}_1K-JPG_";
            string outPath = $"{GenTex}/{setId}_DetailAlbedo.png";
            if (!File.Exists(outPath) && File.Exists(pre + "Color.jpg"))
            {
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                src.LoadImage(File.ReadAllBytes(pre + "Color.jpg"));
                int w = Mathf.Min(1024, src.width), h = Mathf.Min(1024, src.height);
                var px = new Color32[w * h];
                double sum = 0;
                var lum = new float[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                        float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                        lum[y * w + x] = l; sum += l;
                    }
                float mean = (float)(sum / lum.Length);
                for (int i = 0; i < lum.Length; i++)
                {
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(127.5f * lum[i] / Mathf.Max(0.02f, mean)), 0, 255);
                    px[i] = new Color32(v, v, v, 255);
                }
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
                dst.SetPixels32(px);
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                Object.DestroyImmediate(src); Object.DestroyImmediate(dst);
                AssetDatabase.ImportAsset(outPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
                imp.sRGBTexture = false; imp.maxTextureSize = 1024; imp.anisoLevel = 8; imp.streamingMipmaps = true;
                imp.SaveAndReimport();
            }
            var detailAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            var detailNormal = Load(pre + "NormalGL.jpg", true, maxSize: 2048);
            if (detailAlbedo) { m.SetTexture("_DetailAlbedoMap", detailAlbedo); m.SetFloat("_DetailAlbedoMapScale", albedoStrength); }
            if (detailNormal) { m.SetTexture("_DetailNormalMap", detailNormal); m.SetFloat("_DetailNormalMapScale", normalStrength); }
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(detailTiling, detailTiling));
            m.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(m);
        }

        /// <summary>Multi-octave value noise in 0..1, tileable over `period` cells.</summary>
        public static float TileNoise(float x, float y, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int p = period << o;
                float fx = x * p, fy = y * p;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = fx - x0, ty = fy - y0;
                tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                float a = Hash(x0 % p, y0 % p, seed + o), b = Hash((x0 + 1) % p, y0 % p, seed + o);
                float c = Hash(x0 % p, (y0 + 1) % p, seed + o), d = Hash((x0 + 1) % p, (y0 + 1) % p, seed + o);
                sum += amp * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                norm += amp; amp *= 0.5f;
            }
            return sum / norm;
        }

        private static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        private static Texture2D SaveGenerated(string name, int w, int h, System.Func<int, int, Color> f, bool srgb, TextureWrapMode wrap, bool alpha)
        {
            string path = $"{GenTex}/{name}.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
                var px = new Color[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
                tex.SetPixels(px);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.sRGBTexture = srgb; imp.wrapMode = wrap; imp.alphaIsTransparency = alpha; imp.mipmapEnabled = true;
                imp.anisoLevel = 4; imp.streamingMipmaps = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Date-palm leaflet palette: X runs along a leaflet (base to tip), Y picks a per-leaflet
        /// variation row. Colors follow real Phoenix dactylifera leaflets: glaucous grey-green,
        /// yellower at the base, slightly sun-bleached or brown at the tips.
        /// </summary>
        public static Material PalmLeaflets()
        {
            var tex = SaveGenerated("PalmLeafletPalette", 128, 64, (x, y) =>
            {
                float t = x / 127f, row = y / 63f;
                float n = TileNoise(t, row, 8, 3, 11);
                Color baseC = Color.Lerp(new Color(0.30f, 0.34f, 0.14f), new Color(0.17f, 0.25f, 0.12f), Mathf.SmoothStep(0f, 0.35f, t));
                Color mid = Color.Lerp(new Color(0.12f, 0.20f, 0.10f), new Color(0.20f, 0.27f, 0.15f), row);   // deep olive to grey-green
                Color tip = Color.Lerp(mid, new Color(0.38f, 0.34f, 0.18f), 0.5f);
                Color c = t < 0.35f ? Color.Lerp(baseC, mid, t / 0.35f) : Color.Lerp(mid, tip, Mathf.Pow(Mathf.InverseLerp(0.75f, 1f, t), 2f));
                return c * (0.88f + 0.24f * n);
            }, true, TextureWrapMode.Clamp, false);
            var m = Solid("City_PalmLeaflets", Color.white, 0.12f, 0f, true);
            m.SetTexture("_BaseMap", tex);
            // Matte foliage: no sky reflection sheen (it read as silvery crowns at distance)
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Dry, dead fronds hanging below the crown.</summary>
        public static Material PalmDeadFrond()
        {
            var tex = SaveGenerated("PalmDeadPalette", 128, 64, (x, y) =>
            {
                float t = x / 127f, row = y / 63f, n = TileNoise(t, row, 8, 3, 23);
                return Color.Lerp(new Color(0.46f, 0.36f, 0.22f), new Color(0.62f, 0.52f, 0.36f), row * 0.7f + t * 0.3f) * (0.85f + 0.3f * n);
            }, true, TextureWrapMode.Clamp, false);
            // Tinted down: in full sun the straw palette blew out to near-white hanging skirts
            var m = Solid("City_PalmDeadFrond", new Color(0.66f, 0.60f, 0.54f), 0.08f, 0f, true);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Tire-wear track overlay: transparent dark, slightly polished strips in the wheel paths.
        /// U runs across the strip (soft edges), V along it (patchy intensity).
        /// </summary>
        public static Material RoadWear()
        {
            var tex = SaveGenerated("RoadWearMask", 64, 512, (x, y) =>
            {
                float u = x / 63f, v = y / 511f;
                float edge = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.26f, 2f));
                float along = 0.45f + 0.55f * TileNoise(u * 0.25f, v, 6, 4, 5);
                return new Color(1f, 1f, 1f, Mathf.Clamp01(edge * along));
            }, true, TextureWrapMode.Repeat, true);
            // Low smoothness: a glossy transparent overlay picks up grazing-angle sky reflection (Fresnel)
            // and turns white, the opposite of tire grime.
            var m = Glass("City_RoadWear", new Color(0.015f, 0.015f, 0.016f, 0.38f), 0.08f, false);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetTexture("_BaseMap", tex);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Facade weathering atlas: 4 rows of 256 px (V), tileable along U. Each row fades to zero
        /// alpha at its top and bottom texels so mip levels don't bleed between rows.
        ///   row 0: dust and dirt band rising from the ground (strongest at the bottom)
        ///   row 1: rain drips hanging from a top edge (sills, slab bands, parapets)
        ///   row 2: one stain streak (water and rust below a split-AC unit), centered on U
        ///   row 3: large faint dirt patches with vertical rain wash, for whole faces
        /// Used by BuildingKit as alpha-blended overlay quads a few centimeters in front of the wall.
        /// </summary>
        public static Material FacadeGrime()
        {
            var tex = SaveGenerated("FacadeGrimeAtlas", 512, 1024, (x, y) =>
            {
                float u = x / 512f; int row = y / 256; float t = (y % 256) / 255f;
                float edge = Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.02f);
                Color c; float a;
                switch (row)
                {
                    case 0:
                    {
                        float n = TileNoise(u, t, 8, 4, 101), f = TileNoise(u, t, 48, 2, 103);
                        float h = 1f - t;
                        a = Mathf.Pow(h, 1.7f) * (0.40f + 0.60f * n) + 0.18f * Mathf.Pow(h, 5f) * f;
                        a = Mathf.Clamp01(a) * 0.60f;
                        c = Color.Lerp(new Color(0.44f, 0.37f, 0.28f), new Color(0.30f, 0.25f, 0.19f), f);
                        break;
                    }
                    case 1:
                    {
                        // Distinct drip columns, each with its own position, width, length and strength,
                        // in two layers (broad stains + thin runs). The atlas tile spans about 5-7 m of wall.
                        float d = 1f - t;                                               // distance below the edge
                        float streak = Mathf.Max(DripLayer(u, d, 22, 211, 1f), 0.7f * DripLayer(u, d, 53, 223, 0.45f));
                        float band = Mathf.Exp(-d / 0.05f);
                        float grain = 0.75f + 0.25f * TileNoise(u, t, 32, 2, 117);
                        a = Mathf.Clamp01(band * 0.6f + streak * grain) * 0.85f;
                        c = Color.Lerp(new Color(0.20f, 0.18f, 0.16f), new Color(0.33f, 0.28f, 0.22f), d);
                        break;
                    }
                    case 2:
                    {
                        float wig = (TileNoise(0.5f, t, 4, 2, 121) - 0.5f) * 0.10f;
                        float w = 0.12f + 0.16f * (1f - t);                              // spreads as it runs down
                        float g = Mathf.Exp(-Mathf.Pow((u - 0.5f - wig) / w, 2f));
                        float fade = Mathf.Pow(t, 0.5f);                                 // strongest at the drip point
                        float n = 0.65f + 0.35f * TileNoise(u, t, 12, 3, 123);
                        a = Mathf.Clamp01(g * fade * n * 1.25f) * 0.85f;
                        c = Color.Lerp(new Color(0.30f, 0.20f, 0.12f), new Color(0.22f, 0.19f, 0.16f), 1f - t);
                        break;
                    }
                    default:
                    {
                        float n = TileNoise(u, t, 3, 4, 131);
                        float wash = TileNoise(u, t * 0.125f, 48, 2, 137);
                        float blot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.72f, n));
                        a = Mathf.Clamp01(blot * 0.34f * (0.65f + 0.35f * wash) + 0.08f * wash);
                        c = new Color(0.36f, 0.30f, 0.23f);
                        break;
                    }
                }
                c.a = a * edge;
                return c;
            }, true, TextureWrapMode.Repeat, true);
            // Matte, no reflections: a glossy overlay would catch the sky (see RoadWear)
            var m = Glass("City_FacadeGrime", Color.white, 0.05f, false);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetTexture("_BaseMap", tex);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 60;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// One layer of rain drips: n columns across the tile; each column gets a hashed center jitter,
        /// width, length (fraction of the row height) and strength. d = distance below the top edge (0..1).
        /// </summary>
        private static float DripLayer(float u, float d, int n, int seed, float maxLen)
        {
            float best = 0f;
            int ci = Mathf.FloorToInt(u * n);
            for (int k = -1; k <= 1; k++)
            {
                int i = ((ci + k) % n + n) % n;
                float center = (ci + k + 0.5f + (Hash(i, 1, seed) - 0.5f) * 0.6f) / n;
                float width = (0.12f + 0.30f * Hash(i, 2, seed)) / n;
                float len = maxLen * (0.15f + 0.85f * Hash(i, 3, seed) * Hash(i, 4, seed));
                float strength = 0.35f + 0.65f * Hash(i, 5, seed);
                if (d > len) continue;
                float across = Mathf.Exp(-Mathf.Pow((u - center) / width, 2f));
                float along = Mathf.Pow(1f - d / len, 0.6f);
                best = Mathf.Max(best, across * along * strength);
            }
            return best;
        }

        /// <summary>Road paint worn by traffic and sun: alpha-clipped noise gives chipped edges and holes.</summary>
        public static Material WornPaint(string name, Color color)
        {
            var tex = SaveGenerated("RoadPaintWear", 512, 512, (x, y) =>
            {
                float u = x / 512f, v = y / 512f;
                float big = TileNoise(u, v, 4, 4, 31), fine = TileNoise(u, v, 32, 3, 47);
                float a = Mathf.Clamp01((big * 0.65f + fine * 0.35f - 0.30f) * 3.2f);
                float shade = 0.80f + 0.20f * fine;
                return new Color(shade, shade, shade, a);
            }, true, TextureWrapMode.Repeat, true);
            var m = Solid(name, color, 0.35f);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ textures

        private static Texture2D Load(string path, bool normal, bool linear = false, int maxSize = 1024)
        {
            if (!File.Exists(path)) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool dirty = false;
                if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (linear && imp.sRGBTexture) { imp.sRGBTexture = false; dirty = true; }
                if (imp.maxTextureSize != maxSize) { imp.maxTextureSize = maxSize; dirty = true; }
                if (!imp.streamingMipmaps) { imp.streamingMipmaps = true; dirty = true; }
                if (imp.anisoLevel != 8) { imp.anisoLevel = 8; dirty = true; }
                var ios = imp.GetPlatformTextureSettings("iPhone");
                if (!ios.overridden || ios.maxTextureSize != maxSize || ios.format != (normal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6))
                {
                    ios.overridden = true;
                    ios.maxTextureSize = maxSize;
                    ios.format = normal ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
                    imp.SetPlatformTextureSettings(ios);
                    dirty = true;
                }
                if (dirty) imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Builds an RGBA mask: R = 0 (non-metal), A = 1 - roughness. Cached as a PNG next to the source.</summary>
        private static Texture2D PackSmoothness(string dir, string setId, int maxSize = 1024)
        {
            string rough = File.Exists($"{dir}/{setId}_Roughness.jpg") ? $"{dir}/{setId}_Roughness.jpg" : $"{dir}/{setId}_1K-JPG_Roughness.jpg";
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
                imp.maxTextureSize = maxSize;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
    }
}
