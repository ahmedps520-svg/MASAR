using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>Checks the generated city scene for renderers whose mesh is missing or empty (QA).</summary>
    public static class CityDiagnostics
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(OsmCityBuilder.TahliaScene, OpenSceneMode.Single);
            var byMat = new Dictionary<string, (int ok, int missing, int empty)>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var r = mf.GetComponent<Renderer>();
                string mat = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "(none)";
                byMat.TryGetValue(mat, out var c);
                if (mf.sharedMesh == null) c.missing++;
                else if (mf.sharedMesh.vertexCount == 0) c.empty++;
                else c.ok++;
                byMat[mat] = c;
            }
            foreach (var kv in byMat)
                if (kv.Value.missing > 0 || kv.Value.empty > 0 || kv.Key.Contains("Stone") || kv.Key.Contains("ShopGlass") || kv.Key.Contains("Sign0"))
                    Debug.Log($"[MASAR diag] {kv.Key}: ok {kv.Value.ok}, missing mesh {kv.Value.missing}, empty mesh {kv.Value.empty}");
        }
    }
}
