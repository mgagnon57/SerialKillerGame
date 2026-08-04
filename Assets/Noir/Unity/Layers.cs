using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// What is currently drawn, one switch per kind of thing.
    ///
    /// WHY THIS EXISTS. Everything that dressed the town used to hang off a single flag,
    /// VillageHost.ShowBuildings: either you got the survey plan or you got all four thousand
    /// renderers of brick, trees, farm clutter and signage at once. There was no way to ask the
    /// question people actually ask - "what does the LAND look like" - because the moment you
    /// turned the town on, forty trees stood in front of every lot boundary you were trying to
    /// read.
    ///
    /// THE HARD PART WAS NOT THE SWITCH, IT WAS THE BAKE. CityChunker combines every renderer
    /// under a node into a handful of meshes and DestroyImmediates the originals, so a single
    /// bake over the whole city left nothing per-layer to switch off - the trees and the walls
    /// had already become the same mesh. So each layer is baked SEPARATELY, under its own root,
    /// and the root is what toggles. The cost is that two layers sharing a material no longer
    /// merge into one draw call; the gain is that any of them can be taken away at runtime for
    /// free, with no rebuild and no reload.
    ///
    /// NOTHING HERE CHANGES THE SIMULATION. A hidden car still drives its lane and is still
    /// counted by the jam instrument; a hidden villager still goes to work. This is a way of
    /// LOOKING at Rossville, not a different Rossville - the same distinction HideActors has
    /// always drawn.
    /// </summary>
    public static class Layers
    {
        /// <summary>
        /// One switchable kind of thing. The order is the order the panel lists them, which is
        /// roughly ground-upward: what the land is, then what was built on it, then what moves.
        /// </summary>
        public enum Kind
        {
            Streets,        // asphalt, kerbs, painted lanes, crossings
            Parking,        // lay-bys and parking aprons
            Signs,          // road signs and street nameplates
            Signals,        // traffic light heads and their posts
            RailBed,        // the real CSX line at grade
            Rail,           // the elevated El
            Powerlines,     // poles and spans
            Farm,           // barns, silos, crops, yard clutter
            Buildings,      // the authored places - school, bank, firehouse
            Districts,      // downtown blocks
            Houses,         // the suburb - residential stock
            Story,          // the story props
            Trees,          // every tree, bush, hedge and fallen trunk
            Lamps,          // street lighting fixtures and window glazing
            Traffic,        // moving vehicles
            People,         // the citizens

            /// <summary>
            /// The county's own lot lines, drawn over whatever is standing on them.
            ///
            /// This used to be an EITHER/OR - CityOutlines only built when ShowBuildings was
            /// false, so you got the survey plan or the town and never both. That is exactly
            /// backwards for the question it answers best, which is "is this house where the
            /// parcel says it is". Now it is a layer like the rest - on by default, off with one
            /// click, and the choice remembered.
            /// </summary>
            Plan,

            /// <summary>
            /// Street names and addresses, drawn in screen space over whatever is standing.
            ///
            /// Its own switch rather than riding on Plan: the names are wanted with the built town
            /// up and the lot lines off, which is the view the owner reads the map in.
            /// </summary>
            Labels,
        }

        /// <summary>How the panel labels each one, and the order it lists them in.</summary>
        public static readonly Kind[] All =
        {
            Kind.Streets, Kind.Parking, Kind.Signs, Kind.Signals,
            Kind.RailBed, Kind.Rail, Kind.Powerlines, Kind.Farm,
            Kind.Buildings, Kind.Districts, Kind.Houses, Kind.Story,
            Kind.Trees, Kind.Lamps, Kind.Traffic, Kind.People, Kind.Plan, Kind.Labels,
        };

        public static string Label(Kind k)
        {
            switch (k)
            {
                case Kind.Streets:    return "Streets";
                case Kind.Parking:    return "Parking";
                case Kind.Signs:      return "Road signs";
                case Kind.Signals:    return "Traffic lights";
                case Kind.RailBed:    return "Railroad (CSX)";
                case Kind.Rail:       return "Elevated rail";
                case Kind.Powerlines: return "Power lines";
                case Kind.Farm:       return "Farm";
                case Kind.Buildings:  return "Civic buildings";
                case Kind.Districts:  return "Downtown blocks";
                case Kind.Houses:     return "Houses";
                case Kind.Story:      return "Story props";
                case Kind.Trees:      return "Trees & hedges";
                case Kind.Lamps:      return "Street lighting";
                case Kind.Traffic:    return "Traffic";
                case Kind.People:     return "People";
                case Kind.Plan:       return "Parcel lines";
                case Kind.Labels:     return "Street names";
                default:              return k.ToString();
            }
        }

        // ---- the registry ----------------------------------------------------------------
        //
        // A layer can own more than one root: "Houses" is CitySuburb, and the lamps are
        // SunRig's fixtures, which are built in a different pass from everything else.
        private static readonly Dictionary<Kind, List<GameObject>> _roots =
            new Dictionary<Kind, List<GameObject>>();

        private static readonly Dictionary<Kind, bool> _on = new Dictionary<Kind, bool>();

        /// <summary>
        /// Layers that are not a GameObject anybody can switch off.
        ///
        /// SunRig's street lamps and window glazing are the case this exists for: they are LISTS
        /// of renderers scattered across the town's own meshes, not a node of their own, so the
        /// only way to hide them is to ask SunRig to do it. A callback keeps that knowledge in
        /// SunRig, where it belongs, instead of teaching this class what a lantern is.
        /// </summary>
        private static readonly Dictionary<Kind, List<System.Action<bool>>> _hooks =
            new Dictionary<Kind, List<System.Action<bool>>>();

        private const string KeyPrefix = "noir.layer.";

        /// <summary>
        /// Hand a built root to the switch that owns it. Null is accepted and ignored, because
        /// a builder that had nothing to build legitimately returns one - the elevated rail
        /// needs a `place railway` the real map does not have.
        /// </summary>
        public static void Register(Kind kind, GameObject root)
        {
            if (root == null) return;

            if (!_roots.TryGetValue(kind, out var list))
                _roots[kind] = list = new List<GameObject>();
            list.Add(root);

            // Apply whatever the switch is already set to, so a layer registered late comes up
            // in the right state rather than flashing on until somebody touches the panel.
            root.SetActive(IsOn(kind));
        }

        /// <summary>Register a layer that answers to a callback rather than to a root.</summary>
        public static void Register(Kind kind, System.Action<bool> setVisible)
        {
            if (setVisible == null) return;

            if (!_hooks.TryGetValue(kind, out var list))
                _hooks[kind] = list = new List<System.Action<bool>>();
            list.Add(setVisible);

            setVisible(IsOn(kind));
        }

        /// <summary>Forget every root and hook. Called when the town is rebuilt, or the
        /// dictionaries keep pointers to destroyed objects and every toggle throws.</summary>
        public static void Clear() { _roots.Clear(); _hooks.Clear(); }

        private static readonly GameObject[] None = new GameObject[0];

        /// <summary>
        /// The roots registered to a layer, for the caller that has to do something to each of
        /// them - which in practice means the bake. Never null.
        /// </summary>
        public static IReadOnlyList<GameObject> RootsOf(Kind kind) =>
            _roots.TryGetValue(kind, out var list) ? (IReadOnlyList<GameObject>)list : None;

        /// <summary>
        /// Whether a layer is drawn. Defaults to ON: the town opens showing everything it has,
        /// and taking things away is the deliberate act. The opposite default - which is what
        /// this project had - means somebody who presses Play sees a dark plan and has no way of
        /// knowing there is a town behind it.
        /// </summary>
        public static bool IsOn(Kind kind)
        {
            if (_on.TryGetValue(kind, out bool cached)) return cached;
            bool stored = PlayerPrefs.GetInt(KeyPrefix + kind, 1) == 1;
            _on[kind] = stored;
            return stored;
        }

        public static void Set(Kind kind, bool on)
        {
            _on[kind] = on;
            PlayerPrefs.SetInt(KeyPrefix + kind, on ? 1 : 0);
            PlayerPrefs.Save();

            if (_roots.TryGetValue(kind, out var list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null) { list.RemoveAt(i); continue; }   // destroyed by a rebuild
                    list[i].SetActive(on);
                }
            }

            if (_hooks.TryGetValue(kind, out var hooks))
                foreach (var hook in hooks) hook(on);
        }

        public static void Toggle(Kind kind) => Set(kind, !IsOn(kind));

        public static void SetAll(bool on)
        {
            foreach (var k in All) Set(k, on);
        }

        /// <summary>
        /// The land and what was surveyed onto it, and nothing that was built or planted.
        ///
        /// This is the view the town is actually judged on while the ground work is being done:
        /// elevation, terrain textures, the road surfaces and the railroad, with no trees in
        /// front of them and no houses standing on them. It is a preset rather than a mode -
        /// every switch it sets can be set by hand.
        /// </summary>
        public static void GroundAndRoadsOnly()
        {
            SetAll(false);
            Set(Kind.Streets, true);
            Set(Kind.Parking, true);
            Set(Kind.RailBed, true);
        }

        /// <summary>How many layers are currently drawn, for the panel's header.</summary>
        public static int CountOn()
        {
            int n = 0;
            foreach (var k in All) if (IsOn(k)) n++;
            return n;
        }
    }
}
