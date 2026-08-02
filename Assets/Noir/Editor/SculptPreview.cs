using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The ground-only Edit-Mode scene the sculpt window paints on: no city, no buildings, no
    /// traffic, no people - none of that changes shape under a brush, and building it anyway
    /// would be exactly the "regenerate everything on every stroke" cost this tool exists to
    /// avoid. Built from the same VillageMesh.BuildGround every full village build already runs,
    /// so there is exactly one ground mesh generator in the project rather than a second one that
    /// could drift from it.
    /// </summary>
    public sealed class SculptPreview
    {
        public GameObject Root { get; private set; }
        public WorldModel World { get; private set; }

        private readonly Dictionary<(int col, int row), MeshFilter> _chunks =
            new Dictionary<(int col, int row), MeshFilter>();

        public IReadOnlyDictionary<(int col, int row), MeshFilter> Chunks => _chunks;

        /// <summary>Tears down any previous preview and builds a fresh one from the map file on
        /// disk. Also how Undo/Redo apply a restored delta grid - simpler and safer than trying
        /// to re-derive exactly which chunks a multi-stroke undo touched.</summary>
        public void Build()
        {
            Teardown();

            PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
            var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
            World = WorldBuilder.Build(layout);

            Root = new GameObject("SculptPreview");
            VillageMesh.BuildGround(World, Root.transform);

            var ground = Root.transform.Find("Ground");
            foreach (Transform child in ground)
            {
                var mf = child.GetComponent<MeshFilter>();
                if (mf == null) continue;
                if (!TryParseChunkName(child.name, out int col, out int row)) continue;
                _chunks[(col, row)] = mf;
            }

            Debug.Log($"[sculpt] preview built: {World.Width}x{World.Height}, {_chunks.Count} ground chunks.");
        }

        public void Teardown()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            Root = null;
            World = null;
            _chunks.Clear();
        }

        /// <summary>Chunk mesh names are "Ground {col},{row}" (see MeshChunks.Emit) - "Ground"
        /// specifically, not "Surround", which is the same container's unchunked map-edge skirt
        /// and would otherwise collide with a real chunk at (0,0).</summary>
        private static bool TryParseChunkName(string name, out int col, out int row)
        {
            col = row = 0;
            var parts = name.Split(' ');
            if (parts.Length != 2 || parts[0] != "Ground") return false;
            var coords = parts[1].Split(',');
            if (coords.Length != 2) return false;
            return int.TryParse(coords[0], out col) && int.TryParse(coords[1], out row);
        }
    }
}
