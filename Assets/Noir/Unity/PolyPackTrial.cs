using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Stands the Poly Universal Pack prototype cottage in the running village, so a bought
    /// building can be walked up to with the game's own camera instead of judged from a still.
    ///
    /// It goes on the green: open ground near the middle of the village, which is both easy to
    /// find and the only reliably empty acre - dropped anywhere else the cottage grows out of
    /// somebody's roof. Nothing else is disturbed; this adds one GameObject and touches no
    /// simulation state.
    ///
    /// Evaluation scaffolding. It deletes with the rest of the prototype once the catalog-driven
    /// renderer in the spec exists.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after VillageHost, which is -100 and builds the world
    public sealed class PolyPackTrial : MonoBehaviour
    {
        /// <summary>
        /// Off. The trial cottage answered its question - a building assembled from the modular
        /// parts kit reads as a house - and the answer sent the project somewhere else: whole
        /// city sections placed on authored lots, which is CityBuildings. Left in the tree
        /// because the cottage is still the only worked example of the fine-grained kit, and
        /// the two costs are worth being able to compare: 49 pieces for that one cottage against
        /// about five for a townhouse.
        /// </summary>
        public static bool Enabled = false;

        private bool _built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Enabled) return;
            var go = new GameObject("PolyPackTrial");
            DontDestroyOnLoad(go);
            go.AddComponent<PolyPackTrial>();
        }

        // Not Start: VillageHost builds the world in its own Awake, and which of two
        // RuntimeInitializeOnLoadMethod hooks runs first is not something to rely on. Waiting
        // for the world to exist costs one branch a frame and cannot get the order wrong.
        private void Update()
        {
            if (_built || !Enabled) return;

            var host = VillageHost.Instance;
            if (host == null || host.World == null) return;

            _built = true;

            if (!TryFindGreen(host.World, out var green))
            {
                Debug.LogWarning("[polypack] no green to stand the trial cottage on.");
                return;
            }

            // Village y runs into Unity -z, as everywhere else in the renderers.
            var centre = new Vector3(green.Centre.X,
                                     ElevationGrid.HeightAt(green.Centre.X, green.Centre.Y),
                                     -green.Centre.Y);
            var origin = centre - new Vector3(PolyPackCottageBuilder.W / 2f, 0f,
                                              PolyPackCottageBuilder.D / 2f);

            var cottage = PolyPackCottageBuilder.Assemble(origin);
            cottage.transform.SetParent(transform, false);
            cottage.transform.position = origin;

            Debug.Log($"[polypack] trial cottage on the green at {green.Centre.X},{green.Centre.Y} "
                    + "- press X to take the buildings away if it is hidden behind one.");
        }

        private static bool TryFindGreen(WorldModel world, out TileRect bounds)
        {
            foreach (var place in world.AllPlaces)
                if (place.Kind == PlaceKind.Green) { bounds = place.Bounds; return true; }

            foreach (var place in world.AllPlaces)
                if (place.Kind == PlaceKind.Gardens) { bounds = place.Bounds; return true; }

            bounds = default;
            return false;
        }
    }
}
