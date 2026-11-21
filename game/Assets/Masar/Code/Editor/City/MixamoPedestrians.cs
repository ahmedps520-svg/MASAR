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
            var characters = fbx.FindAll(p => p != walkPath && !IsAnim(p) && !Lower(p).Contains("walk"));
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
