using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// What the game built inside every seated building, written back beside SurveyReport's
    /// verdicts.
    ///
    /// THE LOOP THIS CLOSES. The browser map now authors floor plans - Content/floorplans/*.json
    /// - and SeatOnSurvey hands each one to FloorPlans.For, which returns an AuthoredInterior or
    /// null depending on whether the door lines up, the units count matches, the footprint is
    /// still the shape it was drawn against. None of that comes back. The owner draws a kitchen
    /// at 611,813, the game either stamps it there or quietly falls back to a generated one, and
    /// there is no symptom - the house still has a kitchen, just not necessarily his.
    ///
    /// So the survey passes note what they built on each measured footprint, this resolves each
    /// note against the finished world once WorldBuilder has stamped every room and every stick
    /// of furniture, and the map can show a plan next to the one that built it. Same shape as
    /// SurveyReport: a REPORT and not content - derived, rewritten on every build, safe to delete.
    ///
    /// WHAT A NOTE COVERS, AND WHY IT IS NOT "WHAT MOVED" (2026-08-20). Note() used to be called
    /// from one place - the branch of SeatOnSurvey's loop where a building successfully moves onto
    /// its measurement - so the report described 147 buildings out of a town that builds well over
    /// four hundred on surveyed ground. Everything else was invisible to the map by construction:
    /// a building refused for standing in a street, one that yielded a taken spot, one on a
    /// footprint the owner dated later than 1991, and every one of the 313 FillFromSurvey raises
    /// on lots city.txt never had. All of those BUILD. The map asks "what did the game build
    /// here", not "what did it move here", so the note now covers every building the game stands
    /// on a parcel that has a measured footprint for it to be keyed against - carrying that
    /// footprint's own parcel and index, which is the browser map's key, and never an invented
    /// one.
    ///
    /// THE TWO-STEP IS FORCED BY WHEN THE FACTS EXIST. SeatOnSurvey has the parcel, the index and
    /// the PlaceSpec while the layout is still just data - no rooms, no furniture, because those
    /// are stamped by WorldBuilder afterwards. Note() runs there and keeps only what survives
    /// that gap: a key stable enough to find the same building again. Write() runs once
    /// TownPipeline has the built WorldModel in hand and does the actual look-up.
    /// </summary>
    public static class InteriorsReport
    {
        public const string FileName = "game-interiors.json";

        /// <summary>One seated building, as SeatOnSurvey saw it - before the world exists.</summary>
        private readonly struct Taken
        {
            public readonly int Parcel;
            public readonly int Index;
            public readonly ulong Key;
            public readonly bool Authored;

            public Taken(int parcel, int index, ulong key, bool authored)
            {
                Parcel = parcel; Index = index; Key = key; Authored = authored;
            }
        }

        private static readonly List<Taken> _notes = new List<Taken>();

        /// <summary>One seated building that resolved to a built Place - what Write() found, kept
        /// for whoever needs to walk the same buildings this report describes without re-deriving
        /// the parcel/index -&gt; Place lookup from scratch (the mesh exporter this report was
        /// planned alongside is the first such caller).</summary>
        public readonly struct Resolved
        {
            public readonly int Parcel;
            public readonly int Index;
            public readonly Place Place;
            public readonly bool Authored;

            public Resolved(int parcel, int index, Place place, bool authored)
            {
                Parcel = parcel; Index = index; Place = place; Authored = authored;
            }
        }

        private static readonly List<Resolved> _resolved = new List<Resolved>();

        /// <summary>Every noted building Write() could still find in the built world, in the order
        /// it found them. Empty until Write() has run for this build.</summary>
        public static IReadOnlyList<Resolved> Noted => _resolved;

        public static void Clear() { _notes.Clear(); _resolved.Clear(); }

        /// <summary>
        /// Called from wherever a survey pass has the parcel, the index and the PlaceSpec
        /// together - SeatOnSurvey at every exit of its own loop, and FillFromSurvey as it raises
        /// a building - and always before the world exists.
        ///
        /// <paramref name="authored"/> is whether the owner's own floor plan actually took, so it
        /// is only ever true where the caller ran the FloorPlans.For attach. A pass that leaves a
        /// building on its generated interior says false, which is the truth about that building
        /// rather than a missing answer.
        ///
        /// KEYED THE SAME WAY WorldBuilder KEYS A PLACE: <see cref="Keys.Of"/> over
        /// <c>spec.Key</c> if the owner set one, else <c>spec.Name</c> - see Place.KeySource and
        /// the clash check in WorldBuilder.Build, which is what makes this exact rather than a
        /// best guess. Anything else (array index, insertion order) drifts the moment a building
        /// is added or removed upstream of this pass.
        /// </summary>
        public static void Note(int parcel, int index, PlaceSpec spec, bool authored)
        {
            if (spec == null) return;
            string keySource = string.IsNullOrEmpty(spec.Key) ? spec.Name : spec.Key;
            _notes.Add(new Taken(parcel, index, Keys.Of(keySource), authored));
        }

        /// <summary>
        /// Resolves every note against the built world and writes the interiors out.
        ///
        /// A note with no matching Place did not fail silently - a LATER pass took that building
        /// down. DowntownFromSanborn drops everything standing on a lot whose footprint the owner
        /// dated later than 1991 and lays a 1913 terrace over it instead, and those units are new
        /// places with new keys. There is no building left to report on, and SKIPPING is the
        /// correct outcome, not a bug to chase - the count is said out loud below.
        /// </summary>
        public static void Write(WorldModel world)
        {
            _resolved.Clear();
            if (world == null || _notes.Count == 0) return;

            // Place.Key is unique within a built world - WorldBuilder.Build throws on a clash -
            // so this lookup is exact, the same guarantee Note() relies on going in.
            var byKey = new Dictionary<ulong, Place>(world.PlaceCount);
            foreach (var place in world.AllPlaces) byKey[place.Key] = place;

            // One pass over the world's furniture, bucketed by room, rather than one scan per
            // building - AllFurniture has no per-place index and this town's furniture count is
            // the kind of number that makes an O(buildings x furniture) scan worth avoiding.
            var furnitureByRoom = new Dictionary<int, List<Furniture>>();
            foreach (var f in world.AllFurniture)
            {
                int key = f.Room.Value;
                if (!furnitureByRoom.TryGetValue(key, out var list))
                    furnitureByRoom[key] = list = new List<Furniture>();
                list.Add(f);
            }

            var rows = new List<string>(_notes.Count);
            int skipped = 0;
            foreach (var note in _notes)
            {
                if (!byKey.TryGetValue(note.Key, out var place)) { skipped++; continue; }

                _resolved.Add(new Resolved(note.Parcel, note.Index, place, note.Authored));
                rows.Add(BuildingJson(note.Parcel, note.Index, place, note.Authored, world,
                                      furnitureByRoom));
            }

            if (rows.Count == 0) return;
            var path = System.IO.Path.Combine(ContentLoader.Root, "..", "tools", FileName);

            var sb = new StringBuilder();
            sb.Append("{\"when\":\"").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
              .Append("\",\"buildings\":[").Append(string.Join(",", rows)).Append("]}");

            try
            {
                System.IO.File.WriteAllText(System.IO.Path.GetFullPath(path), sb.ToString());
                Debug.Log($"[interiors] {rows.Count} building(s) reported back to the browser map"
                        + (skipped > 0 ? $", {skipped} noted but never built" : "") + ".");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[interiors] could not write " + FileName + ": " + e.Message);
            }
        }

        private static string BuildingJson(int parcel, int index, Place place, bool authored,
                                            WorldModel world, Dictionary<int, List<Furniture>> furnitureByRoom)
        {
            var rooms = new List<string>();
            var furniture = new List<string>();
            foreach (var roomId in world.RoomsIn(place.Id))
            {
                var room = world.GetRoom(roomId);
                if (room == null) continue;
                rooms.Add(RoomJson(room));

                if (!furnitureByRoom.TryGetValue(roomId.Value, out var pieces)) continue;
                foreach (var f in pieces) furniture.Add(FurnitureJson(f));
            }

            var b = place.Bounds;
            var sb = new StringBuilder();
            sb.Append("{\"parcel\":").Append(parcel)
              .Append(",\"index\":").Append(index)
              .Append(",\"place\":\"").Append(Escape(place.Name)).Append('"')
              .Append(",\"bounds\":").Append(RectJson(b.X, b.Y, b.W, b.H))
              .Append(",\"door\":").Append(place.Door.IsValid ? PointJson(place.Door.X, place.Door.Y) : "null")
              .Append(",\"units\":").Append(place.Units)
              .Append(",\"authored\":").Append(authored ? "true" : "false")
              .Append(",\"rooms\":[").Append(string.Join(",", rooms))
              .Append("],\"furniture\":[").Append(string.Join(",", furniture))
              .Append("]}");
            return sb.ToString();
        }

        private static string RoomJson(Room room)
        {
            var b = room.Bounds;
            return "{\"x\":" + b.X + ",\"y\":" + b.Y + ",\"w\":" + b.W + ",\"h\":" + b.H
                 + ",\"kind\":\"" + room.Kind + "\",\"name\":\"" + Escape(room.Name) + "\"}";
        }

        private static string FurnitureJson(Furniture f)
        {
            var b = f.Footprint;
            return "{\"x\":" + b.X + ",\"y\":" + b.Y + ",\"w\":" + b.W + ",\"h\":" + b.H
                 + ",\"kind\":\"" + f.Kind + "\",\"height\":"
                 + f.Height.ToString("0.##", CultureInfo.InvariantCulture)
                 + ",\"model\":\"" + Escape(f.Model) + "\"}";
        }

        private static string RectJson(int x, int y, int w, int h) =>
            "{\"x\":" + x + ",\"y\":" + y + ",\"w\":" + w + ",\"h\":" + h + "}";

        private static string PointJson(int x, int y) => "{\"x\":" + x + ",\"y\":" + y + "}";

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "").Replace("\"", "'").Replace("\n", " ");
    }
}
