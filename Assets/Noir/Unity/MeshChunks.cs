using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Noir.Unity
{
    /// <summary>
    /// One chunk's geometry while it is being built: the buffers a mesh builder appends to.
    /// </summary>
    public sealed class MeshChunk
    {
        public readonly int Col, Row;

        public readonly List<Vector3> Verts = new List<Vector3>();

        /// <summary>
        /// Filled in only by geometry that knows which way it faces - the ground, whose risers
        /// are vertical faces that have to look at the low side, and combined primitives, whose
        /// normals are the entire difference between a sphere and a faceted rock. Left empty,
        /// the mesh gets RecalculateNormals instead, which is what the walls and the roofs want
        /// and what they already did.
        /// </summary>
        public readonly List<Vector3> Normals = new List<Vector3>();

        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int>[] Tris;

        internal MeshChunk(int col, int row, int submeshes)
        {
            Col = col;
            Row = row;
            Tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) Tris[i] = new List<int>();
        }

        internal bool IsEmpty
        {
            get
            {
                for (int i = 0; i < Tris.Length; i++) if (Tris[i].Count > 0) return false;
                return true;
            }
        }

        /// <summary>
        /// Append one of Unity's built-in primitives, baked into world space.
        ///
        /// The normal is carried through the INVERSE TRANSPOSE of the scale rather than through
        /// the scale itself. Under the non-uniform scales everything out here uses - a canopy is
        /// 0.85 as tall as it is wide, a hedge segment is two metres of one and one of another -
        /// scaling a normal the same way as a vertex tilts it off the surface it belongs to, and
        /// the whole copse comes out lit slightly wrong. Unity's renderer performs exactly this
        /// division when it draws a scaled object, so doing it here is what makes the combined
        /// mesh light identically to the several hundred primitives it replaces.
        /// </summary>
        public void Add(Primitives.Shape shape, int submesh,
                        Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var inverse = new Vector3(
                scale.x == 0f ? 0f : 1f / scale.x,
                scale.y == 0f ? 0f : 1f / scale.y,
                scale.z == 0f ? 0f : 1f / scale.z);

            int start = Verts.Count;
            var verts = shape.Verts;
            for (int i = 0; i < verts.Length; i++)
            {
                Verts.Add(position + rotation * Vector3.Scale(verts[i], scale));
                Normals.Add((rotation * Vector3.Scale(shape.Normals[i], inverse)).normalized);
                Uvs.Add(shape.Uvs[i]);
            }

            // A scale that flips an odd number of axes turns every triangle in the mesh inside
            // out, and Unity takes a face from Cross(v1-v0, v2-v0) and culls the back - so the
            // object would light and shadow perfectly and be invisible. Nothing in the village
            // is mirrored today; this costs one multiply and closes the hole before somebody
            // scales a tree by -1 to face it the other way.
            bool mirrored = scale.x * scale.y * scale.z < 0f;

            var tris = Tris[submesh];
            var source = shape.Tris;
            for (int i = 0; i + 2 < source.Length; i += 3)
            {
                tris.Add(start + source[i]);
                tris.Add(start + source[i + (mirrored ? 2 : 1)]);
                tris.Add(start + source[i + (mirrored ? 1 : 2)]);
            }
        }
    }

    /// <summary>
    /// A generated mesh, split across a fixed grid of square chunks so the parts of it behind
    /// you can be thrown away before they reach the GPU.
    ///
    /// One mesh for the whole map was right at village scale and is wrong at town scale, for a
    /// reason that has nothing to do with draw calls: Unity culls a mesh by its bounding box,
    /// and the bounding box of the whole village's floor contains the camera wherever the
    /// camera is. Standing in one street the GPU was handed the entire floor, every wall in
    /// Ashcombe and every roof on it. Chunking exists to give the culler something it can
    /// actually discard.
    ///
    /// The grid is anchored to the GRID ORIGIN rather than to the map, so where a chunk
    /// boundary falls is a property of the world and not of the map's size. Adding a row of
    /// tiles at the top of village.txt does not then shift every chunk in the town one metre
    /// and change every mesh in the project.
    ///
    /// Chunks are stored densely and emitted row by row, column by column. That is not tidiness
    /// - the offline snapshot renderer is compared byte for byte between runs, so an output
    /// order that came out of a Dictionary's enumeration would be a regression test that failed
    /// at random and told you nothing when it did.
    /// </summary>
    public sealed class MeshChunks
    {
        /// <summary>
        /// Chunk edge, in metres. One tile is one metre, so this is also 64 tiles.
        ///
        /// Sized against the two views that matter rather than against a profiler. In the wide
        /// shot every chunk is on screen by definition, so the chunk size only costs there -
        /// each chunk is one renderer per material present in it, and halving the size roughly
        /// quadruples that bill. At street level the frustum is a narrow wedge and the chunk
        /// size is the whole saving. 64 m is about the length of a village street block, which
        /// means a chunk tends to hold whole buildings rather than slicing through them, and it
        /// puts a 300x220 town at 5x4 chunks - enough that turning round discards most of it,
        /// few enough that the noon overview is still counted in tens of draw calls and not
        /// hundreds.
        /// </summary>
        public const int Size = 64;

        /// <summary>
        /// Chunk edge for the GROUND alone, in metres. Deliberately a second, coarser number
        /// rather than a bump to Size, because Size is shared with Walls and Props and is tuned
        /// for THEIR trade-off - a chunk that holds whole buildings rather than slicing through
        /// them (see above). The ground has no buildings to slice and wants a different answer.
        ///
        /// WHY IT CAN BE COARSER NOW, WHICH IT COULD NOT BEFORE. A draw call is one per
        /// (chunk, material present in it), so the ground's bill is set by how many chunks there
        /// are and not by how much is in them. On the 2,100 x 2,400 map, 64 m gives 33 x 38
        /// chunks - and until the greedy merge landed, each of those chunks held four thousand
        /// individual tile quads, so making them bigger would have meant handing the GPU tens of
        /// thousands of quads for a chunk with one corner on screen. A ground chunk is now a few
        /// dozen merged quads, and 256 m of it costs less than 64 m of it used to. The culling
        /// accuracy that buys the draw-call reduction is the cheapest thing on the map to give up.
        ///
        /// MEASURED, not picked - GroundChunkProbe sweeps this over the real 2,100 x 2,400 map:
        ///
        ///     size   chunks   draw calls   vertices   worst chunk
        ///       64     1254        2,626    575,652     4,924 tris
        ///      128      323          860    559,504     8,902
        ///      256       90          319    553,148    15,846
        ///      384       42          184    549,676    19,738
        ///      512       25          120    549,452    37,098
        ///
        /// Vertices go DOWN as chunks coarsen, which is not the direction anyone expects: a run
        /// may not cross a chunk edge, so a coarser grid cuts fewer of them in half.
        ///
        /// 256 is where the two curves cross usefully. It is 8.2x fewer draw calls, and the
        /// worst single chunk on the whole map is still under sixteen thousand triangles - about
        /// what one bought tree costs - so nothing is paid for having a corner of one on screen.
        /// 384 and 512 keep buying draw calls, and they are NOT taken: at 512 there are
        /// twenty-five chunks over the entire town, which is few enough that turning round
        /// discards almost nothing, and culling is the reason this class exists at all. The
        /// ground is cheap enough now that it could probably survive being drawn whole - but
        /// "probably survives being unculled" is a worse thing to write down than a grid that
        /// still works, for two hundred draw calls that nothing has asked for.
        /// </summary>
        public const int GroundSize = 256;

        private readonly int _chunkSize;
        private readonly int _minCol, _minRow, _cols, _rows;
        private readonly int _submeshes;
        private readonly MeshChunk[] _buckets;   // dense, row-major; null until something lands in it

        private readonly bool _single;
        private bool _clamped;

        /// <summary>
        /// A grid covering the given inclusive range of grid coordinates. Both ends are
        /// inclusive because the riser pass walks one PAST the last tile - a riser at
        /// x == world.Width is the cut edge of the last column and has to have somewhere to go.
        /// </summary>
        public MeshChunks(int submeshes, int chunkSize, int fromGx, int fromGy, int toGx, int toGy)
            : this(submeshes, chunkSize, fromGx, fromGy, toGx, toGy, false) { }

        private MeshChunks(int submeshes, int chunkSize, int fromGx, int fromGy,
                           int toGx, int toGy, bool single)
        {
            _single = single;
            _submeshes = submeshes;
            _chunkSize = chunkSize;
            _minCol = FloorDiv(fromGx, chunkSize);
            _minRow = FloorDiv(fromGy, chunkSize);
            _cols = FloorDiv(toGx, chunkSize) - _minCol + 1;
            _rows = FloorDiv(toGy, chunkSize) - _minRow + 1;
            _buckets = new MeshChunk[_cols * _rows];
        }

        /// <summary>
        /// A grid of exactly one chunk - for geometry that must not be split but still wants the
        /// same emitter. The ground's two-kilometre skirt is the case: a dozen quads whose
        /// bounding box contains the world.
        /// </summary>
        public static MeshChunks Single(int submeshes) =>
            new MeshChunks(submeshes, 1, 0, 0, 0, 0, true);

        public MeshChunk At(int gx, int gy) =>
            Bucket(FloorDiv(gx, _chunkSize), FloorDiv(gy, _chunkSize));

        public MeshChunk At(float gx, float gy) =>
            Bucket(Mathf.FloorToInt(gx / _chunkSize), Mathf.FloorToInt(gy / _chunkSize));

        private MeshChunk Bucket(int col, int row)
        {
            // Clamping rather than throwing, because a clamped chunk is only INEFFICIENT and
            // never wrong: mesh bounds come from the vertices that actually went in, so the
            // geometry is still culled correctly, just alongside neighbours it need not have
            // been grouped with. It does mean the declared extent was too small, which is worth
            // one line in the log rather than a failed build.
            int c = Mathf.Clamp(col - _minCol, 0, _cols - 1);
            int r = Mathf.Clamp(row - _minRow, 0, _rows - 1);
            if (!_single && (c != col - _minCol || r != row - _minRow)) _clamped = true;

            int i = r * _cols + c;
            return _buckets[i] ?? (_buckets[i] = new MeshChunk(c + _minCol, r + _minRow, _submeshes));
        }

        /// <summary>
        /// Every chunk that has something in it, row by row then column by column. The order is
        /// fixed by the array walk, which is the point - see the note on this class.
        /// </summary>
        public IEnumerable<MeshChunk> All
        {
            get
            {
                for (int i = 0; i < _buckets.Length; i++)
                    if (_buckets[i] != null && !_buckets[i].IsEmpty) yield return _buckets[i];
            }
        }

        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (var chunk in All) n += chunk.Verts.Count;
                return n;
            }
        }

        /// <summary>Triangle count across every submesh of every chunk - index count / 3, summed
        /// rather than tracked separately, so it can never disagree with what actually got
        /// built.</summary>
        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (var chunk in All)
                    for (int s = 0; s < chunk.Tris.Length; s++)
                        n += chunk.Tris[s].Count / 3;
                return n;
            }
        }

        /// <summary>
        /// Build a renderer per non-empty chunk, and return them.
        ///
        /// A chunk carries only the submeshes it actually has geometry for. That is what keeps
        /// the material count honest: the ground has nine materials across the map, but a chunk
        /// of open field has grass and nothing else, and an empty submesh is still a material
        /// slot Unity walks past on every draw. The materials themselves are the same shared
        /// objects the unchunked mesh used - chunking must not mint a single new one - and the
        /// submeshes are emitted in ascending index order, so a chunk's material list is a
        /// subsequence of the palette rather than an order the build loop happened to produce.
        /// </summary>
        public List<MeshRenderer> Emit(Transform parent, string name, Material[] palette,
                                       ShadowCastingMode shadows, bool receiveShadows)
        {
            var renderers = new List<MeshRenderer>();
            if (_clamped)
                Debug.LogError($"{name}: geometry landed outside the chunk grid and was clamped "
                             + "into the outermost chunks. It will still draw correctly, but the "
                             + "grid's declared extent is too small and culling is the worse for it.");

            var used = new List<int>();

            foreach (var chunk in All)
            {
                used.Clear();
                for (int s = 0; s < chunk.Tris.Length; s++)
                    if (chunk.Tris[s].Count > 0) used.Add(s);

                var mesh = new Mesh
                {
                    name = $"{name} {chunk.Col},{chunk.Row}",
                    // 16-bit indices where they fit. The old single mesh had no choice; a chunk
                    // usually does, and it halves the index buffer.
                    indexFormat = chunk.Verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };

                mesh.SetVertices(chunk.Verts);
                mesh.SetUVs(0, chunk.Uvs);

                bool hasNormals = chunk.Normals.Count == chunk.Verts.Count;
                if (chunk.Normals.Count != 0 && !hasNormals)
                    Debug.LogError($"{mesh.name}: {chunk.Normals.Count} normals for "
                                 + $"{chunk.Verts.Count} vertices. The mesh will be given "
                                 + "recalculated ones, which flat-shades anything round.");
                if (hasNormals) mesh.SetNormals(chunk.Normals);

                mesh.subMeshCount = used.Count;
                var materials = new Material[used.Count];
                for (int i = 0; i < used.Count; i++)
                {
                    mesh.SetTriangles(chunk.Tris[used[i]], i);
                    materials[i] = palette[used[i]];
                }

                if (!hasNormals) mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = shadows;
                mr.receiveShadows = receiveShadows;
                renderers.Add(mr);
            }

            return renderers;
        }

        /// <summary>How many draw calls the emitted chunks come to - one per material per chunk.</summary>
        public int DrawCalls
        {
            get
            {
                int n = 0;
                foreach (var chunk in All)
                    for (int s = 0; s < chunk.Tris.Length; s++)
                        if (chunk.Tris[s].Count > 0) n++;
                return n;
            }
        }

        /// <summary>C#'s integer division truncates toward zero, which puts -1 and 0 in the same chunk.</summary>
        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }

    /// <summary>
    /// The geometry of Unity's built-in primitives, read once and kept.
    ///
    /// Taken off a real primitive rather than fetched by asset name: Resources.GetBuiltinResource
    /// wants the file the mesh happens to ship inside, which is a string that has moved between
    /// Unity versions, whereas creating one cube and throwing it away cannot go out of date.
    /// </summary>
    public static class Primitives
    {
        public sealed class Shape
        {
            public Vector3[] Verts;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Tris;
        }

        private static readonly Shape[] _shapes = new Shape[8];

        public static Shape Of(PrimitiveType type)
        {
            int i = (int)type;
            if (_shapes[i] != null) return _shapes[i];

            var probe = GameObject.CreatePrimitive(type);
            var mesh = probe.GetComponent<MeshFilter>().sharedMesh;

            // mesh.triangles concatenates every submesh, which is what is wanted here - a
            // built-in primitive that ships split into several draws is still one solid object
            // in one material as far as anything in this project is concerned.
            var shape = new Shape
            {
                Verts = mesh.vertices,
                Normals = mesh.normals,
                Uvs = mesh.uv,
                Tris = mesh.triangles,
            };

            // A mesh is allowed to have no normals and no UVs, and appending one blind would
            // then throw on the first tree rather than on the primitive that was actually
            // wrong. Unity's own primitives have both; this is only so that if one ever does
            // not, the village still builds and says which one it was.
            if (shape.Normals.Length != shape.Verts.Length)
            {
                Debug.LogWarning($"The built-in {type} has {shape.Normals.Length} normals for "
                               + $"{shape.Verts.Length} vertices; it will be flat where it "
                               + "should be round.");
                shape.Normals = new Vector3[shape.Verts.Length];
                for (int v = 0; v < shape.Normals.Length; v++) shape.Normals[v] = Vector3.up;
            }
            if (shape.Uvs.Length != shape.Verts.Length)
                shape.Uvs = new Vector2[shape.Verts.Length];

            // DestroyImmediate, because Object.Destroy is a silent no-op outside play mode and
            // the offline renderer builds the whole village without ever pressing Play. Left to
            // Destroy, every probe would survive in the scene with an error line beside it.
            if (Application.isPlaying) Object.Destroy(probe);
            else Object.DestroyImmediate(probe);

            return _shapes[i] = shape;
        }
    }

    /// <summary>
    /// The materials the scenery is combined from - the props inside the village and the
    /// hedgerows and woods outside it - in a FIXED order, index here being submesh index.
    ///
    /// Fixed rather than allocated as each material is first seen, because "first seen" is an
    /// order, and an order that falls out of a build loop is one more thing that has to come out
    /// identical on every run for the snapshots to be worth comparing. A written-down table
    /// cannot drift; a discovered one can, silently, the first time somebody reorders a loop.
    /// </summary>
    public static class Scenery
    {
        private static int Greens => Materials3D.Canopies.Length;

        public const int Bark = 0;
        public const int Canopy = 1;                 // .. Canopy + Greens - 1
        public static int Timber => Canopy + Greens;
        public static int Stone => Timber + 1;
        public static int Postbox => Stone + 1;
        public static int Hedge => Postbox + 1;
        public static int Count => Hedge + 1;

        /// <summary>
        /// Which green a canopy at this position is. The same call the individual props made,
        /// so a tree keeps the colour it had - it is a property of where it stands, and the
        /// whole point of a palette of four is that a copse has depth instead of being one mass.
        /// </summary>
        public static int CanopyFor(int x, int y, int salt) =>
            Canopy + (int)(Materials3D.Scatter(x, y, salt) % (uint)Greens);

        public static Material[] Palette()
        {
            var greens = Materials3D.Canopies;
            var palette = new Material[Count];
            palette[Bark] = Materials3D.Bark;
            for (int i = 0; i < greens.Length; i++) palette[Canopy + i] = greens[i];
            palette[Timber] = Materials3D.Timber;
            palette[Stone] = Materials3D.Stone;
            palette[Postbox] = Materials3D.Postbox;
            palette[Hedge] = Materials3D.Hedge;
            return palette;
        }
    }
}
