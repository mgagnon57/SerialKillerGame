using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Survey;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// PUTS THE OWNER'S 1991 BUSINESSES INTO THE TOWN.
    ///
    /// Reads Content/business-1991.txt and, for each unit he has ruled on, sets the place's KIND
    /// and its display name before the world is built. The kind is the point: it decides the
    /// interior, the jobs, the opening hours and what the people inside do all day. The name is
    /// only what it says on the front.
    ///
    /// ON THE LAYOUT, BEFORE WorldBuilder, and that is not a detail. Interiors, rooms, furniture,
    /// counters, job slots and households are all generated FROM the kind as the world is built,
    /// so a kind changed afterwards would be a tavern with a shop's rooms in it. This is also why
    /// the panel that writes the file says the change lands on the next build rather than
    /// pretending to swap a building under the player.
    ///
    /// THE HANDLE IS REMEMBERED BECAUSE THE NAME IS ABOUT TO CHANGE. A ruled unit stops being
    /// "Rossville unit 12" and becomes "Shorty's", so nothing downstream - including the panel
    /// that has to write the next ruling - could find its way back to the key. <see cref="HandleOf"/>
    /// keeps that mapping for the life of the build.
    /// </summary>
    public static class BusinessFromRulings
    {
        private static readonly Dictionary<string, string> _handles =
            new Dictionary<string, string>();

        /// <summary>
        /// The city.txt handle behind a place's displayed name, for anything that needs to write
        /// a ruling about it. Returns the name unchanged when no ruling renamed it, so a caller
        /// can always use the result as the key.
        /// </summary>
        public static string HandleOf(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return displayName;
            return _handles.TryGetValue(displayName, out var handle) ? handle : displayName;
        }

        /// <summary>
        /// Write one unit's ruling, keeping every other ruling in the file.
        ///
        /// THE SAME ATOMIC WRITE ParcelNotes USES, and for the same reason: this file is the sole
        /// copy of something only the owner knows, it is rewritten whole on every save, and a
        /// plain WriteAllText truncates the real file before it fills it - so a crash or a killed
        /// editor mid-save takes the lot. Temp file, then File.Replace, which leaves the previous
        /// contents in business-1991.txt.bak for nothing.
        ///
        /// Pass an empty kind/business/trade to clear that field; a ruling with nothing left in it
        /// drops out of the file entirely, so "I was wrong about this one" is a real action.
        /// </summary>
        public static void Save(string unit, string kind, string business, string trade)
        {
            if (string.IsNullOrWhiteSpace(unit)) return;

            var all = new Dictionary<string, BusinessRulings.Ruling>();
            foreach (var kv in BusinessRulings.All) all[kv.Key] = kv.Value;

            all[unit] = new BusinessRulings.Ruling
            {
                Kind = (kind ?? "").Trim(),
                Business = (business ?? "").Trim(),
                Trade = (trade ?? "").Trim(),
            };

            string path = System.IO.Path.Combine(ContentLoader.Root, BusinessRulings.FileName);
            string temp = path + ".tmp";
            string backup = path + ".bak";

            System.IO.File.WriteAllText(temp, BusinessRulings.Serialise(all));
            if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, backup);
            else System.IO.File.Move(temp, path);

            Debug.Log($"[business] ruled '{unit}': kind '{all[unit].Kind}', "
                    + $"business '{all[unit].Business}'. It lands on the next build.");
        }

        public static int Apply(VillageLayout layout)
        {
            _handles.Clear();
            if (layout == null) return 0;

            var table = PlaceKindTable.Current;
            int named = 0, retyped = 0, unknown = 0;

            for (int i = 0; i < layout.Places.Count; i++)
            {
                var spec = layout.Places[i];
                var ruling = BusinessRulings.For(spec.Name);
                if (ruling == null) continue;

                string handle = spec.Name;

                if (ruling.Kind.Length > 0)
                {
                    // BOTH LOOKUPS. kinds.txt resolves a kind through its `words` line, and the
                    // panel writes the enum's own name - which is usually in that line but is not
                    // guaranteed to be. Asking by name as well means a kind the player can pick
                    // can always be read back.
                    var kind = default(PlaceKind);
                    bool known = table != null
                              && (table.TryKindOf(ruling.Kind, out kind)
                               || table.TryNamed(ruling.Kind, out kind));
                    if (known)
                    {
                        if (spec.Kind != kind) retyped++;
                        spec.Kind = kind;
                    }
                    else
                    {
                        // SAID OUT LOUD, because this is the one failure that is otherwise
                        // silent: an unrecognised kind falls through to a default and the
                        // building merely looks wrong. kinds.txt resolves through a `words` line,
                        // so a kind renamed there without its word moving is exactly this.
                        Debug.LogWarning($"[business] '{handle}' is ruled kind '{ruling.Kind}', "
                                       + "which nothing in kinds.txt answers to. Left as "
                                       + $"{spec.Kind}. See BusinessRulings.");
                        unknown++;
                    }
                }

                if (ruling.Business.Length > 0)
                {
                    spec.Name = ruling.Business;
                    _handles[spec.Name] = handle;
                    named++;
                }

                if (ruling.Trade.Length > 0)
                    spec.Human = ruling.Trade;
            }

            // Greppable, and it prints even at zero - the whole point of this file is that the
            // owner can write a ruling the game never reads, and a silent pass is how that
            // happens. Same reasoning as [lots], [walks] and [roads].
            Debug.Log($"[business] {BusinessRulings.Count} unit(s) ruled in "
                    + $"{BusinessRulings.FileName}: {named} named, {retyped} re-typed"
                    + (unknown > 0 ? $", {unknown} WITH A KIND NOTHING ANSWERS TO" : "")
                    + ".");

            return named + retyped;
        }
    }
}
