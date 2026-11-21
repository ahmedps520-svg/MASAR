using System.Collections.Generic;
using Masar.World;
using Masar.World.Traffic;
using UnityEditor;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Prefabs for the AI traffic and the pedestrians.
    /// - Traffic cars: the project's own generic test sedan (self-made, unbranded), stripped to a
    ///   visual body (no interior, no physics model, no lights) in common Jeddah paint colors.
    /// - Pedestrians: PLACEHOLDER figures built from simple shapes (thobe with ghutra, abaya,
    ///   casual clothes, work overalls), animated procedurally. To be replaced by imported characters.
    /// </summary>
    public static class TrafficKit
    {
        const string CarDir = "Assets/Masar/Prefabs/Traffic";
        const string PedDir = "Assets/Masar/Prefabs/Pedestrians";
        const string PedMeshDir = "Assets/Masar/Generated/Pedestrians";

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ cars

        public static TrafficCar[] BuildTrafficCars()
        {
            Folder(CarDir);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(TestCarBuilder.PrefabPath) ?? TestCarBuilder.BuildPrefab();
            var colors = new (string name, Color c, float metal, float smooth)[]
            {
                ("White", new Color(0.86f, 0.86f, 0.85f), 0.1f, 0.65f),
                ("PearlWhite", new Color(0.83f, 0.84f, 0.83f), 0.3f, 0.6f),
                ("Silver", new Color(0.60f, 0.61f, 0.63f), 0.75f, 0.6f),
                ("Black", new Color(0.03f, 0.03f, 0.035f), 0.4f, 0.7f),
                ("Beige", new Color(0.70f, 0.64f, 0.53f), 0.45f, 0.6f),
                ("Gray", new Color(0.28f, 0.29f, 0.30f), 0.6f, 0.6f),
                ("NavyBlue", new Color(0.05f, 0.10f, 0.22f), 0.55f, 0.65f),
                ("Red", new Color(0.48f, 0.05f, 0.04f), 0.4f, 0.65f),
            };
            var result = new List<TrafficCar>();
            foreach (var (name, color, metal, smooth) in colors)
            {
                var paint = MaterialLibrary.CarPaint($"Traffic_Paint_{name}", color, metal, smooth);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = $"TrafficCar_{name}";
                // Strip everything but the visual body
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
                foreach (var l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var n in new[] { "Interior", "CabinCollider", "SteeringColumn", "SteeringWheel" })
                {
                    var t = FindDeep(go.transform, n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "Car_Paint_PearlWhite") mats[i] = paint;
                    r.sharedMaterials = mats;
                }
                var tc = go.AddComponent<TrafficCar>();
                string[] wn = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
                for (int i = 0; i < 4; i++) tc.wheels[i] = FindDeep(go.transform, wn[i]);
                if (tc.wheels[0] != null && tc.wheels[2] != null)
                {
                    tc.wheelRadius = tc.wheels[0].localPosition.y;
                    tc.wheelbase = Mathf.Abs(tc.wheels[0].localPosition.z - tc.wheels[2].localPosition.z);
                }
                var tails = new List<Renderer>();
                foreach (var n in new[] { "Taillight_L", "Taillight_R" }) { var t = FindDeep(go.transform, n); if (t != null && t.GetComponent<Renderer>() != null) tails.Add(t.GetComponent<Renderer>()); }
                tc.brakeLights = tails.ToArray();
                var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var b = LocalBounds(go);
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center; box.size = b.size;
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{CarDir}/{go.name}.prefab");
                Object.DestroyImmediate(go);
                result.Add(prefab.GetComponent<TrafficCar>());
            }
            return result.ToArray();
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }

        static Bounds LocalBounds(GameObject go)
        {
            var b = new Bounds(Vector3.up * 0.7f, Vector3.zero); bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var p = go.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        // ------------------------------------------------------------------ pedestrians (placeholders)

        enum Outfit { Thobe, Abaya, Casual, Overalls }

        public static Pedestrian[] BuildPedestrians()
        {
            Folder(PedDir); Folder(PedMeshDir);
            var skin = new[]
            {
                MaterialLibrary.Solid("Ped_Skin_A", new Color(0.62f, 0.45f, 0.34f), 0.35f),
                MaterialLibrary.Solid("Ped_Skin_B", new Color(0.48f, 0.33f, 0.24f), 0.35f),
                MaterialLibrary.Solid("Ped_Skin_C", new Color(0.36f, 0.24f, 0.17f), 0.35f),
            };
            var white = MaterialLibrary.Solid("Ped_ClothWhite", new Color(0.92f, 0.92f, 0.9f), 0.2f);
            var black = MaterialLibrary.Solid("Ped_ClothBlack", new Color(0.03f, 0.03f, 0.035f), 0.3f);
            var red = MaterialLibrary.Solid("Ped_ShemaghRed", new Color(0.62f, 0.10f, 0.10f), 0.2f);
            var jeans = MaterialLibrary.Solid("Ped_Jeans", new Color(0.12f, 0.17f, 0.28f), 0.25f);
            var khaki = MaterialLibrary.Solid("Ped_Khaki", new Color(0.55f, 0.50f, 0.38f), 0.25f);
            var shoes = MaterialLibrary.Solid("Ped_Shoes", new Color(0.1f, 0.08f, 0.07f), 0.4f);
            var shirts = new[]
            {
                MaterialLibrary.Solid("Ped_Shirt_Blue", new Color(0.18f, 0.32f, 0.55f), 0.2f),
                MaterialLibrary.Solid("Ped_Shirt_Gray", new Color(0.55f, 0.56f, 0.57f), 0.2f),
                MaterialLibrary.Solid("Ped_Shirt_Green", new Color(0.18f, 0.35f, 0.25f), 0.2f),
            };
            var overalls = MaterialLibrary.Solid("Ped_Overalls", new Color(0.12f, 0.25f, 0.48f), 0.25f);

            var list = new List<Pedestrian>
            {
                Ped("Ped_Thobe_A", Outfit.Thobe, white, white, red, skin[0], shoes),
                Ped("Ped_Thobe_B", Outfit.Thobe, white, white, white, skin[1], shoes),
                Ped("Ped_Thobe_C", Outfit.Thobe, white, white, red, skin[2], shoes),
                Ped("Ped_Abaya_A", Outfit.Abaya, black, black, black, skin[0], black),
                Ped("Ped_Abaya_B", Outfit.Abaya, black, black, black, skin[1], black),
                Ped("Ped_Casual_A", Outfit.Casual, shirts[0], jeans, null, skin[1], shoes),
                Ped("Ped_Casual_B", Outfit.Casual, shirts[1], khaki, null, skin[2], shoes),
                Ped("Ped_Casual_C", Outfit.Casual, shirts[2], jeans, null, skin[0], shoes),
                Ped("Ped_Worker", Outfit.Overalls, overalls, overalls, null, skin[2], shoes),
            };
            return list.ToArray();
        }

        static Pedestrian Ped(string name, Outfit o, Material top, Material bottom, Material head, Material skin, Material feet)
        {
            var root = new GameObject(name);
            var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
            bool robe = o == Outfit.Thobe || o == Outfit.Abaya;
            float hip = 0.92f, shoulder = 1.43f;

            // Torso / robe
            var mb = new MeshBuilder();
            if (robe)
            {
                mb.Cylinder(new Vector3(0, 0.05f, 0), 0.27f, 0.19f, shoulder - 0.05f, 14, true);
                Save(mb, body, "Robe", top, name);
            }
            else
            {
                mb.Cylinder(new Vector3(0, hip - 0.05f, 0), 0.17f, 0.2f, shoulder - hip + 0.05f, 12, true);
                Save(mb, body, "Torso", top, name);
            }
            // Neck + head
            mb.Cylinder(new Vector3(0, shoulder, 0), 0.055f, 0.05f, 0.1f, 8, false);
            Sphere(mb, new Vector3(0, shoulder + 0.2f, 0.01f), 0.105f, 0.12f);
            Save(mb, body, "Head", o == Outfit.Abaya ? skin : skin, name);
            if (o == Outfit.Thobe)
            {
                // Ghutra / shemagh draped over the head and shoulders, black agal ring
                mb.Cylinder(new Vector3(0, shoulder - 0.02f, -0.02f), 0.2f, 0.115f, 0.37f, 12, true);
                Save(mb, body, "Ghutra", head, name);
                mb.CylinderAxis(new Vector3(0, shoulder + 0.29f, 0.0f), Vector3.up, 0.11f, 0.025f, 14, true);
                Save(mb, body, "Agal", MaterialLibrary.Solid("Ped_Agal", new Color(0.02f, 0.02f, 0.02f), 0.3f), name);
            }
            else if (o == Outfit.Abaya)
            {
                // Head cover leaving the face open
                mb.Cylinder(new Vector3(0, shoulder - 0.03f, -0.035f), 0.17f, 0.12f, 0.36f, 12, true);
                Save(mb, body, "Hijab", head, name);
            }
            else
            {
                Sphere(mb, new Vector3(0, shoulder + 0.25f, -0.005f), 0.108f, 0.07f);   // hair
                Save(mb, body, "Hair", MaterialLibrary.Solid("Ped_Hair", new Color(0.05f, 0.04f, 0.035f), 0.3f), name);
            }

            // Arms (pivot at the shoulder)
            Transform Arm(float side)
            {
                var pivot = new GameObject(side < 0 ? "ArmL" : "ArmR").transform;
                pivot.SetParent(body, false);
                pivot.localPosition = new Vector3(side * 0.23f, shoulder - 0.04f, 0f);
                var a = new MeshBuilder();
                a.BoxC(new Vector3(0, -0.29f, 0), new Vector3(0.09f, 0.58f, 0.1f));
                Save(a, pivot, "Sleeve", robe ? top : o == Outfit.Overalls ? top : skin, name + (side < 0 ? "L" : "R"));
                a.BoxC(new Vector3(0, -0.63f, 0.005f), new Vector3(0.07f, 0.1f, 0.08f));
                Save(a, pivot, "Hand", skin, name + (side < 0 ? "L" : "R"));
                if (!robe && o != Outfit.Overalls)
                {
                    a.BoxC(new Vector3(0, -0.1f, 0), new Vector3(0.105f, 0.2f, 0.115f));   // t-shirt sleeve
                    Save(a, pivot, "ShortSleeve", top, name + (side < 0 ? "L" : "R"));
                }
                return pivot;
            }
            // Legs (pivot at the hip); under a robe only the feet show
            Transform Leg(float side)
            {
                var pivot = new GameObject(side < 0 ? "LegL" : "LegR").transform;
                pivot.SetParent(body, false);
                pivot.localPosition = new Vector3(side * 0.095f, hip, 0f);
                var l = new MeshBuilder();
                if (!robe)
                {
                    l.BoxC(new Vector3(0, -hip * 0.5f + 0.04f, 0), new Vector3(0.13f, hip - 0.08f, 0.14f));
                    Save(l, pivot, "Leg", bottom, name + (side < 0 ? "L" : "R"));
                }
                l.BoxC(new Vector3(0, -hip + 0.04f, 0.05f), new Vector3(0.1f, 0.08f, 0.26f));
                Save(l, pivot, "Foot", feet, name + (side < 0 ? "L" : "R"));
                return pivot;
            }
            var ped = root.AddComponent<Pedestrian>();
            ped.body = body;
            ped.armL = Arm(-1); ped.armR = Arm(1);
            ped.legL = Leg(-1); ped.legR = Leg(1);
            if (robe) { ped.legSwing = 18f; ped.armSwing = 14f; }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var cap = root.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.88f, 0); cap.height = 1.75f; cap.radius = 0.25f;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PedDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Pedestrian>();
        }

        static void Sphere(MeshBuilder b, Vector3 c, float r, float ry)
        {
            const int seg = 12, rings = 8;
            var rs = new List<Vector3[]>();
            for (int i = 1; i < rings; i++)
            {
                float th = Mathf.PI * i / rings;
                var ring = new Vector3[seg];
                for (int k = 0; k < seg; k++)
                {
                    float ph = 2f * Mathf.PI * k / seg;
                    ring[k] = c + new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * r, Mathf.Cos(th) * (r + ry) * 0.5f + Mathf.Cos(th) * ry * 0.5f, Mathf.Sin(th) * Mathf.Sin(ph) * r);
                }
                rs.Add(ring);
            }
            int start = b.Count;
            b.Loft(rs, true);
            b.OrientOutward(start, _ => c);
            // Caps
            b.Fan(rs[0], Vector3.up);
            b.Fan(rs[rs.Count - 1], Vector3.down);
        }

        static void Save(MeshBuilder mb, Transform parent, string part, Material mat, string owner)
        {
            if (mb.Count == 0) return;
            mb.FixWinding();
            var go = new GameObject(part);
            go.transform.SetParent(parent, false);
            var mesh = mb.ToMesh($"{owner}_{part}");
            string path = $"{PedMeshDir}/{mesh.name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            mb.Clear();
        }
    }
}
