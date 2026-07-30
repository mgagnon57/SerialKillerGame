using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// One cottage assembled out of Poly Universal Pack modular parts.
    ///
    /// This is the evaluation prototype behind
    /// docs/superpowers/specs/2026-07-29-poly-universal-pack-integration-design.md - a hand-laid
    /// building, not the catalog-driven renderer that spec proposes. It lives in the runtime
    /// assembly rather than in Editor/ only so it can be spawned while the game is playing and
    /// looked at from the village's own camera, which is the only way to judge whether a bought
    /// building belongs in this village.
    ///
    /// Editor-only in practice: the pieces are loaded through AssetDatabase, because the pack is
    /// not under Resources and this is not shipping code.
    /// </summary>
    public static class PolyPackCottageBuilder
    {
        private const string Parts = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/";
        private const string Plain = Parts + "Walls/3m/Walls Plain/";
        private const string Slope = Parts + "Roofs/Roof Regular/";
        private const string Doors = Parts + "Doors/Doors City/";
        private const string Windows = Parts + "Windows/Windows City/";
        private const string Gutters = Parts + "Roofs/Gutter/";

        /// <summary>Footprint 6m x 4m, 3m to the eaves, 45-degree gable ridge along x at 5m.</summary>
        public const float W = 6f, D = 4f;
        private const float WallTop = 3f;

        /// <summary>
        /// The roof tiles carry a 0.13m lip below their pivot, so seating them at eave height
        /// needs that taken back off or every roof floats a hand's breadth above its walls.
        /// </summary>
        private const float Lip = 0.13f;

        /// <summary>Mid-height of a wall piece's window hole, which tops out at 2.2m.</summary>
        private const float WindowMid = 1.55f;

        /// <summary>
        /// Limewash, terracotta and a red door, instead of render and slate. A comparison, not a
        /// decision: the muted palette was never argued for anywhere in the codebase, so this is
        /// here to be looked at rather than reasoned about.
        /// </summary>
        public static bool Painted = false;

        /// <summary>
        /// One cottage, origin at its south-west ground corner. Walls pivot on their right edge
        /// at ground level, so each face is laid out by walking segments along it.
        /// </summary>
        public static GameObject Assemble(Vector3 origin)
        {
            var go = new GameObject("PolyPackCottage");
            go.transform.position = origin;

#if UNITY_EDITOR
            // The pack's own atlas materials sample to a flat blue here, so every piece is
            // repainted in Ashcombe's palette as it goes in. That is where this was always
            // heading - the village is deliberately muted and the kit's stock colours are not.
            //
            // Flat colour rather than Materials3D.Wall or .Roofs, which carry a surface texture
            // tiled against Ashcombe's own world-space UVs. The kit's meshes are atlas-mapped -
            // every vertex of a panel lands on one palette texel - and under those materials a
            // wall came out invisible. Materials3D.Stone is the one untextured member of the set
            // and it was the one piece that rendered correctly, which is the whole argument.
            // Two palettes, because "muted" turned out never to have been decided. Nothing in
            // Materials3D argues for it - the village is desaturated by accretion, one
            // individually low-saturation colour at a time. The single properly saturated thing
            // in the whole palette is the postbox at 8E1F1C, which is saturated precisely
            // BECAUSE it is meant to catch the eye. That is the principle worth keeping: colour
            // rationed so it still means something, not colour absent.
            var wall = Painted
                ? Flat("PaintedWall", new Color32(0xE9, 0xDE, 0xC6, 0xFF))   // limewash
                : Flat("PackWall", new Color32(0xC6, 0xB8, 0xA6, 0xFF));
            var roof = Painted
                ? Flat("PaintedRoof", new Color32(0xA8, 0x58, 0x3C, 0xFF))   // terracotta pantile
                : Flat("PackRoofSlate", new Color32(0x6B, 0x70, 0x79, 0xFF));
            // A painted front door is the most English thing a cottage owns, and Ashcombe already
            // has the right red on the shelf - the postbox's.
            var timber = Painted
                ? Flat("PaintedDoor", new Color32(0x8E, 0x1F, 0x1C, 0xFF))
                : Flat("PackTimber", new Color32(0x6E, 0x5A, 0x46, 0xFF));
            // Window casings are the pale painted joinery that does most of the work in the
            // publisher's own farmhouse, so they are lighter than the render, not darker.
            var casing = Painted
                ? Flat("PaintedCasing", new Color32(0xF4, 0xEF, 0xE6, 0xFF))
                : Flat("PackCasing", new Color32(0xDE, 0xD6, 0xC6, 0xFF));
            var stone = Materials3D.Stone;
            var glass = Materials3D.WindowGlass;

            // An _Ext piece is a single-sided outer skin - that is what the matching _Int pieces
            // are for - so its one visible face points along +z at rest, NOT along -z as the
            // 0.1m thickness running that way suggests. Faced the other way the cottage was
            // built inside out: every wall's only face pointed into the room, and from the road
            // you looked straight through the house.
            //
            // South face, z=0, looking -z: window, door, plain.
            Put(go, Plain + "Wall_Window_2x3m_Ext.prefab",        new Vector3(0f, 0f, 0f), 180f, wall);
            Put(go, Plain + "Wall_Door_Regular_2x3m_Ext.prefab",  new Vector3(2f, 0f, 0f), 180f, wall);
            Put(go, Plain + "Wall_2x3m_Ext.prefab",               new Vector3(4f, 0f, 0f), 180f, wall);

            // North face, z=D, looking +z.
            Put(go, Plain + "Wall_1x3m_Ext.prefab",               new Vector3(1f, 0f, D), 0f, wall);
            Put(go, Plain + "Wall_Window_2x3m_Ext.prefab",        new Vector3(3f, 0f, D), 0f, wall);
            Put(go, Plain + "Wall_3x3m_Ext.prefab",               new Vector3(6f, 0f, D), 0f, wall);

            // West face, x=0, looking -x.
            Put(go, Plain + "Wall_4x3m_Ext.prefab",               new Vector3(0f, 0f, D), 270f, wall);

            // East face, x=W, looking +x.
            Put(go, Plain + "Wall_1x3m_Ext.prefab",               new Vector3(W, 0f, 0f), 90f, wall);
            Put(go, Plain + "Wall_Window_2x3m_Ext.prefab",        new Vector3(W, 0f, 1f), 90f, wall);
            Put(go, Plain + "Wall_1x3m_Ext.prefab",               new Vector3(W, 0f, 3f), 90f, wall);

            // Doors and windows are separate inserts - the wall pieces only supply the hole.
            // Their pivots are not consistent with each other (a door is centred on its own
            // opening, a window is not), so each is seated by measuring the piece and moving its
            // centre onto the hole rather than by a per-prefab magic number.
            //
            // The City family rather than Fantasy. There is no exterior trim in the kit's Trims
            // folder - that is all interior skirting and coving - so the white casing round a
            // window in the publisher's own farmhouse is part of the WINDOW, not a separate
            // piece. Picking the family is therefore how you get trim. Fantasy gave an arched
            // plank door and a pointed leaded light, which read medieval; City reads as a house.
            Fit(go, Doors + "Door_Single_A_Regular_City.prefab",
                new Vector3(3f, 0f, -0.05f), 180f, timber, glass);
            Fit(go, Windows + "Window_A_City.prefab",
                new Vector3(1f, 0f, -0.05f), 180f, casing, glass);
            Fit(go, Windows + "Window_A_City.prefab",
                new Vector3(2f, 0f, D + 0.05f), 0f, casing, glass);
            Fit(go, Windows + "Window_A_City.prefab",
                new Vector3(W + 0.05f, 0f, 2f), 90f, casing, glass);

            // Gable triangles fill wall-top to ridge on the two ends the ridge runs into.
            Gable(go, x: 0f,  facing: 270f, paint: wall);
            Gable(go, x: W,   facing: 90f,  paint: wall);

            // Roof. Tiles are high at their -z edge and fall to +z, so the north pitch sits at
            // its natural orientation and the south pitch is the same tile turned about.
            for (float x = 2f; x <= W; x += 2f)
                Put(go, Slope + "Roof_Regular_2x2m.prefab", new Vector3(x, WallTop - Lip, D), 0f, roof);
            for (float x = 0f; x < W; x += 2f)
                Put(go, Slope + "Roof_Regular_2x2m.prefab", new Vector3(x, WallTop - Lip, 0f), 180f, roof);

            Put(go, Parts + "Chimneys/Chimney_3m_A_Fantasy.prefab",
                new Vector3(1.4f, WallTop, D / 2f), 0f, stone);

            Dress(go, roof, Materials3D.Ironwork);
#else
            Debug.LogWarning("[polypack] the cottage prototype only assembles inside the editor.");
#endif
            return go;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Eaves, barge boards, gutters and a downpipe.
        ///
        /// None of this is structure and all of it is what the eye reads as "a built house"
        /// rather than "a shape". The bare cottage used only the flat slope tiles, which leaves
        /// the roof stopping dead at the wall with no overhang and no shadow line under it -
        /// the single biggest difference between the prototype and the publisher's own dressed
        /// farmhouse.
        ///
        /// The eave pieces take the same position and rotation as the slope tile they belong to,
        /// which is a happy accident of the kit's pivots: an _Edge piece occupies the half metre
        /// OUTSIDE the eave line where the tile occupies the two metres inside it.
        /// </summary>
        private static void Dress(GameObject go, Material roof, Material metal)
        {
            const float EaveY = WallTop - Lip;

            // Eaves along both long sides, following the tiles they hang off.
            for (float x = 2f; x <= W; x += 2f)
                Put(go, Slope + "Roof_Regular_2m_Edge.prefab", new Vector3(x, EaveY, D), 0f, roof);
            for (float x = 0f; x < W; x += 2f)
                Put(go, Slope + "Roof_Regular_2m_Edge.prefab", new Vector3(x, EaveY, 0f), 180f, roof);

            // Barge boards up the two gable slopes. Same handedness problem as the gable itself:
            // the piece only rises one way, so each end gets one placed and one reflected.
            Rake(go, new Vector3(W, EaveY, D), 0f, roof);     // east
            Rake(go, new Vector3(0f, EaveY, 0f), 180f, roof); // west

            // Gutters sit under the eave overhang. Their pivot is the LEFT edge, unlike the
            // walls and roofs, so a run spanning [a, a+2] is placed at a going one way and at
            // a+2 going the other.
            for (float x = 0f; x < W; x += 2f)
                Put(go, Gutters + "Gutter_Regular_2m.prefab", new Vector3(x, EaveY, D), 0f, metal);
            for (float x = 2f; x <= W; x += 2f)
                Put(go, Gutters + "Gutter_Regular_2m.prefab", new Vector3(x, EaveY, 0f), 180f, metal);

            // One downpipe, off the back corner, ground to eave.
            Put(go, Gutters + "Gutter_Pipe_3m.prefab", new Vector3(W - 0.15f, 0f, D + 0.4f), 0f, metal);
        }

        /// <summary>
        /// One gable's pair of barge boards, the far one reflected through the ridge plane.
        /// </summary>
        private static void Rake(GameObject parent, Vector3 at, float yaw, Material paint)
        {
            Put(parent, Slope + "Roof_Regular_2m_Front.prefab", at, yaw, paint);

            var mirror = new GameObject("RakeMirror");
            mirror.transform.SetParent(parent.transform, false);
            mirror.transform.localPosition = new Vector3(0f, 0f, D);
            mirror.transform.localScale = new Vector3(1f, 1f, -1f);
            Put(mirror, Slope + "Roof_Regular_2m_Front.prefab", at, yaw, paint);
        }

        /// <summary>
        /// A gable end: two right triangles meeting at the ridge. The kit only ships the half
        /// that rises toward its pivot, and a Y rotation cannot turn a right triangle into a
        /// left one - so the far half is the same piece reflected through the ridge plane by a
        /// parent with a negative axis.
        /// </summary>
        private static void Gable(GameObject parent, float x, float facing, Material paint)
        {
            const string Half = Plain + "Wall_Roof_Corner_Down_2x2m_Ext.prefab";

            Put(parent, Half, new Vector3(x, WallTop, D / 2f), facing, paint);

            var mirror = new GameObject("GableMirror");
            mirror.transform.SetParent(parent.transform, false);
            mirror.transform.localPosition = new Vector3(0f, 0f, D);
            mirror.transform.localScale = new Vector3(1f, 1f, -1f);
            Put(mirror, Half, new Vector3(x, WallTop, D / 2f), facing, paint);
        }

        /// <summary>
        /// Seat an insert so its own centre lands on <paramref name="anchor"/>, measured rather
        /// than assumed. The kit's door pivots sit at the middle of the opening and its window
        /// pivots do not, so placing either by its pivot puts one of them through the wall.
        /// </summary>
        private static void Fit(GameObject parent, string path, Vector3 anchor, float yaw,
                                Material paint, Material glass = null)
        {
            var go = Spawn(parent, path);
            if (go == null) return;

            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localPosition = Vector3.zero;
            Paint(go, paint, glass);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Only x and z are steered onto the hole. HEIGHT IS THE PIECE'S OWN: these inserts
            // are authored to sit at the wall's base, and a City window already carries its sill
            // at 0.54 and its head at 2.65. Re-centring it on a guessed mid-height only slid it
            // off the opening it was cut for.
            var drift = b.center - go.transform.position;
            var target = parent.transform.TransformPoint(new Vector3(anchor.x, 0f, anchor.z));

            go.transform.position = new Vector3(target.x - drift.x,
                                                go.transform.position.y,
                                                target.z - drift.z);
        }

        private static void Put(GameObject parent, string path, Vector3 localPos, float yaw,
                                Material paint)
        {
            var go = Spawn(parent, path);
            if (go == null) return;

            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Paint(go, paint);
        }

        private static GameObject Spawn(GameObject parent, string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[polypack] missing " + path); return null; }

            var go = Object.Instantiate(prefab);
            go.name = prefab.name;
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>
        /// Repaint a piece, keeping its glazing glazed.
        ///
        /// A window is not one material: the kit models the frame and the pane as separate
        /// submeshes and names the second one for what it is. Painting every slot the same
        /// turned the City windows into dark slabs - a frame with no glass reads as a hole, and
        /// glass with no frame reads as nothing at all. So the ORIGINAL material's name decides:
        /// anything the pack called glass stays glass and everything else takes the paint.
        /// </summary>
        private static void Paint(GameObject go, Material paint, Material glass = null)
        {
            if (paint == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var originals = r.sharedMaterials;
                var slots = new Material[originals.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    bool glazed = glass != null && originals[i] != null
                        && originals[i].name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    slots[i] = glazed ? glass : paint;
                }
                r.sharedMaterials = slots;
            }
        }

        private static readonly Dictionary<string, Material> _flats = new Dictionary<string, Material>();

        /// <summary>An opaque, untextured URP Lit material in one of Ashcombe's colours.</summary>
        private static Material Flat(string name, Color colour)
        {
            if (_flats.TryGetValue(name, out var existing) && existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.enableInstancing = true;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            _flats[name] = m;
            return m;
        }
#endif
    }
}
