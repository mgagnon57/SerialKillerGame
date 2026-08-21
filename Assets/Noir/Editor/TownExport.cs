using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Writes what the browser house-inspector tool needs to draw a building from outside the
    /// game: one OBJ+MTL per seated building in <c>tools/preview-cache/</c>, and the furniture
    /// palette it can offer a floor plan - <c>tools/furniture-palette.json</c>. Both are derived,
    /// rewritten on every run, gitignored, and meaningless without the run that produced them -
    /// the same standing as <c>InteriorsReport</c>'s own <c>game-interiors.json</c>, which this
    /// reads straight out of.
    ///
    /// Run headlessly:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.TownExport.ForViewer
    ///
    /// THE TOWN THIS BUILDS, AND WHY IT ISN'T THE WHOLE ONE. <see cref="TownPipeline.Build"/> is
    /// Core-only - it produces a <see cref="WorldModel"/> and nothing you can see. Every mesh in
    /// Rossville comes from a second, separate pass (<c>VillageMesh</c>, <c>CityBuildings</c>,
    /// then <c>CityChunker.Bake</c> which combines and DESTROYS the per-building renderers to cut
    /// the draw-call count). Of those, only <see cref="CityBuildings"/> ever stands a building on
    /// its OWN GameObject in the first place: an ordinary dwelling is procedural massing that
    /// <c>VillageMesh.BuildWalls</c> pours straight into 64 m chunk meshes grouped by material,
    /// with no per-place identity to recover afterwards - not something the bake did, something
    /// the geometry was never given. So this calls <c>CityBuildings.Build</c> alone, over a scratch
    /// root nothing ever bakes, and skips <c>VillageMesh</c> and the chunker entirely: the owner's
    /// hand-made houses, the terrace rows and the pack landmarks (firehouse, gas station, water
    /// tower, elevator, farm buildings) come out with real geometry; every plain massed house
    /// comes out with none, because there is nothing standing to walk. That building is counted
    /// SKIPPED, the same as a genuinely empty lot - the census line does not distinguish "not
    /// captured" from "not built", which is honest about what this tool can currently see.
    /// </summary>
    public static class TownExport
    {
        private const float MetresToFeet = 3.28084f;

        private static readonly RoomKind[] DomesticRooms =
        {
            RoomKind.Hall, RoomKind.Living, RoomKind.Kitchen, RoomKind.Bedroom,
            RoomKind.Bathroom, RoomKind.Scullery, RoomKind.Workroom,
        };

        [MenuItem("Noir/Export For The Map Tool")]
        public static void ForViewer()
        {
            int code = 0;
            GameObject scratch = null;

            try
            {
                if (!ContentLoader.Exists)
                    throw new Exception($"content not found at {ContentLoader.Root}");

                // THE REAL TOWN, AND NOTHING TRUSTED FROM BEFORE IT. InteriorsReport.Noted is not
                // cleared by BuildUnsurveyed (a fixture build), so the only safe read is straight
                // after THIS build - see the class header on why TownPipeline.Build is the one way
                // in.
                var built = TownPipeline.Build();
                var world = built.World;
                var noted = InteriorsReport.Noted;

                scratch = new GameObject("TownExportScratch");
                var authored = CityBuildings.Build(world, scratch.transform);

                var candidates = new List<Transform>();
                if (authored != null)
                    foreach (Transform child in authored.transform)
                        // Fleet() parks a squad car, an engine or a bus at the kerb outside its
                        // building, on the same root as the building itself - see CityBuildings'
                        // own header on Fleet. It is not part of the building, so it is excluded
                        // by the one thing that reliably marks it: the pack's own vehicle naming.
                        if (!child.name.StartsWith("Car_", StringComparison.OrdinalIgnoreCase))
                            candidates.Add(child);

                string cacheDir = Path.GetFullPath(
                    Path.Combine(ContentLoader.Root, "..", "tools", "preview-cache"));
                Directory.CreateDirectory(cacheDir);
                foreach (var f in Directory.GetFiles(cacheDir, "*.obj")) File.Delete(f);
                foreach (var f in Directory.GetFiles(cacheDir, "*.mtl")) File.Delete(f);

                int meshed = 0, skipped = 0, unreadableTotal = 0;

                // WHICH OF THE THINGS STANDING WERE NEVER ASKED ABOUT. On 2026-08-20 this run
                // read "1 buildings meshed, 146 skipped" and the reason was not the export at
                // all: InteriorsReport only knew about the buildings SeatOnSurvey had MOVED, so
                // the walk below asked 147 questions and 24 pieces were standing, almost none of
                // them on a moved building. The one that matched was parcel 225 - the fire
                // station, a city.txt place that happened to be seated onto its measured
                // footprint with a pack landmark on it. Both halves are fixed, and this counter
                // is what stops the same blindness being invisible next time: a model standing in
                // Rossville that no seated building claims is either a building out in the
                // country, on no parcel at all, or a hole in the report.
                var claimed = new HashSet<Transform>();

                foreach (var note in noted)
                {
                    var place = note.Place;
                    var b = place.Bounds;

                    // WORLD-SPACE OVERLAP, NOT A NAME MATCH. CityBuildings parents every piece it
                    // builds straight under its own root with no reference back to the Place it
                    // stood on (Record() keeps only a height, for picking - see its own comment on
                    // why). A terrace stands ONE model over the union of several places' bounds
                    // (CityBuildings.Build's own terrace-row account), so this is an OVERLAP test
                    // and not containment - every unit under that roof gets the whole model,
                    // each in its own building-local frame. That is a known duplication, not a bug:
                    // see the class header.
                    float minX = b.X, maxX = b.X + b.W;
                    float minZ = -(b.Y + b.H), maxZ = -b.Y;

                    var parts = new List<Renderer>();
                    foreach (var child in candidates)
                    {
                        var rends = child.GetComponentsInChildren<Renderer>(false);
                        if (rends.Length == 0) continue;

                        var cb = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) cb.Encapsulate(rends[i].bounds);
                        if (cb.max.x < minX || cb.min.x > maxX || cb.max.z < minZ || cb.min.z > maxZ)
                            continue;

                        parts.AddRange(rends);
                        claimed.Add(child);
                    }

                    if (parts.Count == 0) { skipped++; continue; }

                    float groundY = Space3D.GroundUnder(b);
                    if (WriteObj(cacheDir, note.Parcel, note.Index, b, groundY, parts,
                                 out int unreadable))
                        meshed++;
                    else
                        skipped++;
                    unreadableTotal += unreadable;
                }

                WritePalette(cacheDir, out int yours, out int pack);

                Debug.Log($"[export] {meshed} buildings meshed, {skipped} skipped of "
                        + $"{noted.Count} reported, {candidates.Count - claimed.Count} of "
                        + $"{candidates.Count} models standing matched no reported building, "
                        + $"palette {yours} yours + {pack} pack."
                        + (unreadableTotal > 0
                            ? $" {unreadableTotal} renderer(s) skipped for not being Read/Write "
                            + "enabled - run Noir/Make City Meshes Readable."
                            : ""));

                // A CACHE WITH NOTHING IN IT IS A FAILURE, not a quiet success. The spec's
                // Testing section asks for a non-zero exit on zero buildings written, because
                // the caller that matters (tools/verify_run.py, off the map's Publish button)
                // can only see the exit code and a log it does not parse. Every path that gets
                // here has just deleted the previous cache, so a silent zero would also leave
                // the tool with nothing where it had something.
                if (meshed == 0)
                {
                    Debug.LogError("[export] wrote ZERO buildings - the preview cache is empty. "
                                 + "Nothing under CityBuildings overlapped a seated place.");
                    code = 1;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[export] FAILED: " + ex);
                code = 1;
            }
            finally
            {
                if (scratch != null) UnityEngine.Object.DestroyImmediate(scratch);
            }

            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>
        /// One building: every renderer that overlapped it, transformed to world, then re-based so
        /// (0,0,0) is the WORLD position of the building bounds' own NW corner at its own ground
        /// height - exactly the frame <c>tools/preview-cache/&lt;parcel&gt;-&lt;index&gt;.obj</c>'s
        /// contract promises a reader.
        ///
        /// THE Z AXIS IS DELIBERATELY MIRRORED FROM UNITY'S OWN. <see cref="Space3D"/> negates
        /// tile-Y into world-Z so Unity's +Z points geographic north; this file undoes that one
        /// negation so the OBJ's local Z runs the same way the tile grid's Y does (and the way
        /// InteriorsReport's own room/furniture JSON already reports x/y) - a reader placing a
        /// room at tile (x,y) needs no second sign flip to agree with this file. Mirroring one
        /// axis flips triangle winding, so every face is written b,a rather than a,b to put it
        /// back.
        /// </summary>
        private static bool WriteObj(string dir, int parcel, int index, TileRect bounds,
                                     float groundY, List<Renderer> parts, out int unreadable)
        {
            unreadable = 0;
            string baseName = $"{parcel}-{index}";

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var order = new List<Material>();

            // A vertex's normal is added to `normals` at the same index it was added to `verts`
            // at (see the loop below), so one index serves both - three ints per triangle, not
            // six.
            var faces = new Dictionary<Material, List<int>>();

            foreach (var r in parts)
            {
                Mesh mesh; Matrix4x4 xform; bool owned = false;

                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null) continue;
                    mesh = new Mesh();
                    smr.BakeMesh(mesh);
                    xform = smr.transform.localToWorldMatrix;
                    owned = true;
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    if (!mesh.isReadable) { unreadable++; continue; }
                    xform = mf.transform.localToWorldMatrix;
                }

                var mv = mesh.vertices;
                if (mv == null || mv.Length == 0) { if (owned) UnityEngine.Object.DestroyImmediate(mesh); continue; }
                var mn = mesh.normals;
                var normalXform = xform.inverse.transpose;

                int vBase = verts.Count;
                for (int i = 0; i < mv.Length; i++)
                {
                    var wp = xform.MultiplyPoint3x4(mv[i]);
                    verts.Add(new Vector3(wp.x - bounds.X, wp.y - groundY, -wp.z - bounds.Y));

                    Vector3 wn = mn != null && i < mn.Length
                        ? normalXform.MultiplyVector(mn[i]).normalized
                        : Vector3.up;
                    normals.Add(new Vector3(wn.x, wn.y, -wn.z));
                }

                var slots = r.sharedMaterials;
                int subs = Mathf.Min(mesh.subMeshCount, slots.Length);
                for (int s = 0; s < subs; s++)
                {
                    var mat = slots[s];
                    if (mat == null) continue;
                    if (!faces.TryGetValue(mat, out var list))
                    {
                        faces[mat] = list = new List<int>();
                        order.Add(mat);
                    }

                    var tris = mesh.GetTriangles(s);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int a = vBase + tris[t] + 1;
                        int b = vBase + tris[t + 1] + 1;
                        int c = vBase + tris[t + 2] + 1;
                        // Mirrored Z inverted the winding above - write a, c, b to put every face
                        // back facing outward.
                        list.Add(a); list.Add(c); list.Add(b);
                    }
                }

                if (owned) UnityEngine.Object.DestroyImmediate(mesh);
            }

            if (verts.Count == 0 || order.Count == 0) return false;

            var matNames = new Dictionary<Material, string>();
            var used = new HashSet<string>();
            var mtl = new StringBuilder();
            foreach (var mat in order)
            {
                string name = Sanitize(mat.name);
                string unique = name;
                int n = 1;
                while (!used.Add(unique)) unique = name + "_" + n++;
                matNames[mat] = unique;

                var c = mat.color;
                mtl.Append("newmtl ").Append(unique).Append('\n')
                   .Append("Kd ").Append(F(c.r)).Append(' ').Append(F(c.g)).Append(' ')
                   .Append(F(c.b)).Append('\n').Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, baseName + ".mtl"), mtl.ToString());

            var obj = new StringBuilder();
            obj.Append("# ").Append(baseName)
               .Append(" - Noir.Editor.TownExport, building-local metres, y-up\n")
               .Append("mtllib ").Append(baseName).Append(".mtl\n");
            foreach (var v in verts)
                obj.Append("v ").Append(F(v.x)).Append(' ').Append(F(v.y)).Append(' ')
                   .Append(F(v.z)).Append('\n');
            foreach (var vn in normals)
                obj.Append("vn ").Append(F(vn.x)).Append(' ').Append(F(vn.y)).Append(' ')
                   .Append(F(vn.z)).Append('\n');

            foreach (var mat in order)
            {
                obj.Append("usemtl ").Append(matNames[mat]).Append('\n');
                var f = faces[mat];
                for (int i = 0; i + 2 < f.Count; i += 3)
                {
                    obj.Append("f ").Append(f[i]).Append("//").Append(f[i]).Append(' ')
                       .Append(f[i + 1]).Append("//").Append(f[i + 1]).Append(' ')
                       .Append(f[i + 2]).Append("//").Append(f[i + 2]).Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(dir, baseName + ".obj"), obj.ToString());
            return true;
        }

        /// <summary>
        /// The furniture palette: <c>yours</c> from <c>Content/furniture-models.txt</c> (the
        /// owner's own models, same voice as <c>Content/models.txt</c> for buildings) and
        /// <c>pack</c> from Core's own <see cref="FurniturePlans"/> - the domestic room kinds
        /// only (<see cref="DomesticRooms"/>: everything above <c>RoomKind</c>'s own "not a house"
        /// divider), one row per <see cref="FurnitureKind"/> at the FIRST footprint it is placed
        /// at, since the same kind recurs at different sizes across rooms (a kitchen table is not
        /// a parlour table) and the palette wants one representative box per kind, not one per use.
        /// </summary>
        private static void WritePalette(string cacheDir, out int yoursCount, out int packCount)
        {
            var yours = ReadFurnitureModels();
            var pack = DomesticPackFootprints();
            yoursCount = yours.Count;
            packCount = pack.Count;

            var sb = new StringBuilder();
            sb.Append("{\"when\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
              .Append("\",\"yours\":[");
            for (int i = 0; i < yours.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var y = yours[i];
                sb.Append("{\"name\":\"").Append(Escape(y.name)).Append("\",\"model\":\"")
                  .Append(Escape(y.model)).Append("\",\"kind\":\"")
                  .Append(Escape(y.kind)).Append("\",\"w_ft\":").Append(F(y.wMetres * MetresToFeet))
                  .Append(",\"d_ft\":").Append(F(y.dMetres * MetresToFeet)).Append('}');
            }
            sb.Append("],\"pack\":[");
            for (int i = 0; i < pack.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = pack[i];
                sb.Append("{\"kind\":\"").Append(Escape(p.kind)).Append("\",\"w_ft\":")
                  .Append(F(p.wFt)).Append(",\"d_ft\":").Append(F(p.dFt)).Append('}');
            }
            sb.Append("]}");

            string path = Path.GetFullPath(Path.Combine(cacheDir, "..", "furniture-palette.json"));
            File.WriteAllText(path, sb.ToString());
        }

        private static List<(string kind, float wFt, float dFt)> DomesticPackFootprints()
        {
            var seen = new HashSet<FurnitureKind>();
            var list = new List<(string, float, float)>();
            foreach (var room in DomesticRooms)
                foreach (var item in FurniturePlans.For(room))
                {
                    if (!seen.Add(item.Kind)) continue;
                    list.Add((item.Kind.ToString(), item.W * MetresToFeet, item.H * MetresToFeet));
                }
            return list;
        }

        /// <summary>Content/furniture-models.txt: <c>name | model | kind | width_m x depth_m</c>.
        /// An absent file is not an error - no owner furniture yet ruled costs nothing, the same
        /// asymmetry Content/models.txt itself carries.</summary>
        private static List<(string name, string model, string kind, float wMetres, float dMetres)>
            ReadFurnitureModels()
        {
            var list = new List<(string, string, string, float, float)>();
            string path = Path.Combine(ContentLoader.Root, "furniture-models.txt");
            if (!File.Exists(path)) return list;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split('|');
                if (parts.Length < 4)
                {
                    Debug.LogWarning("[export] unreadable row in Content/furniture-models.txt: " + raw);
                    continue;
                }

                var dims = parts[3].Trim().Split(new[] { 'x', 'X' }, 2);
                if (dims.Length != 2
                    || !float.TryParse(dims[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float w)
                    || !float.TryParse(dims[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                {
                    Debug.LogWarning("[export] bad width x depth in Content/furniture-models.txt: " + raw);
                    continue;
                }

                list.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), w, d));
            }
            return list;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "material";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "material" : sb.ToString();
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "").Replace("\"", "'").Replace("\n", " ");

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
