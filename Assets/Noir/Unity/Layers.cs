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

            /// <summary>
            /// The alleys, split out from Streets so they can be taken away on their own.
            ///
            /// They were on their own switch the day the streets were being judged and the alleys
            /// were still known to be in the wrong place - an alley laid across a lot is exactly
            /// the sort of thing that makes a correct street look wrong.
            /// </summary>
            Alleys,

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
            Lamps,          // street lighting fixtures - the lanterns, and nothing else. A
                            // building's own windows are part of the building; see
                            // SunRig.SetWindowPanes for what that cost before it was true.
            Traffic,        // moving vehicles
            People,         // the citizens

            /// <summary>
            /// VillageMesh's own generated geometry - walls, roofs, furniture, frontage planting,
            /// the countryside scatter. The primitive stand-in town, drawn from the map rather
            /// than bought.
            ///
            /// It gets a switch because it did not have one. All six builders hung off the same
            /// root as the GROUND, so this geometry was drawn underneath the bought prefabs and no
            /// combination of the other seventeen switches could take it away - "roads and lot
            /// lines only" still came up full of houses. The ground itself stays outside the
            /// switch, because a plan needs a surface to dim to near-black.
            /// </summary>
            Massing,

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
            /// The buildings' own outlines, over the top of whatever is standing.
            ///
            /// Its own switch rather than riding on Plan, because the two answer different
            /// questions - where the boundary is, and where the house is - and the useful thing
            /// is putting the second over the first. They shared a mesh, so they shared a switch.
            ///
            /// Drawn ignoring depth: a building stands exactly on its own outline and hides it,
            /// so with the massing up this layer used to draw nothing and read as broken.
            /// </summary>
            Footprints,

            /// <summary>
            /// Street names and addresses, drawn in screen space over whatever is standing.
            ///
            /// Its own switch rather than riding on Plan: the names are wanted with the built town
            /// up and the lot lines off, which is the view the owner reads the map in.
            /// </summary>
            Labels,

            /// <summary>
            /// The cars standing at people's houses — which is nearly every car the town owns.
            ///
            /// Its own switch and not Parking's: Parking is public lots, this is the driveway of a
            /// private house, and the useful thing is being able to take the parked cars away and
            /// still see the lot lines under them. Added at the END of the enum on purpose —
            /// Layers persists by NAME (<c>KeyPrefix + kind</c> concatenates the enum's ToString),
            /// so a new member cannot renumber anybody's saved switches, but there is no reason to
            /// find that out the hard way.
            /// </summary>
            Driveways,
        }

        /// <summary>How the panel labels each one, and the order it lists them in.</summary>
        public static readonly Kind[] All =
        {
            // Kind.Rail - the ELEVATED railway - is deliberately not listed. It needs a
            // `place railway` in the map, Rossville has none and is not going to, and Rossville is
            // the only map this project still carries. The switch was permanently dead, so it is
            // off the panel rather than sitting there as a thing you can click for no result. The
            // builder and the enum member stay: a map that wants an El only has to say so.
            Kind.Streets, Kind.Alleys, Kind.Parking, Kind.Signs, Kind.Signals,
            Kind.RailBed, Kind.Powerlines, Kind.Farm,
            Kind.Buildings, Kind.Districts, Kind.Houses, Kind.Story,
            Kind.Trees, Kind.Lamps, Kind.Traffic, Kind.People, Kind.Massing,
            Kind.Plan, Kind.Footprints, Kind.Labels, Kind.Driveways,
        };

        public static string Label(Kind k)
        {
            switch (k)
            {
                case Kind.Streets:    return "Streets";
                case Kind.Alleys:     return "Alleys";
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
                case Kind.Trees:      return "Trees, hedges & fences";
                case Kind.Lamps:      return "Street lighting";
                case Kind.Traffic:    return "Traffic";
                case Kind.People:     return "People";
                case Kind.Massing:    return "Generated massing";
                case Kind.Plan:       return "Parcel lines";
                case Kind.Footprints: return "House outlines";
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

        /// <summary>
        /// Register something expensive that is only worth building if somebody asks to see it.
        ///
        /// THE PROBLEM THIS SOLVES. Trees, farm clutter and power lines were skipped entirely when
        /// their layer was off - 17,405 farm pieces, 12,804 trees and 789 poles that nothing was
        /// ever going to look at, and on a survey view that is the whole frame budget. The price
        /// was that switching one back ON did nothing until you left Play and came back, with a
        /// line in the log to explain it. A switch that needs the game restarted is not a switch,
        /// and nobody reads the log while they are looking at the town.
        ///
        /// So the saving stays and the restart goes: the builder is kept, and run the first time
        /// the layer is turned on. Startup skips what is switched off, and switching it on costs
        /// its build once, there and then.
        ///
        /// A layer built this way after the bake is not baked, so it draws in more calls than one
        /// that was there from the start. That is the trade, and it only applies to a layer that
        /// was off when the town came up.
        /// </summary>
        public static void RegisterLazy(Kind kind, System.Func<GameObject> build)
        {
            if (build == null) return;

            if (IsOn(kind)) { Register(kind, build()); return; }

            if (!_pending.TryGetValue(kind, out var list))
                _pending[kind] = list = new List<System.Func<GameObject>>();
            list.Add(build);
        }

        private static readonly Dictionary<Kind, List<System.Func<GameObject>>> _pending =
            new Dictionary<Kind, List<System.Func<GameObject>>>();

        private static void BuildPending(Kind kind)
        {
            if (!_pending.TryGetValue(kind, out var builders)) return;
            _pending.Remove(kind);        // before building, so a builder that toggles cannot loop

            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var build in builders) Register(kind, build());
            Debug.Log($"[layers] built {Label(kind)} on demand in {clock.ElapsedMilliseconds} ms - "
                    + "it was switched off when the town came up.");
        }

        /// <summary>Whether this layer has anything behind it: a root, a hook, or a builder
        /// waiting to run. False means the switch does nothing, and the panel says so.</summary>
        public static bool IsWired(Kind kind) =>
            (_roots.TryGetValue(kind, out var r) && r.Count > 0)
         || (_hooks.TryGetValue(kind, out var h) && h.Count > 0)
         || _pending.ContainsKey(kind);

        /// <summary>Forget every root, hook and pending builder. Called when the town is rebuilt,
        /// or the dictionaries keep pointers to destroyed objects and every toggle throws.</summary>
        public static void Clear() { _roots.Clear(); _hooks.Clear(); _pending.Clear(); }

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

            // BATCH MODE GETS THE COMPILED DEFAULT, NOT A PERSON'S PREFERENCES.
            //
            // `Set` already refused to WRITE PlayerPrefs headless; this is the read, and it was
            // still going to the same place - so whatever the owner last toggled in the editor
            // silently decided what every headless test measured. CLAUDE.md documents where that
            // ends: `noir.layer.Traffic = 0`, set once months earlier for a survey view, froze all
            // 160 vehicles in EVERY subsequent run including the tests, and three gate tests
            // reported it three different ways before anybody found the cause.
            //
            // Fixed HERE rather than by a `[RuntimeInitializeOnLoadMethod]` in the test assembly,
            // which was the other candidate and is a trap: `Noir.PlayTests.asmdef` has no
            // `excludePlatforms`, so such a hook fires on EVERY entry into Play, not only under
            // the test runner - and the owner would press Play one day and permanently lose his
            // trees, farm and power lines. There is no hook to mis-fire this way.
            if (Application.isBatchMode)
            {
                _on[kind] = true;
                return true;
            }

            bool stored = PlayerPrefs.GetInt(KeyPrefix + kind, 1) == 1;
            _on[kind] = stored;
            return stored;
        }

        public static void Set(Kind kind, bool on)
        {
            _on[kind] = on;

            // REMEMBERED FOR A PERSON, AND THERE IS NOBODY HERE IN BATCH MODE.
            //
            // These switches are a UI preference: which layers the owner last chose to look at.
            // Persisting them is right when a person flicked the switch and wrong when a TEST did,
            // and a test does - LayerProof walks every layer combination to photograph them, and
            // each step went through here and rewrote the preference. Whatever combination the run
            // happened to end on was then what the editor came up with next time.
            //
            // That is not hypothetical and it is not harmless. On 2026-08-07 a diagnostic run left
            // `noir.layer.People = 0`, so VillageHost skipped building AgentMeshView entirely, and
            // WhyAreThePeopleNotAnimating failed with "no AgentMeshView - are the people drawn?"
            // and "[body] 0 Animators in the scene". No exception, nothing in the log, and it
            // passed again whenever some other run happened to leave the switch on. It was written
            // down in this project's own notes as a FLAKY TEST. It was a test reading a preference
            // another test had scribbled on.
            //
            // Same rule as the audio and the boot curtain: batch mode has no person whose
            // preferences matter. The switch still takes effect for this run; it just does not
            // outlive it.
            if (!Application.isBatchMode)
            {
                PlayerPrefs.SetInt(KeyPrefix + kind, on ? 1 : 0);
                PlayerPrefs.Save();
            }

            // Built the first time it is asked for, not at startup - see RegisterLazy. Must come
            // after _on is set, because Register activates the new root from it.
            if (on) BuildPending(kind);

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

        /// <summary>
        /// Say out loud which switches have nothing behind them.
        ///
        /// Called once the town is up and everything has had its chance to register. Some of these
        /// are legitimate and permanent - the elevated rail needs a `place railway` that
        /// Rossville's map does not have - and some mean a builder returned null and nobody
        /// noticed. Either way a switch that does nothing should be visible as one, and the panel
        /// marks the same layers this names.
        /// </summary>
        public static void Audit()
        {
            var dead = new List<string>();
            foreach (var k in All) if (!IsWired(k)) dead.Add(Label(k));

            if (dead.Count == 0)
            {
                Debug.Log($"[layers] all {All.Length} switches have something behind them.");
                return;
            }
            Debug.LogWarning($"[layers] {dead.Count} of {All.Length} switches have nothing behind "
                           + "them and are marked in the panel: " + string.Join(", ", dead) + ".");
        }

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

        /// <summary>
        /// THE STREET LAYOUT AND NOTHING ELSE: road and alley surfaces, the railroad, the county
        /// lot lines, and the names.
        ///
        /// Different question from GroundAndRoadsOnly, which is about the LAND - it keeps parking
        /// aprons and drops the lot lines. This one is for reading the PLAT: where the streets and
        /// alleys run, how they sit against the parcels, and what each one is called. It is the
        /// view somebody who grew up in the town can check against their own memory, which is a
        /// better instrument than anything in this repo and the only one that has caught every
        /// error so far.
        ///
        /// Names are included on purpose. "Alley 3 is wrong" is a bug report; "one of the alleys
        /// is wrong" is a search. Turn them off with one click if they get in the way.
        /// </summary>
        public static void StreetLayoutOnly()
        {
            SetAll(false);
            Set(Kind.Streets, true);
            Set(Kind.RailBed, true);
            Set(Kind.Plan, true);
            Set(Kind.Labels, true);
            // ALLEYS DELIBERATELY OFF. They are still laid wrong, and an alley across a lot makes
            // a correct street look wrong. Add them from the panel when they are worth looking at.
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
