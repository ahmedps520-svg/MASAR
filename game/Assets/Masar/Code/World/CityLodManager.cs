using UnityEngine;
using UnityEngine.SceneManagement;

namespace Masar.World
{
    /// <summary>
    /// Checks the camera distance of every block and cell five times a second, and streams the
    /// blocks' cell scenes: one load at a time, nearest first, starting <see cref="Preload"/> m before
    /// a block is needed; unloaded <see cref="Keep"/> m past it.
    /// </summary>
    public sealed class CityLodManager : MonoBehaviour
    {
        public const float NearDistance = 170f, FarDistance = 420f;
        const float Preload = 200f, Keep = 350f;
        internal static bool Dirty;
        float _next;
        static AsyncOperation _op;
        static BlockLod _opBlock;
        static int _unloadedSinceCleanup;
        static AsyncOperation _cleanup;

        /// <summary>Blocks needed now (inside the far distance) that are not loaded yet.</summary>
        public static int Missing { get; private set; }
        /// <summary>Blocks needed now, loaded or not (for the loading screen's progress).</summary>
        public static int Needed { get; private set; }
        /// <summary>True when every block needed at the camera's position is loaded.</summary>
        public static bool Settled => _evaluated && Missing == 0 && _op == null;
        static bool _evaluated;

        void OnDisable() { _op = null; _opBlock = null; _cleanup = null; _unloadedSinceCleanup = 0; Missing = Needed = 0; _evaluated = false; }

        void Update()
        {
            if (!Dirty && _op == null && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;
            Dirty = false;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            float s = CellLod.DistanceScale, far = FarDistance * s;
            for (int i = 0; i < BlockLod.All.Count; i++) BlockLod.All[i].Apply(c, far);
            for (int i = 0; i < CellLod.All.Count; i++) CellLod.All[i].Apply(c, NearDistance * s);
            Stream(far);
        }

        void Stream(float far)
        {
            if (_op != null)
            {
                if (!_op.isDone) return;
                if (_opBlock != null && _opBlock.loading) Attach(_opBlock);
                _op = null; _opBlock = null;
            }
            int missing = 0, needed = 0;
            BlockLod next = null, drop = null;
            for (int i = 0; i < BlockLod.All.Count; i++)
            {
                var b = BlockLod.All[i];
                if (!b.Streamed) continue;
                if (b.distance < far) { needed++; if (!b.Loaded) missing++; }
                if (!b.Loaded && b.distance < far + Preload && (next == null || b.distance < next.distance)) next = b;
                if (b.Loaded && b.distance > far + Keep && (drop == null || b.distance > drop.distance)) drop = b;
            }
            Missing = missing; Needed = needed;
            _evaluated = true;
            // Loads first (what the player is about to see), then unloads, then free the unloaded meshes
            if (next != null)
            {
                // Full speed while blocks the player should already see are missing (the loading screen),
                // gentle in the background otherwise so driving stays smooth
                Application.backgroundLoadingPriority = missing > 0 ? ThreadPriority.High : ThreadPriority.BelowNormal;
                next.loading = true;
                _opBlock = next;
                _op = SceneManager.LoadSceneAsync(next.section, LoadSceneMode.Additive);
                if (_op == null) { Debug.LogError($"[MASAR] City block scene missing from the build: {next.section}"); next.section = null; _opBlock = null; }
                return;
            }
            if (drop != null)
            {
                var root = drop.cells;
                drop.cells = null;
                drop.state = -1;
                drop.Apply(Camera.main.transform.position, far);   // far mesh back on
                var scene = root.scene;
                if (scene.IsValid() && scene.isLoaded) _op = SceneManager.UnloadSceneAsync(scene);
                _unloadedSinceCleanup++;
                return;
            }
            // Unloading a scene keeps its meshes in memory until the next unused-asset sweep
            if (_unloadedSinceCleanup > 0 && (_cleanup == null || _cleanup.isDone))
            {
                _unloadedSinceCleanup = 0;
                _cleanup = Resources.UnloadUnusedAssets();
            }
        }

        static void Attach(BlockLod b)
        {
            b.loading = false;
            var scene = SceneManager.GetSceneByName(b.section);
            if (!scene.IsValid() || !scene.isLoaded) { Debug.LogError($"[MASAR] City block {b.section} did not load"); b.section = null; return; }
            var roots = scene.GetRootGameObjects();
            if (roots.Length == 0) return;
            b.cells = roots[0];   // saved inactive: shown by Apply when the block is close enough
            b.state = -1;
            Dirty = true;
        }
    }
}
