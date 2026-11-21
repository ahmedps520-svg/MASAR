using System.Collections.Generic;
using System.IO;
using Masar.World.Traffic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Imports owner-provided Mixamo characters as pedestrians (they replace the placeholder figures).
    /// Put the files in game/Assets/Masar/Art/Characters/Imported/ (gitignored: Mixamo content may be used
    /// in a game but not redistributed as files, and the repository is public):
    ///   - one FBX per character ("FBX for Unity", With Skin, T-pose), any name
    ///   - one walk animation FBX whose name contains "walk" ("Without Skin", "In Place" ticked)
    /// Every character becomes a Humanoid prefab driven by the shared walk clip; the walk speed follows
    /// the pedestrian's real speed.
    /// </summary>
    public static class MixamoPedestrians
    {
        public const string Folder = "Assets/Masar/Art/Characters/Imported";
        const string PrefabDir = "Assets/Masar/Prefabs/Pedestrians";
        const string ControllerPath = "Assets/Masar/Prefabs/Pedestrians/MixamoWalk.controller";

        /// <summary>
        /// Mixamo files come in different units, so characters imported as giants or children: scale each
        /// to a real adult height and stand it on the ground.
        /// </summary>
        static void NormalizeHeight(GameObject body, float targetHeight)
        {
            var rs = body.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y < 0.01f) return;
            float s = targetHeight / b.size.y;
            body.transform.localScale = body.transform.localScale * s;
            rs = body.GetComponentsInChildren<Renderer>();
            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            body.transform.position += Vector3.up * (body.transform.parent.position.y - b.min.y);
            Debug.Log($"[MASAR] Mixamo {body.name}: scaled x{s:F3} to {targetHeight:F2} m");
        }

        /// <summary>
        /// URP Lit materials built from the extracted Mixamo textures (Diffuse, Normal; Glossiness as
        /// smoothness). The automatic FBX material conversion left them white. Material slot i uses
        /// texture tile 1001 + i unless the original material already names its texture.
        /// </summary>
        static void AssignMaterials(GameObject body, string fbxPath)
        {
            string name = Path.GetFileNameWithoutExtension(fbxPath);
            string texDir = Path.GetDirectoryName(fbxPath).Replace('\\', '/') + "/_tex_" + name;
            string matDir = "Assets/Masar/Prefabs/Pedestrians/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder("Assets/Masar/Prefabs/Pedestrians", "Materials");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Texture2D Tex(string suffix, string tile, bool normal)
            {
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
                {
                    string tp = AssetDatabase.GUIDToAssetPath(g);
                    string f = Path.GetFileNameWithoutExtension(tp);
                    if (!f.Contains("_" + tile + "_") || !f.EndsWith(suffix)) continue;
                    var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
                    if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                }
                return null;
            }
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.sharedMaterials;
                var mesh = r.sharedMesh;
                for (int i = 0; i < mats.Length; i++)
                {
                    // Which texture set this slot uses:
                    // 1. by name: materials named after a part ("Remy_Body" -> Remy_Body_Diffuse)
                    // 2. by UV tile (UDIM): Mixamo's "Ch.." characters put each part in its own UV tile
                    //    (u in [k, k+1] -> tile 1001 + k), and several material slots share a tile
                    string tile = null;
                    // Part names may be plural in the model ("Tops") and singular in the textures ("Top")
                    var names = (mats[i] != null ? mats[i].name : "") + "_" + r.gameObject.name;
                    foreach (var part in names.Split('_', ':', ' ', '.', '(', ')'))
                    {
                        if (part.Length < 3) continue;
                        foreach (var cand in new[] { part, part.TrimEnd('s', 'S'), char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant() })
                            if (cand.Length >= 3 && Tex("Diffuse", cand, false) != null) { tile = cand; break; }
                        if (tile != null) break;
                    }
                    if (tile == null && mesh != null && i < mesh.subMeshCount)
                    {
                        var sm = mesh.GetSubMesh(i);
                        var uvs = new System.Collections.Generic.List<Vector2>();
                        mesh.GetUVs(0, uvs);
                        if (uvs.Count > 0 && sm.vertexCount > 0)
                        {
                            Vector2 u = uvs[Mathf.Clamp(sm.firstVertex + sm.vertexCount / 2, 0, uvs.Count - 1)];
                            tile = (1001 + Mathf.FloorToInt(u.x) + 10 * Mathf.FloorToInt(u.y)).ToString();
                        }
                    }
                    if (tile == null || Tex("Diffuse", tile, false) == null) tile = Tex("Diffuse", "Body", false) != null ? "Body" : "1001";
                    var diffuse = Tex("Diffuse", tile, false);
                    var opacity = Tex("Opacity", tile, false);
                    if (diffuse != null && opacity != null) diffuse = MergeOpacity(diffuse, opacity);
                    string mp = $"{matDir}/{name}_{tile}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
                    if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, mp); }
                    mat.shader = lit;
                    if (diffuse != null) { mat.SetTexture("_BaseMap", diffuse); mat.SetTexture("_MainTex", diffuse); }
                    mat.SetColor("_BaseColor", Color.white);
                    var normalMap = Tex("Normal", tile, true);
                    if (normalMap != null) { mat.SetTexture("_BumpMap", normalMap); mat.EnableKeyword("_NORMALMAP"); }
                    mat.SetFloat("_Smoothness", 0.3f);
                    mat.SetFloat("_Metallic", 0f);
                    // Hair and eyelashes use the diffuse alpha: cut out, both sides
                    string dp = diffuse != null ? AssetDatabase.GetAssetPath(diffuse) : null;
                    var dti = dp != null ? (TextureImporter)AssetImporter.GetAtPath(dp) : null;
                    bool alpha = dti != null && dti.DoesSourceTextureHaveAlpha();
                    mat.SetFloat("_AlphaClip", alpha ? 1f : 0f);
                    mat.SetFloat("_Cutoff", 0.4f);
                    mat.SetFloat("_Cull", alpha ? 0f : 2f);
                    if (alpha) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                    EditorUtility.SetDirty(mat);
                    mats[i] = mat;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Some characters (Remy) ship hair transparency as a separate Opacity map; URP Lit reads it from the
        /// base map's alpha, so the two are merged once into "*_DiffuseAlpha.png" next to the originals.
        /// </summary>
        static Texture2D MergeOpacity(Texture2D diffuse, Texture2D opacity)
        {
            string dp = AssetDatabase.GetAssetPath(diffuse);
            string outPath = dp.Replace("_Diffuse.png", "_DiffuseAlpha.png");
            if (outPath == dp) return diffuse;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (existing != null) return existing;
            Color32[] Read(Texture2D t, int w, int h)
            {
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var px = tex.GetPixels32(); Object.DestroyImmediate(tex);
                return px;
            }
            int W = diffuse.width, H = diffuse.height;
            var c = Read(diffuse, W, H);
            var a = Read(opacity, W, H);
            for (int k = 0; k < c.Length; k++) c[k].a = a[k].r;
            var outTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            outTex.SetPixels32(c); outTex.Apply();
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            AssetDatabase.ImportAsset(outPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
            ti.alphaIsTransparency = true; ti.maxTextureSize = 1024; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        /// <summary>Embedded 2-4K textures are extracted next to the model and capped at 1024 px (memory on the iPad).</summary>
        static void LimitTextures(string path, ModelImporter imp)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string dir = parent + "/_tex_" + Path.GetFileNameWithoutExtension(path);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                imp.ExtractTextures(dir);
                AssetDatabase.Refresh();
            }
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g));
                if (ti != null && ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
            }
        }

        [MenuItem("MASAR/Build/Pedestrians (Mixamo)")]
        public static void Rebuild() => Build();

        public static Pedestrian[] Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return null;
            var fbx = new List<string>();
            foreach (var f in Directory.GetFiles(Folder, "*.fbx", SearchOption.AllDirectories)) fbx.Add(f.Replace('\\', '/'));
            // Mixamo names animations "Character@Animation.fbx". The walk loop is the one named "walk" but not "start"/"stop"
            bool IsAnim(string p) => Path.GetFileName(p).Contains("@");
            string Lower(string p) => Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            string walkPath = fbx.Find(p => IsAnim(p) && Lower(p).Contains("walk") && !Lower(p).Contains("start") && !Lower(p).Contains("stop"))
                              ?? fbx.Find(p => Lower(p).Contains("walk") && !Lower(p).Contains("start") && !Lower(p).Contains("stop"));
            // Characters left out of a Jeddah street crowd (Ch08: ragged, zombie-like look)
            string[] excluded = { "ch08_nonpbr" };
            var characters = fbx.FindAll(p => p != walkPath && !IsAnim(p) && !Lower(p).Contains("walk") && System.Array.IndexOf(excluded, Lower(p)) < 0);
            if (walkPath == null || characters.Count == 0)
            {
                if (fbx.Count > 0) Debug.LogWarning($"[MASAR] Mixamo: need at least one character FBX and one walk FBX (name containing 'walk') in {Folder}; found {fbx.Count} file(s)");
                return null;
            }

            // Humanoid rigs; the walk clip loops in place
            foreach (var p in fbx)
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(p);
                bool changed = imp.animationType != ModelImporterAnimationType.Human;
                imp.animationType = ModelImporterAnimationType.Human;
                if (p == walkPath)
                {
                    var clips = imp.defaultClipAnimations;
                    // Root motion XZ is NOT baked into the pose: it goes to the root, which is discarded (walk in place)
                    foreach (var c in clips) { c.loopTime = true; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = false; c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; }
                    imp.clipAnimations = clips;
                    changed = true;
                }
                else
                {
                    imp.importAnimation = false;
                    // Materials become URP Lit through the material description importer
                    if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; changed = true; }
                }
                if (changed) imp.SaveAndReimport();
                if (characters.Contains(p)) LimitTextures(p, imp);
            }
            AnimationClip walk = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(walkPath))
                if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) { walk = ac; break; }
            if (walk == null) { Debug.LogWarning($"[MASAR] Mixamo: no animation clip in {walkPath}"); return null; }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.layers[0].stateMachine.AddState("Walk").motion = walk;

            var result = new List<Pedestrian>();
            foreach (var p in characters)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (model == null) continue;
                var root = new GameObject("Ped_Mixamo_" + Path.GetFileNameWithoutExtension(p));
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                body.transform.SetParent(root.transform, false);
                var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = false;
                AssignMaterials(body, p);
                int h = 0; foreach (char ch in Path.GetFileName(p)) h = h * 31 + ch;   // stable per file
                NormalizeHeight(body, 1.62f + (Mathf.Abs(h) % 100) / 100f * 0.23f);
                // Not drawn (or animated) once smaller than 3 % of the screen height
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.03f, body.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
                var ped = root.AddComponent<Pedestrian>();
                ped.animator = anim;
                var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true;
                var cap = root.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.88f, 0); cap.height = 1.75f; cap.radius = 0.25f;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{root.name}.prefab");
                Object.DestroyImmediate(root);
                result.Add(prefab.GetComponent<Pedestrian>());
            }
            Debug.Log($"[MASAR] Mixamo pedestrians: {result.Count} characters, walk clip '{walk.name}'");
            return result.Count > 0 ? result.ToArray() : null;
        }
    }
}
