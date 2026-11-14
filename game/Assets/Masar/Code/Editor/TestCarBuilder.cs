using System.Collections.Generic;
using Masar.Cameras;
using Masar.Vehicles;
using Masar.World;
using UnityEditor;
using UnityEngine;

namespace Masar.EditorTools
{
    /// <summary>
    /// Generates the Phase 0 test vehicle: a generic, unbranded mid-size sedan built from
    /// lofted sections, with a cabin (dashboard, seats, steering wheel, gauges),
    /// wheels, lights and camera anchors. PLACEHOLDER ART: it exists to prove the physics,
    /// camera and material pipeline. The production vehicle is a Month 2 deliverable.
    /// </summary>
    public static class TestCarBuilder
    {
        public const string PrefabPath = "Assets/Masar/Prefabs/TestSedan.prefab";
        public const string DefinitionPath = "Assets/Masar/Data/TestSedan.asset";
        const string MeshDir = "Assets/Masar/Generated/Meshes";

        // Overall shape (meters). Origin on the ground at the center of the wheelbase. +Z forward.
        const float ZRear = -2.42f, ZFront = 2.43f;
        const float AxleF = 1.41f, AxleR = -1.41f;
        const float WheelR = 0.33f, WheelW = 0.215f, TrackHalf = 0.79f;

        [MenuItem("MASAR/Build/Test Vehicle")]
        public static GameObject BuildPrefab()
        {
            MaterialLibrary.EnsureFolders();
            var def = CreateDefinition();

            var paint = MaterialLibrary.CarPaint("Car_Paint_PearlWhite", new Color(0.83f, 0.84f, 0.83f), 0.3f, 0.6f);
            var glass = MaterialLibrary.Glass("Car_Glass", new Color(0.04f, 0.06f, 0.07f, 0.5f));
            var trim = MaterialLibrary.Solid("Car_Trim_Black", new Color(0.025f, 0.025f, 0.028f), 0.45f);
            var chrome = MaterialLibrary.Solid("Car_Chrome", new Color(0.85f, 0.85f, 0.86f), 0.93f, 1f);
            var tire = MaterialLibrary.Solid("Car_Tire", new Color(0.03f, 0.03f, 0.03f), 0.22f);
            var rimMat = MaterialLibrary.Solid("Car_Rim", new Color(0.62f, 0.63f, 0.66f), 0.78f, 1f);
            var brakeMat = MaterialLibrary.Solid("Car_BrakeDisc", new Color(0.25f, 0.24f, 0.23f), 0.55f, 1f);
            var interior = MaterialLibrary.Solid("Car_Interior", new Color(0.07f, 0.07f, 0.075f), 0.22f, 0f, true);
            var seatMat = MaterialLibrary.Solid("Car_Seat", new Color(0.42f, 0.37f, 0.31f), 0.15f);
            var headliner = MaterialLibrary.Solid("Car_Headliner", new Color(0.58f, 0.56f, 0.52f), 0.08f, 0f, true);
            var headMat = MaterialLibrary.Emissive("Car_Headlight", new Color(0.9f, 0.9f, 0.93f), Color.black, 0.96f);
            var tailMat = MaterialLibrary.Emissive("Car_Taillight", new Color(0.38f, 0.015f, 0.015f), Color.black, 0.92f);
            var revMat = MaterialLibrary.Emissive("Car_ReverseLight", new Color(0.8f, 0.8f, 0.82f), Color.black, 0.92f);
            var plateMat = MaterialLibrary.Solid("Car_Plate", new Color(0.9f, 0.9f, 0.88f), 0.45f);
            var gaugeMat = MaterialLibrary.Emissive("Car_Gauges", new Color(0.03f, 0.04f, 0.05f), new Color(0.15f, 0.45f, 0.9f) * 0.5f, 0.9f);

            var root = new GameObject("TestSedan");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = def.body.massKg;
            var controller = root.AddComponent<VehicleController>();
            var so = new SerializedObject(controller);
            so.FindProperty("definition").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
            var visuals = root.AddComponent<VehicleVisuals>();

            // ---- colliders (body + cabin)
            var bodyCol = root.AddComponent<BoxCollider>();
            bodyCol.center = new Vector3(0f, 0.68f, 0.0f);
            bodyCol.size = new Vector3(1.80f, 0.62f, 4.78f);
            var cabinCol = new GameObject("CabinCollider").AddComponent<BoxCollider>();
            cabinCol.transform.SetParent(root.transform, false);
            cabinCol.center = new Vector3(0f, 1.2f, -0.15f);
            cabinCol.size = new Vector3(1.4f, 0.45f, 2.1f);

            var bodyGo = Part(root.transform, "Body");

            // ---- exterior shell
            var mb = new MeshBuilder();
            BuildLowerBody(mb);
            Save(mb, bodyGo, "Sedan_BodyPaint", paint);

            mb.Clear(); BuildGreenhouse(mb, out var pillars);
            Save(mb, bodyGo, "Sedan_Glass", glass);
            Save(pillars, bodyGo, "Sedan_Pillars", paint);

            mb.Clear(); BuildTrim(mb);
            Save(mb, bodyGo, "Sedan_Trim", trim);

            mb.Clear(); BuildChrome(mb);
            Save(mb, bodyGo, "Sedan_Chrome", chrome);

            mb.Clear();
            mb.BoxC(new Vector3(0f, 0.44f, ZFront - 0.01f), new Vector3(0.52f, 0.12f, 0.03f)); // front plate
            mb.BoxC(new Vector3(0f, 0.62f, ZRear + 0.01f), new Vector3(0.52f, 0.12f, 0.03f));  // rear plate
            Save(mb, bodyGo, "Sedan_Plates", plateMat);

            // ---- lights (separate renderers so the visuals component can drive emission)
            var headL = Light(bodyGo.transform, "Headlight_L", headMat, new Vector3(-0.60f, 0.77f, ZFront - 0.14f), new Vector3(0.42f, 0.10f, 0.20f), -8f);
            var headR = Light(bodyGo.transform, "Headlight_R", headMat, new Vector3(0.60f, 0.77f, ZFront - 0.14f), new Vector3(0.42f, 0.10f, 0.20f), 8f);
            var tailL = Light(bodyGo.transform, "Taillight_L", tailMat, new Vector3(-0.66f, 0.88f, ZRear + 0.07f), new Vector3(0.40f, 0.11f, 0.14f), 6f);
            var tailR = Light(bodyGo.transform, "Taillight_R", tailMat, new Vector3(0.66f, 0.88f, ZRear + 0.07f), new Vector3(0.40f, 0.11f, 0.14f), -6f);
            var revL = Light(bodyGo.transform, "Reverse_L", revMat, new Vector3(-0.36f, 0.86f, ZRear + 0.03f), new Vector3(0.12f, 0.07f, 0.05f), 0f);
            var revR = Light(bodyGo.transform, "Reverse_R", revMat, new Vector3(0.36f, 0.86f, ZRear + 0.03f), new Vector3(0.12f, 0.07f, 0.05f), 0f);

            var spots = new List<UnityEngine.Light>();
            foreach (var x in new[] { -0.6f, 0.6f })
            {
                var lgo = new GameObject(x < 0 ? "HeadlightBeam_L" : "HeadlightBeam_R");
                lgo.transform.SetParent(root.transform, false);
                lgo.transform.localPosition = new Vector3(x, 0.77f, ZFront - 0.05f);
                lgo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                var l = lgo.AddComponent<UnityEngine.Light>();
                l.type = LightType.Spot;
                l.range = 60f;
                l.spotAngle = 70f;
                l.innerSpotAngle = 30f;
                l.intensity = 30f;
                l.color = new Color(1f, 0.96f, 0.9f);
                l.shadows = LightShadows.None;
                spots.Add(l);
            }

            // ---- interior
            var interiorGo = Part(root.transform, "Interior");
            mb.Clear(); BuildInterior(mb);
            Save(mb, interiorGo, "Sedan_Interior", interior);
            mb.Clear(); BuildSeats(mb);
            Save(mb, interiorGo, "Sedan_Seats", seatMat);
            mb.Clear(); BuildHeadliner(mb);
            Save(mb, interiorGo, "Sedan_Headliner", headliner);
            mb.Clear(); mb.BoxC(new Vector3(-0.37f, 0.96f, 0.705f), new Vector3(0.36f, 0.11f, 0.01f));
            Save(mb, interiorGo, "Sedan_Gauges", gaugeMat);

            // Steering wheel: column (tilted) -> pivot (rotates) -> mesh
            var column = new GameObject("SteeringColumn").transform;
            column.SetParent(interiorGo.transform, false);
            column.localPosition = new Vector3(-0.37f, 0.97f, 0.50f);
            column.localRotation = Quaternion.LookRotation(new Vector3(0f, Mathf.Sin(24f * Mathf.Deg2Rad), -Mathf.Cos(24f * Mathf.Deg2Rad)), Vector3.up);
            var wheelPivot = new GameObject("SteeringWheel").transform;
            wheelPivot.SetParent(column, false);
            mb.Clear(); BuildSteeringWheel(mb);
            Save(mb, wheelPivot.gameObject, "Sedan_SteeringWheel", interior);

            // ---- wheels
            var wheelMeshTire = new MeshBuilder(); BuildTire(wheelMeshTire);
            var tireMesh = SaveMesh(wheelMeshTire, "Sedan_Tire");
            var wheelMeshRim = new MeshBuilder(); BuildRim(wheelMeshRim);
            var rimMesh = SaveMesh(wheelMeshRim, "Sedan_Rim");
            var discB = new MeshBuilder(); discB.CylinderAxis(Vector3.zero, Vector3.right, 0.17f, 0.028f, 28, true);
            var discMesh = SaveMesh(discB, "Sedan_BrakeDisc");
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            var wheels = new Transform[4];
            var sim = new VehicleSimulation(def);
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject(names[i]).transform;
                pivot.SetParent(root.transform, false);
                var m = sim.MountPoint(i);
                pivot.localPosition = new Vector3(m.x, WheelR, m.z);
                var meshes = new GameObject("Mesh").transform;
                meshes.SetParent(pivot, false);
                bool left = i == 0 || i == 2;
                meshes.localScale = new Vector3(left ? -1f : 1f, 1f, 1f); // outer face points outward
                Child(meshes, "Tire", tireMesh, tire);
                Child(meshes, "Rim", rimMesh, rimMat);
                var disc = Child(meshes, "Disc", discMesh, brakeMat);
                disc.transform.localPosition = new Vector3(-0.03f, 0f, 0f);
                wheels[i] = pivot;
            }

            // ---- camera anchors
            var anchors = root.AddComponent<VehicleCameraAnchors>();
            anchors.driverEye = Anchor(root.transform, "Cam_DriverEye", new Vector3(-0.37f, 1.17f, -0.22f));
            anchors.passengerEye = Anchor(root.transform, "Cam_PassengerEye", new Vector3(0.37f, 1.15f, -0.22f));
            anchors.hood = Anchor(root.transform, "Cam_Hood", new Vector3(0f, 1.08f, 1.30f));
            anchors.lookTarget = Anchor(root.transform, "Cam_LookTarget", new Vector3(0f, 1.0f, 0.2f));

            // ---- wire visuals
            visuals.wheels = wheels;
            visuals.steeringWheel = wheelPivot;
            visuals.brakeLightRenderers = new[] { tailL, tailR };
            visuals.reverseLightRenderers = new[] { revL, revR };
            visuals.headlightRenderers = new[] { headL, headR };
            visuals.headlights = spots.ToArray();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MASAR] Test vehicle prefab written: {PrefabPath}");
            return prefab;
        }

        // ------------------------------------------------------------------ definition

        private static VehicleDefinition CreateDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(DefinitionPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<VehicleDefinition>();
                AssetDatabase.CreateAsset(def, DefinitionPath);
            }
            def.displayName = "MASAR Test Sedan (generic)";
            def.notes = "Generic mid-size FWD sedan, roughly 1.5 t, 2.5 L NA four-cylinder class, 6-speed automatic. " +
                        "Values are plausible engineering estimates for a test vehicle, not data for any real car model.";
            def.body.massKg = 1480f;
            def.body.centerOfMass = new Vector3(0f, 0.52f, 0.2f);
            def.body.wheelbase = 2.82f;
            def.body.trackFront = 1.58f;
            def.body.trackRear = 1.57f;
            def.body.mountHeight = 0.56f;
            def.drivetrain.layout = DrivetrainLayout.FrontWheelDrive;
            def.frontTires.radius = WheelR;
            def.rearTires.radius = WheelR;
            EditorUtility.SetDirty(def);
            return def;
        }

        // ------------------------------------------------------------------ shape functions

        private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Top surface height of the lower body (hood, belt line, trunk deck).</summary>
        private static float TopY(float z)
        {
            float y = Mathf.Lerp(1.02f, 0.985f, Smooth(-1.4f, 1.2f, z));          // wedge belt line
            y = Mathf.Lerp(y, 0.86f, Smooth(1.25f, 2.3f, z));                      // hood slopes down to the nose
            y = Mathf.Lerp(y, 0.64f, Smooth(2.25f, ZFront, z));                    // nose rounding
            y = Mathf.Lerp(y, 0.80f, Smooth(-2.2f, ZRear, z));                     // tail rounding
            return y;
        }

        private static float BottomY(float z)
        {
            float y = 0.30f;
            y = Mathf.Lerp(y, 0.36f, Smooth(1.9f, ZFront, z));
            y = Mathf.Lerp(y, 0.38f, Smooth(-2.0f, ZRear, z));
            foreach (float ax in new[] { AxleF, AxleR })
            {
                float dz = z - ax;
                const float r = 0.43f;
                if (Mathf.Abs(dz) < r) y = Mathf.Max(y, WheelR + Mathf.Sqrt(r * r - dz * dz) * 0.97f);
            }
            return y;
        }

        private static float HalfWidth(float z)
        {
            float w = 0.92f;
            if (z > 1.7f) w -= 0.13f * Mathf.Pow((z - 1.7f) / (ZFront - 1.7f), 2f);
            if (z < -1.8f) w -= 0.08f * Mathf.Pow((z - -1.8f) / (ZRear - -1.8f), 2f);
            return w;
        }

        private static Vector3 Super(float hw, float bottom, float top, float z, float thetaDeg)
        {
            const float n = 6f;
            float th = thetaDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(th), s = Mathf.Sin(th);
            float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
            float y = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / n);
            float mid = 0.5f * (top + bottom), hh = 0.5f * (top - bottom);
            float tumble = 1f - 0.05f * Mathf.Max(0f, y);  // slight inward lean toward the top
            return new Vector3(x * hw * tumble, mid + y * hh, z);
        }

        private const float BeltTheta = 62f;

        /// <summary>Lower body "U" (sides + underside) everywhere, plus hood and trunk lids, plus end caps.</summary>
        private static void BuildLowerBody(MeshBuilder mb)
        {
            int start = mb.Count;
            const int rows = 56;
            var uRings = new List<Vector3[]>();
            var hoodRings = new List<Vector3[]>();
            var trunkRings = new List<Vector3[]>();
            const int uPts = 30, lidPts = 10;
            for (int r = 0; r < rows; r++)
            {
                float z = Mathf.Lerp(ZRear, ZFront, r / (float)(rows - 1));
                float hw = HalfWidth(z), top = TopY(z), bot = BottomY(z);
                var ring = new Vector3[uPts];
                for (int i = 0; i < uPts; i++)
                {
                    float th = Mathf.Lerp(180f - BeltTheta, 360f + BeltTheta, i / (float)(uPts - 1));
                    ring[i] = Super(hw, bot, top, z, th);
                }
                uRings.Add(ring);
                var lid = new Vector3[lidPts];
                for (int i = 0; i < lidPts; i++) lid[i] = Super(hw, bot, top, z, Mathf.Lerp(BeltTheta, 180f - BeltTheta, i / (float)(lidPts - 1)));
                if (z >= 1.16f) hoodRings.Add(lid);
                if (z <= -1.24f) trunkRings.Add(lid);
            }
            mb.Loft(uRings, false);
            if (hoodRings.Count > 1) mb.Loft(hoodRings, false);
            if (trunkRings.Count > 1) mb.Loft(trunkRings, false);
            mb.OrientOutward(start, p => new Vector3(0f, 0.5f * (TopY(p.z) + BottomY(p.z)), p.z));
            mb.FixWinding();

            // End caps (full ring at the rear and front faces)
            foreach (var z in new[] { ZRear, ZFront })
            {
                float hw = HalfWidth(z), top = TopY(z), bot = BottomY(z);
                var ring = new Vector3[36];
                for (int i = 0; i < 36; i++) ring[i] = Super(hw, bot, top, z, i * 10f);
                mb.Fan(ring, z < 0 ? Vector3.back : Vector3.forward);
            }
        }

        /// <summary>Roof line of the greenhouse (cabin glass) for z in [-1.30, 1.22].</summary>
        private static float RoofY(float z)
        {
            float y = Mathf.Lerp(TopY(-1.30f), 1.425f, Smooth(-1.30f, -0.60f, z));   // rear window
            y = Mathf.Lerp(y, TopY(1.22f), Smooth(0.40f, 1.22f, z));                  // windshield
            y += 0.012f * Mathf.Sin(Mathf.InverseLerp(-0.6f, 0.4f, z) * Mathf.PI) * (z > -0.6f && z < 0.4f ? 1f : 0f);
            return y;
        }

        private static void BuildGreenhouse(MeshBuilder glass, out MeshBuilder pillars)
        {
            pillars = new MeshBuilder();
            const int rows = 30, pts = 13;
            var rings = new List<Vector3[]>();
            const float z0 = -1.30f, z1 = 1.22f;
            for (int r = 0; r < rows; r++)
            {
                float z = Mathf.Lerp(z0, z1, r / (float)(rows - 1));
                rings.Add(GreenhouseRing(z, pts, 0f));
            }
            int gs = glass.Count;
            glass.Loft(rings, false);
            glass.OrientOutward(gs, p => new Vector3(0f, 0.7f, p.z));
            glass.FixWinding();

            // Roof panel (paint) over the flat part of the roof, and pillar strips at the glass edges
            var roof = new List<Vector3[]>();
            for (int r = 0; r < 16; r++)
            {
                float z = Mathf.Lerp(-0.66f, 0.46f, r / 15f);
                var full = GreenhouseRing(z, pts, 0.008f);
                var arc = new Vector3[7];
                for (int i = 0; i < 7; i++) arc[i] = full[3 + i];
                roof.Add(arc);
            }
            int ps = pillars.Count;
            pillars.Loft(roof, false);
            // A-pillars, C-pillars and B-pillars: strips covering the two outermost ring segments
            AddPillar(pillars, 0.40f, 1.22f, 18, pts);   // A
            AddPillar(pillars, -1.30f, -0.60f, 14, pts); // C
            AddPillar(pillars, -0.16f, -0.04f, 3, pts, full: true); // B
            pillars.OrientOutward(ps, p => new Vector3(0f, 0.7f, p.z));
            pillars.FixWinding();
        }

        private static void AddPillar(MeshBuilder mb, float z0, float z1, int rows, int pts, bool full = false)
        {
            foreach (int side in new[] { 0, 1 })
            {
                var rings = new List<Vector3[]>();
                for (int r = 0; r < rows; r++)
                {
                    float z = Mathf.Lerp(z0, z1, r / (float)(rows - 1));
                    var ring = GreenhouseRing(z, pts, 0.01f);
                    int n = full ? 4 : 2;
                    var strip = new Vector3[n];
                    for (int i = 0; i < n; i++) strip[i] = side == 0 ? ring[i] : ring[pts - 1 - i];
                    rings.Add(strip);
                }
                mb.Loft(rings, false);
            }
        }

        private static Vector3[] GreenhouseRing(float z, int pts, float inflate)
        {
            float belt = TopY(z);
            float roof = Mathf.Max(belt + 0.001f, RoofY(z));
            float hwBelt = Super(HalfWidth(z), BottomY(z), TopY(z), z, BeltTheta).x - 0.01f;
            float hwTop = 0.63f;
            var ring = new Vector3[pts];
            for (int i = 0; i < pts; i++)
            {
                float t = i / (float)(pts - 1);           // 0 = left belt, 1 = right belt
                float a = Mathf.Lerp(Mathf.PI, 0f, t);    // semicircle-ish over the roof
                float x = Mathf.Cos(a);
                float s = Mathf.Sin(a);
                float sideT = Mathf.Pow(s, 0.35f);        // flattens the roof
                float hw = Mathf.Lerp(hwBelt, hwTop, sideT);
                ring[i] = new Vector3(Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 0.6f) * hw + Mathf.Sign(x) * inflate,
                                      belt + (roof - belt) * sideT + inflate, z);
            }
            return ring;
        }

        private static void BuildTrim(MeshBuilder mb)
        {
            // Grille and lower intake
            mb.BoxC(new Vector3(0f, 0.60f, ZFront - 0.02f), new Vector3(0.80f, 0.13f, 0.05f));
            mb.BoxC(new Vector3(0f, 0.43f, ZFront - 0.06f), new Vector3(1.20f, 0.12f, 0.06f));
            // Side skirts between the arches
            foreach (float x in new[] { -0.905f, 0.905f })
                mb.BoxC(new Vector3(x, 0.33f, 0f), new Vector3(0.03f, 0.07f, 1.95f));
            // Rear diffuser
            mb.BoxC(new Vector3(0f, 0.42f, ZRear + 0.05f), new Vector3(1.30f, 0.09f, 0.06f));
            // Wheel-arch liners: dark upper arcs above each wheel, facing the tire
            foreach (float az in new[] { AxleF, AxleR })
                foreach (float x in new[] { -0.80f, 0.80f })
                    ArchLiner(mb, new Vector3(x, WheelR, az), 0.425f, 0.2f);
            // Chassis tub so the space between the wheels never shows daylight
            mb.Box(new Vector3(-0.66f, 0.18f, ZRear + 0.25f), new Vector3(0.66f, 0.80f, ZFront - 0.3f), 1f, true);
            // Mirrors
            foreach (float x in new[] { -1f, 1f })
            {
                mb.BoxC(new Vector3(x * 0.97f, 1.07f, 0.98f), new Vector3(0.10f, 0.11f, 0.20f));
                mb.BoxC(new Vector3(x * 0.90f, 1.03f, 1.02f), new Vector3(0.08f, 0.04f, 0.08f));
            }
            // Side-window frames (thin black strips along the belt)
            foreach (float x in new[] { -1f, 1f })
                mb.BoxC(new Vector3(x * 0.74f, TopY(0f) + 0.012f, -0.05f), new Vector3(0.025f, 0.02f, 2.45f));
        }

        private static void BuildChrome(MeshBuilder mb)
        {
            foreach (float x in new[] { -0.928f, 0.928f })
                foreach (float z in new[] { 0.32f, -0.72f })
                    mb.BoxC(new Vector3(x, 0.92f, z), new Vector3(0.02f, 0.03f, 0.16f));
            // Exhaust tips
            foreach (float x in new[] { -0.45f, 0.45f })
                mb.CylinderAxis(new Vector3(x, 0.36f, ZRear + 0.02f), Vector3.forward, 0.04f, 0.08f, 14, true);
        }

        private static Renderer Light(Transform parent, string name, Material mat, Vector3 pos, Vector3 size, float yaw)
        {
            var mb = new MeshBuilder();
            mb.BoxC(Vector3.zero, size);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mb, "Sedan_" + name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ------------------------------------------------------------------ interior

        private static void BuildInterior(MeshBuilder mb)
        {
            mb.Box(new Vector3(-0.82f, 0.30f, -1.35f), new Vector3(0.82f, 0.38f, 1.12f));      // floor
            mb.Box(new Vector3(-0.84f, 0.78f, 0.70f), new Vector3(0.84f, 1.00f, 1.14f));       // dashboard
            mb.Box(new Vector3(-0.58f, 0.99f, 0.66f), new Vector3(-0.16f, 1.05f, 0.86f));      // cluster hood
            mb.Box(new Vector3(-0.12f, 0.38f, -0.40f), new Vector3(0.12f, 0.66f, 0.78f));      // center console
            mb.Box(new Vector3(-0.14f, 0.66f, 0.55f), new Vector3(0.14f, 0.92f, 0.72f));       // center stack
            mb.Cylinder(new Vector3(0f, 0.66f, 0.12f), 0.03f, 0.025f, 0.11f, 12);             // gear selector
            mb.Box(new Vector3(-0.90f, 0.36f, -1.30f), new Vector3(-0.84f, 1.00f, 1.10f));     // left door card
            mb.Box(new Vector3(0.84f, 0.36f, -1.30f), new Vector3(0.90f, 1.00f, 1.10f));       // right door card
            mb.Box(new Vector3(-0.80f, 0.38f, 1.10f), new Vector3(0.80f, 0.80f, 1.20f));       // firewall
            mb.Box(new Vector3(-0.85f, 0.60f, -1.80f), new Vector3(0.85f, 1.02f, -1.38f));     // parcel shelf / trunk wall
            mb.Box(new Vector3(-0.12f, 1.33f, 0.36f), new Vector3(0.12f, 1.38f, 0.42f));       // rear-view mirror
        }

        private static void BuildSeats(MeshBuilder mb)
        {
            foreach (float x in new[] { -0.37f, 0.37f })
            {
                mb.Box(new Vector3(x - 0.25f, 0.42f, -0.60f), new Vector3(x + 0.25f, 0.58f, -0.08f));      // cushion
                var back = Matrix4x4.TRS(new Vector3(x, 0.58f, -0.62f), Quaternion.Euler(-14f, 0f, 0f), Vector3.one);
                mb.Transform = back;
                mb.Box(new Vector3(-0.25f, 0f, -0.06f), new Vector3(0.25f, 0.62f, 0.06f));                 // backrest
                mb.Box(new Vector3(-0.14f, 0.64f, -0.05f), new Vector3(0.14f, 0.84f, 0.05f));              // headrest
                mb.Transform = Matrix4x4.identity;
            }
            mb.Box(new Vector3(-0.78f, 0.42f, -1.38f), new Vector3(0.78f, 0.58f, -0.92f));                 // rear bench
            mb.Transform = Matrix4x4.TRS(new Vector3(0f, 0.58f, -1.40f), Quaternion.Euler(-18f, 0f, 0f), Vector3.one);
            mb.Box(new Vector3(-0.78f, 0f, -0.06f), new Vector3(0.78f, 0.58f, 0.06f));
            mb.Transform = Matrix4x4.identity;
        }

        private static void BuildHeadliner(MeshBuilder mb)
        {
            var rings = new List<Vector3[]>();
            for (int r = 0; r < 16; r++)
            {
                float z = Mathf.Lerp(-1.0f, 0.75f, r / 15f);
                var g = GreenhouseRing(z, 13, -0.035f);
                var arc = new Vector3[7];
                for (int i = 0; i < 7; i++) arc[i] = g[3 + i];
                rings.Add(arc);
            }
            int s = mb.Count;
            mb.Loft(rings, false);
            // Inward-facing: normals toward the cabin center
            for (int i = s; i < mb.Count; i++) if (Vector3.Dot(mb.N[i], mb.V[i] - new Vector3(0f, 0.9f, mb.V[i].z)) > 0f) mb.N[i] = -mb.N[i];
            mb.FixWinding();
        }

        private static void BuildSteeringWheel(MeshBuilder mb)
        {
            // Rim as a torus in the pivot's XY plane (axis = local Z, toward the driver)
            const float R = 0.185f, r = 0.017f;
            const int seg = 36, tube = 10;
            int start = mb.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * R;
                Vector3 radial = c.normalized;
                for (int j = 0; j <= tube; j++)
                {
                    float b = j / (float)tube * Mathf.PI * 2f;
                    Vector3 n = radial * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    mb.AddVertex(c + n * r, n, new Vector2(i / (float)seg, j / (float)tube));
                }
            }
            int tStart = mb.T.Count;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tube; j++)
                {
                    int a = start + i * (tube + 1) + j, b = a + 1, d = a + tube + 1, e = d + 1;
                    mb.T.Add(a); mb.T.Add(d); mb.T.Add(e); mb.T.Add(a); mb.T.Add(e); mb.T.Add(b);
                }
            mb.FixWinding(tStart);
            mb.BoxC(new Vector3(0f, 0f, 0.01f), new Vector3(0.11f, 0.09f, 0.05f));                       // hub
            mb.BoxC(new Vector3(-0.10f, -0.01f, 0.005f), new Vector3(0.16f, 0.03f, 0.02f));              // spokes
            mb.BoxC(new Vector3(0.10f, -0.01f, 0.005f), new Vector3(0.16f, 0.03f, 0.02f));
            mb.BoxC(new Vector3(0f, -0.10f, 0.005f), new Vector3(0.03f, 0.13f, 0.02f));
        }

        /// <summary>Upper half-cylinder around a wheel (axis X), with normals facing the wheel.</summary>
        private static void ArchLiner(MeshBuilder mb, Vector3 c, float radius, float width)
        {
            var rings = new List<Vector3[]>();
            for (int s = 0; s <= 1; s++)
            {
                var ring = new Vector3[17];
                for (int i = 0; i < ring.Length; i++)
                {
                    float a = Mathf.Lerp(-15f, 195f, i / (float)(ring.Length - 1)) * Mathf.Deg2Rad;
                    ring[i] = c + new Vector3((s - 0.5f) * width, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius);
                }
                rings.Add(ring);
            }
            int start = mb.Count;
            mb.Loft(rings, false);
            for (int i = start; i < mb.Count; i++)
            {
                Vector3 toAxis = new Vector3(0f, c.y - mb.V[i].y, c.z - mb.V[i].z);
                if (Vector3.Dot(mb.N[i], toAxis) < 0f) mb.N[i] = -mb.N[i];
            }
            mb.FixWinding();
        }

        // ------------------------------------------------------------------ wheels

        private static void BuildTire(MeshBuilder mb)
        {
            // Profile runs from the inner bead (-x) over the tread to the outer bead (+x)
            float h = WheelW * 0.5f;
            var p = new[]
            {
                new Vector2(0.232f, -h + 0.01f), new Vector2(0.28f, -h - 0.004f), new Vector2(0.312f, -h + 0.006f),
                new Vector2(0.326f, -h + 0.03f), new Vector2(WheelR, -h + 0.06f), new Vector2(WheelR + 0.001f, 0f),
                new Vector2(WheelR, h - 0.06f), new Vector2(0.326f, h - 0.03f), new Vector2(0.312f, h - 0.006f),
                new Vector2(0.28f, h + 0.004f), new Vector2(0.232f, h - 0.01f)
            };
            mb.RevolveX(Vector3.zero, p, 40);
        }

        private static void BuildRim(MeshBuilder mb)
        {
            float h = WheelW * 0.5f;
            // Barrel + lip (outer face at +x)
            var barrel = new[]
            {
                new Vector2(0.226f, -h + 0.012f), new Vector2(0.226f, h - 0.03f), new Vector2(0.232f, h - 0.012f), new Vector2(0.215f, h - 0.006f)
            };
            mb.RevolveX(Vector3.zero, barrel, 32);
            // Hub face + 5 spokes + center cap, near the outer face
            float x = h - 0.035f;
            mb.CylinderAxis(new Vector3(x, 0f, 0f), Vector3.right, 0.075f, 0.03f, 20, true);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                mb.Transform = Matrix4x4.TRS(new Vector3(x, 0f, 0f), Quaternion.Euler(a, 0f, 0f), Vector3.one);
                mb.Box(new Vector3(-0.012f, 0.06f, -0.026f), new Vector3(0.018f, 0.218f, 0.026f));
                mb.Transform = Matrix4x4.TRS(new Vector3(x, 0f, 0f), Quaternion.Euler(a + 12f, 0f, 0f), Vector3.one);
                mb.Box(new Vector3(-0.014f, 0.07f, -0.016f), new Vector3(0.010f, 0.21f, 0.016f));
            }
            mb.Transform = Matrix4x4.identity;
            mb.CylinderAxis(new Vector3(x + 0.016f, 0f, 0f), Vector3.right, 0.03f, 0.01f, 16, true);
        }

        // ------------------------------------------------------------------ helpers

        private static GameObject Part(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Transform Anchor(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        private static Mesh SaveMesh(MeshBuilder mb, string name)
        {
            var mesh = mb.ToMesh(name);
            string path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void Save(MeshBuilder mb, GameObject parent, string name, Material mat)
        {
            if (mb.Count == 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mb, name);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static GameObject Child(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
