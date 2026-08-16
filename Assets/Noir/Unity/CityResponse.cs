using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The two vehicles the county sends: a sheriff's car and an ambulance, driving in from the
    /// edge of the map to a scene and back out again.
    ///
    /// THEY ARE NOT PART OF THE TRAFFIC, AND THAT IS DELIBERATE. Every response vehicle drives
    /// the same <see cref="LaneGraph"/> the ambient fleet does, through the same public wrappers
    /// (<see cref="CityTraffic.PointOn"/>, <see cref="CityTraffic.TurnArc"/>,
    /// <see cref="CityTraffic.PointInTurn"/>) — but none of them ever enters
    /// <c>CityTraffic._movers</c>. Eight loops in that file walk <c>_movers</c> to decide who is
    /// following whom, who may pull out and who is about to be driven into, and every one of them
    /// assumes a car that WANDERS: it is recycled at the map edge, it picks its turns at random,
    /// and <see cref="CityTraffic.Retime"/> garages it when the hour says the town is quiet. A
    /// vehicle answering a call does none of those things. Putting one in that list would mean a
    /// guard in eight places and a ninth one missed. So it drives beside the fleet instead: it
    /// sees the ambient cars through <see cref="CityTraffic.AnyMoverWithin"/>, and they see it as
    /// an entry in <see cref="CityTraffic.Obstacles"/> — registered from the moment it is spawned
    /// to the moment it is destroyed, not merely while it stands at a scene. That registration is
    /// what stops cross traffic driving THROUGH an ambulance mid-junction: `_movers` is the only
    /// other thing `Blocked` consults, and a vehicle in neither list is a vehicle nobody can see.
    ///
    /// THEY RUN ON SIM TIME, NOT ON FRAMES — and since 2026-08-16 SO DOES EVERYTHING ELSE THAT
    /// MOVES. Everything here advances by the SIMULATION clock's own delta — `(Tick - lastTick)
    /// / GameClock.TicksPerSecond` — and the one-clock ruling (owner, 2026-08-16, spec
    /// docs/superpowers/specs/2026-08-16-one-clock-design.md) put the ambient fleet, the
    /// signals and the train on the same delta. The earlier ruling recorded here — "the ambient
    /// traffic is scenery and is entitled to run off Time.deltaTime... the two therefore
    /// diverge above 1x, by design" — is REVERSED: at the default 10x the town's walkers were
    /// doing an effective 30 mph past an 18 mph fleet, and the divergence read as a fault, not
    /// a trade. One consequence follows: the held deadline (<see cref="Patience"/>) counts SIM
    /// seconds now too, because everything that holds a rig clears on the sim clock.
    ///
    /// THEY DO NOT STOP AT RED LIGHTS. There is no signal test anywhere in this file, on purpose:
    /// what holds a response vehicle is what is physically in front of it, and nothing else. An
    /// official car crossing a junction against the light is what the town would see — and it is
    /// safe to draw only because the ambient fleet can see it coming, which is the obstacle
    /// registration above rather than any rule in here.
    ///
    /// AND ONE MAN ON FOOT. The county car carries an OFFICER, who gets out where it parks and
    /// walks the town's real walkable grid from door to door for the canvass — see
    /// <see cref="WalkCountyTo"/>. He is NOT a citizen: `Simulation`'s agent array is fixed at
    /// construction, so there is no slot to put him in and no plan for him to follow, and every
    /// witness, sighting and schedule in Core would have to learn about a person who exists for
    /// four sim hours a case. He is a Unity-side actor with a transform and a route, and the
    /// simulation never hears of him. What he DOES share with a citizen is the ground: the same
    /// <see cref="Pathfinder"/> over the same <see cref="TileGrid"/>, the same terrain-scaled
    /// pace, and the same trespass pricing — which already exempts the two endpoint lots, so a
    /// walk aimed at somebody's front door enters that yard lawfully with no plumbing at all.
    /// </summary>
    public sealed class CityResponse : MonoBehaviour
    {
        /// <summary>Which vehicle. The county car answers first; the ambulance comes for a
        /// body; the cruiser carries Rossville's own officer in from the precinct.</summary>
        public enum Rig { County, Ambulance, Cruiser }

        /// <summary>
        /// Metres per second. A shade over the ambient fleet's 8 — an official car moving with
        /// purpose — and under the player's 12, so a driver can still outrun the response.
        /// </summary>
        private const float ResponseSpeed = 10f;

        /// <summary>
        /// The gap kept beyond the two vehicles' own half-lengths, how far off the centre line
        /// another vehicle still counts as being in this lane, how long a stationary obstacle is
        /// assumed to be, and where the nose starts easing off for the stop.
        ///
        /// All four are <see cref="CityTraffic"/>'s own numbers (`Headway`, `LookWide`, the 2.2 m
        /// in `Blocked`'s obstacle arm, and `Braking`), restated here rather than made public
        /// there. They are copied because a response car has to judge a gap the way an ambient
        /// one does, and they are documented as copies so that changing one there is known to
        /// mean changing it here.
        /// </summary>
        private const float Headway = 3.5f, LookWide = 2.4f, ObstacleReach = 2.2f, Braking = 14f;

        /// <summary>Half a vehicle, when there is no mesh to measure. CityTraffic's own fallback.</summary>
        private const float DefaultReach = 2.2f;

        /// <summary>
        /// How much further than the following box counts as "close enough to the scene".
        ///
        /// THE AMBULANCE COULD NEVER REACH A SCENE THE COUNTY CAR WAS STANDING AT, and the two
        /// numbers that made it impossible never overlapped by about eight metres. Task 13 sends
        /// both vehicles to the SAME tile, so <see cref="CandidatesNear"/> hands both the same
        /// segment and the same `StopAtS`; the county car arrives first and parks half a lane off
        /// the centre line, which is well inside `LookWide` and therefore squarely inside the
        /// following box. The second vehicle then held at `Reach + 2.2 + Headway` — about 8.5 m
        /// short — while <see cref="Park"/> only fired within about 3.2 m of the stop. It waited
        /// there for ever: `onArrived` never fired, the case never advanced to `VehiclesLeave`,
        /// and the vehicle blocking it was never ordered away. A wedge with no error in the log.
        ///
        /// So an arrival is a BAND rather than a point: held by something on the final approach,
        /// within the box plus a metre of the scene, IS arriving. Two emergency vehicles queued
        /// nose to tail outside a house is what the street would actually look like.
        ///
        /// THIS IS THE GOOD-LOOKING CASE AND NOT THE SAFETY NET. It only fires near the scene, on
        /// the final leg. What guarantees that no vehicle ever waits for ever, wherever it is, is
        /// <see cref="Patience"/>.
        /// </summary>
        private const float ArrivalMargin = 1f;

        /// <summary>
        /// How long a vehicle will sit behind something, ANYWHERE at all, before it stops waiting
        /// to be somewhere else. SIM seconds since the 2026-08-16 one-clock ruling — it counted
        /// REAL seconds while the ambient fleet and the signal cycle ran on the wall clock — and
        /// <see cref="HeldTooLong"/> is what happens then.
        ///
        /// THE BAND AND THIS ARE NOT TWO PATCHES ON ONE WEDGE:
        ///
        ///   <see cref="ArrivalMargin"/> is the CLEAN STOP AT THE SCENE. The vehicle is already as
        ///   near as whatever is in front will let it get, so it parks there and looks right. It
        ///   is what makes the ordinary case — both rigs sent to the same tile, one of them
        ///   already standing on it — an arrival rather than a queue.
        ///
        ///   THIS is the LIVENESS GUARANTEE, and it holds EVERYWHERE: any segment, mid-turn
        ///   included, however far from the scene. The wedge it closes is not a geometry, which is
        ///   why three attempts to fix it as one each only moved the failure somewhere the last
        ///   could not see. A parked response vehicle blocks a lane; everything behind that block
        ///   is stopped until the case closes; the case cannot close until the vehicle it is
        ///   blocking arrives. Wherever in that chain the second vehicle is standing, waiting is
        ///   not a strategy — so waiting is given a limit.
        ///
        /// WHY SIM SECONDS NOW, WALL SECONDS BEFORE: the deadline must outlast everything that
        /// can legitimately hold a rig, measured on the clock those things clear by. When the
        /// fleet drove on Time.deltaTime and the lights cycled in 36 REAL seconds, that clock
        /// was the wall clock, and a sim-seconds deadline shrank with the speed dial — at 10x a
        /// rig behind an ordinary red gave up before the light could change. The one-clock
        /// ruling (2026-08-16) moved the fleet and the cycle onto the sim clock, which inverts
        /// the argument exactly: queues drain and lights change in SIM seconds now, so sixty
        /// SIM seconds outlasts the whole 36 s cycle with margin AT EVERY DIAL — the property
        /// the wall-clock version could only promise at 1x.
        ///
        /// A genuinely-permanent block therefore delays a case by the same sixty sim seconds
        /// whatever the dial, which is also new: it used to cost ten sim minutes at 10x. The
        /// case machine runs on sim minutes and survives either way. A driver who finally parks
        /// gets out and walks, which is exactly what this models: the county officer covers the
        /// difference on foot.
        /// </summary>
        private const float Patience = 60f;

        /// <summary>
        /// How long a slice of driving is, in SIM seconds, and how many a frame may run.
        ///
        /// The same argument as <see cref="CityTraffic"/>'s `Slice`/`MostSlices` with the frame
        /// rate swapped for the sim rate: at 600x fast-forward a single frame can carry minutes
        /// of sim time, and a car that advanced all of it in one step would jump whole segments,
        /// skip its stop and drive through the junction geometry rather than round it. Fifteen
        /// sim seconds a frame is enough to keep a fast-forwarded response looking driven; past
        /// that it falls behind rather than teleports.
        /// </summary>
        private const float Step = 0.25f;
        private const int MostSteps = 60;

        /// <summary>
        /// How far off the scene a lane may be and still count as "the road it happened on".
        ///
        /// FIFTEEN METRES BECAUSE A ROAD HAS TWO SIDES AND A VERGE. The scene is a tile the
        /// player's car struck somebody on, which may be the far carriageway, the pavement, or a
        /// yard just off the kerb — so the nearest lane by centre-line distance is frequently not
        /// a lane anything can legally route to. See <see cref="PlanRoute"/> for why this is a
        /// LIST rather than a winner.
        /// </summary>
        private const float SceneReach = 15f;

        /// <summary>
        /// How far from the extreme an entry still counts as being at that edge, and the fewest
        /// entries the fallback will consider whatever the distances say.
        ///
        /// Only the FALLBACK path reads these: the first entry tried is always the southmost or
        /// northmost outright, which is Route 1 at both edges of this map.
        /// </summary>
        private const float EdgeBand = 150f;
        private const int LeastEdgeEntries = 4;

        /// <summary>
        /// The most route plans one <see cref="DriveIn"/> may run before giving up and arriving
        /// off-stage. `LaneRoutes.Plan` is a Dijkstra over every lane segment in the town, so a
        /// full entries-by-candidates sweep of a graph this size is a visible hitch. Twenty-four
        /// is several entries' worth of candidates and still a fraction of a frame.
        /// </summary>
        private const int MostPlans = 24;

        /// <summary>
        /// How long an off-stage arrival takes, in SIM seconds.
        ///
        /// THE CASE MUST NEVER WEDGE ON ROUTING. The lane graph offers no U-turns and no
        /// lane-change turns, so `LaneRoutes.Plan` returning false is a LEGAL answer about two
        /// real segments rather than a bug — a scene on a one-way stub reachable only by turning
        /// round has no route from anywhere. When every candidate at every edge entry refuses,
        /// the vehicle is reported as having arrived after two sim minutes with no drive drawn,
        /// and the response carries on. Silence is not an option here and neither is a hang; see
        /// the log line in <see cref="DriveIn"/>.
        /// </summary>
        private const float OffStageSeconds = 120f;

        /// <summary>
        /// The officer's pace on ground that costs 1.0, in metres per SIM second.
        ///
        /// `Citizen.BaseSpeedIn`'s adult baseline, restated because he is not a citizen and has
        /// nobody to ask. The terrain division is the simulation's too (`Simulation.cs:1283-1294`)
        /// — a road is quick and a ploughed field is not — including its clamp: an unwalkable tile
        /// answers `float.MaxValue`, which would divide his speed to nothing, so a cost over 100
        /// is read as open ground exactly as the sim reads it.
        /// </summary>
        private const float WalkSpeed = 1.35f;

        /// <summary>
        /// How long he stands before reporting an arrival he did not actually make, in SIM
        /// seconds — two sim minutes, and it is the same two minutes as
        /// <see cref="OffStageSeconds"/> for the same reason. A door the walkable grid cannot
        /// reach must not wedge a canvass any more than an unroutable street may wedge a drive:
        /// he stands where he is for as long as the walk would plausibly have taken, and then the
        /// case machine's dwell runs from the kerb.
        /// </summary>
        private const float KerbStand = 120f;

        /// <summary>
        /// How long a walk may keep coming back <see cref="PathOutcome.GaveUp"/> before he gives
        /// up on it too, in SIM seconds.
        ///
        /// `GaveUp` is a property of THIS MOMENT rather than of the map — the search ran out of
        /// node budget — so the honest answer is to ask again shortly, and this file's own
        /// <see cref="Update"/> is a perfectly good "shortly". But asking for ever is a wedge with
        /// no error in the log, which is the exact shape of failure the whole of
        /// <see cref="Patience"/> exists to stop, so the retrying is bounded and ends in the same
        /// kerb fallback a permanently-unreachable door gets.
        /// </summary>
        private const float SearchPatience = 120f;

        /// <summary>
        /// How long he waits between asking again while a search keeps giving up, in SIM seconds.
        ///
        /// A `GaveUp` COSTS THE WHOLE ALLOWANCE BEFORE IT CAN SAY SO, which is the mechanism
        /// behind CLAUDE.md's stale-node-cap incident: a capped search spends its entire budget
        /// before it can report failure, so seven honest walks became 171 refusals and seventeen
        /// million nodes were burnt to avoid paying about one million. THAT MEASUREMENT WAS TAKEN
        /// AGAINST A 100,000-NODE CEILING — 171 x 100k — and the ceiling is 300,000 now, so the
        /// same shape of mistake costs three times as much today as it did when it was written up.
        ///
        /// Retrying every frame for the whole of <see cref="SearchPatience"/> would be that shape:
        /// at 60 fps and 1x it is about 7,200 full-budget searches PER DOOR, and about 720 per door
        /// at the default 10x — a canvass is many doors. Five sim seconds is the same answer for a
        /// sixtieth of the cost: `GaveUp` is a property of how busy this MOMENT is, and a moment
        /// does not change meaningfully between two frames.
        /// </summary>
        private const float LookAgainEvery = 5f;

        /// <summary>
        /// Degrees a second his facing may swing, on the REAL clock.
        ///
        /// <see cref="AgentMeshView"/>'s own `TurnRate` and its own `Time.unscaledDeltaTime`,
        /// deliberately identical rather than merely similar: he walks among people whose yaw is
        /// smoothed at exactly this rate, and a figure that snaps 45 degrees at a corner while the
        /// townsman beside him leans into it does not read as a different animation, it reads as a
        /// glitch.
        /// </summary>
        private const float TurnRate = 540f;

        /// <summary>
        /// How tall he is made, in metres.
        ///
        /// The adult baseline `AgentLook` scales the whole town to, and the height
        /// <see cref="AgentAnimation"/>'s clips were authored for — `AgentAnimation.AdultStride`
        /// is this number times 0.82. AT EXACTLY THIS HEIGHT THE STRIDE TERM IS 1, which is what
        /// makes passing `stride: 0f` to `Drive` honest rather than merely absent: he is the body
        /// the walk cycle was animated for, so there is nothing to correct.
        ///
        /// The pack's own adult males are 1.86 to 2.03 m, so without this he stands a head over
        /// everybody he interviews.
        /// </summary>
        private const float AdultHeight = 1.71f;

        /// <summary>
        /// Which clip he draws out of a row that offers several. A FIXED number, so he behaves the
        /// same way in every case and in every replay of a seed.
        ///
        /// IT ONLY EVER PICKS A STANDING GESTURE. The `moving` row holds a single clip, so his
        /// walk is not a choice at all; what this decides is which of the fifteen `talking` clips
        /// he does at a door — whether he shrugs, gestures or nods along. See <see cref="Animate"/>.
        ///
        /// NOT A VILLAGE SEED AND NOT A DATE — it feeds `AgentAnimation`'s clip choice and nothing
        /// else, and no seed reproduces a different town because of it.
        /// </summary>
        private const ulong OfficerSeed = 0x0FF1CEuL;

        /// <summary>
        /// The most tiles one frame may carry the officer over.
        ///
        /// The same argument as <see cref="MostSteps"/>: at 600x fast-forward one frame is minutes
        /// of sim time, and a walker who consumed all of it at once would cross the town in a
        /// single step. Past this he falls behind rather than teleports, and the next frame picks
        /// the walk up where it was. Generous — a whole Rossville block is about thirty tiles —
        /// because the cap is a guard against a hitch, not a speed limit.
        /// </summary>
        private const int MostTiles = 256;

        /// <summary>
        /// How far from where he is put down a walkable tile is looked for, in tiles.
        ///
        /// `Pathfinder.FindPath` answers `NoRouteExists` for an unwalkable ORIGIN and not just an
        /// unwalkable destination, so a bad spawn point fails EVERY door of a canvass rather than
        /// one — and it fails them with a log line that names the doors.
        ///
        /// NOT FOR THE ORDINARY PARKED CAR, whatever an earlier version of this said. A cruiser
        /// that drove parks at the asphalt edge and <see cref="OutOfTheCar"/> steps him onto the
        /// pavement beside it; both are walkable, the first test passes, and this search never
        /// runs. It earns its keep on the vehicle that did NOT drive to a spot chosen for standing
        /// on — see <see cref="NearestFoothold"/>, which holds the full argument.
        /// </summary>
        private const int Foothold = 4;

        /// <summary>
        /// The figure the officer is drawn as: the first name in <see cref="AgentBody"/>'s Men
        /// cast, by direct path.
        ///
        /// NOT THROUGH `AgentBody.Build`, which wants a `Citizen` to decide who somebody looks
        /// like and to dress their mesh from their key — and the officer deliberately has no
        /// citizen. One named prefab is the whole of what this needs, and naming it here means a
        /// pack reshuffle that moves the cast breaks one visible thing rather than silently
        /// drawing nobody. If this path stops resolving, <see cref="Create"/>'s count says so.
        /// </summary>
        private const string OfficerPrefab =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/People/Slavic People/"
            + "Man_Slavic_Summer_Hair.prefab";

        /// <summary>
        /// What he plays: the town's own animator, the one `Noir > Build The Townsfolk Animator`
        /// rebuilds. <see cref="AgentBody"/>'s `Controller`, restated because it is private there.
        ///
        /// THE AVATAR IS NOT FETCHED HERE, and it is the one thing that would fail in silence.
        /// `AgentBody` has to go and find one because not every prefab in the pack carries an
        /// Animator of its own, and one added by hand has nothing on it — seventy-four of 365
        /// figures arrived in their bind pose that way. This prefab is not one of them: it ships
        /// its own Animator with its avatar already bound, so the controller is the whole of what
        /// is missing. If that ever stops being true he stands in his bind pose and slides, which
        /// is precisely what that comment in `AgentBody` warns of.
        /// </summary>
        private const string TownsfolkController = "Assets/Noir/Animations/Townsfolk.controller";

        /// <summary>What the officer is doing about the door he was last sent to.</summary>
        private enum Foot
        {
            /// <summary>Not spawned, or spawned and standing about with no order.</summary>
            Idle,

            /// <summary>Walking a planned route.</summary>
            Walking,

            /// <summary>The search gave up; ask the grid again next pass.</summary>
            Looking,

            /// <summary>No route at all: standing out <see cref="KerbStand"/> before reporting.</summary>
            Standing,
        }

        /// <summary>
        /// One vehicle's whole state, mirroring <see cref="CityTraffic"/>'s `Mover` — and a CLASS
        /// for the same reason that one is: it is mutated in place from several methods, and a
        /// struct in a field array would hand each of them a copy to change and throw away.
        /// </summary>
        private sealed class Car
        {
            public Transform What;

            /// <summary>LaneTurn indices in driving order, from `LaneRoutes.Plan`.</summary>
            public readonly List<int> Turns = new List<int>();

            /// <summary>How many of them are behind it.</summary>
            public int Leg;

            public int Segment;
            public float S;

            /// <summary>0..1 through the turn at <c>Turns[Leg]</c>, while <see cref="InTurn"/>.</summary>
            public float T;
            public bool InTurn;

            /// <summary>Travel coordinate on the LAST segment at which it stops. See Park.</summary>
            public float StopAtS;

            public bool Arrived;

            /// <summary>Driving OUT rather than in: it despawns at the end of the last segment.</summary>
            public bool Leaving;

            /// <summary>Half this vehicle's measured length, nose to tail.</summary>
            public float Reach = DefaultReach;

            public Vector3 Forward = Vector3.forward;

            /// <summary>Sim seconds left of an un-routable arrival; negative when it is driving.</summary>
            public float OffStage = -1f;

            /// <summary>
            /// REAL seconds this vehicle has been held without covering ground — charged from
            /// `Time.unscaledDeltaTime`, at most once per frame, and only on frames the sim
            /// moved, so a paused sim charges nothing — zeroed the moment it moves a real step.
            /// The accumulator the <see cref="Patience"/> threshold reads, on the same REAL
            /// clock as that deadline; see Patience for why the wait is on the wall clock when
            /// the driving is not.
            /// </summary>
            public float HeldFor;

            /// <summary>Fired once, at the moment it is parked. Nulled as it fires.</summary>
            public System.Action OnArrived;

            /// <summary>The scene it was sent to, in world space — where it stands if it never drove.</summary>
            public Vector3 SceneAt;

            /// <summary>Where it ended up. Task 12's officer gets out of the car here.</summary>
            public Vector3 ParkedAt;
        }

        private readonly Car[] _cars = new Car[3];   // sized to the Rig enum

        private WorldModel _world;
        private CityTraffic _traffic;
        private VillageHost _host;

        /// <summary>The sim tick the last slice of driving was measured from. -1 before the first.</summary>
        private long _lastTick = -1;

        // Scratch, reused rather than allocated per call — these run on a hit, not per frame, but
        // the lists are small and permanent and there is no reason to churn them.
        private readonly List<(int Seg, float S, float Lateral)> _candidates =
            new List<(int Seg, float S, float Lateral)>();
        private readonly List<int> _edge = new List<int>();

        // ---- the officer's state ----
        //
        // ONE Pathfinder FOR THE COMPONENT'S LIFETIME, not one per walk. Its scratch arrays are
        // the entire point of the class: a `new Pathfinder(grid)` allocates four arrays the size
        // of the map — 5,040,000 tiles on Rossville — and building one per door would allocate
        // eighty megabytes a canvass to answer a question the same instance answers for nothing.
        // Built on first use rather than in Awake, because `Create` assigns `_world` AFTER
        // `AddComponent` has already run Awake.
        private Pathfinder _finder;

        private Transform _officer;

        /// <summary>His legs, or null when the prefab did not load and he is an empty GameObject.
        /// Every failure mode below it is a no-op or a held pose; none of them throws.</summary>
        private Animator _officerAnim;

        /// <summary>
        /// How fast he is walking, in metres per SIM second — the last step's own terrain-scaled
        /// speed. Read by <see cref="Animate"/>, which converts it to a REAL-time pace.
        ///
        /// IT CAN BE STALE, AND THAT IS HARMLESS ONLY BECAUSE OF WHAT READS IT. The tidy exits
        /// zero it — <see cref="ReachedTheDoor"/>, <see cref="SendHimHome"/>, spawning — but the
        /// interrupt paths do not: a walk replaced mid-stride by a second `WalkCountyTo`, or one
        /// that falls through to <see cref="StandAtTheKerb"/>, leaves the last step's figure
        /// sitting here. Nothing reads it in those states: `Drive` is handed this pace against the
        /// `talking` row, and that row declares no pace of its own, so `Drive` ignores the number
        /// entirely and plays the gesture at 1x. If a standing row is ever given a pace, this
        /// field has to be zeroed on those paths too.
        /// </summary>
        private float _officerPace;

        /// <summary>Sim seconds until the next attempt while <see cref="Foot.Looking"/>.
        /// Owned by <see cref="PlanTheWalk"/>'s give-up arm, which is the only thing that arms
        /// it. See <see cref="LookAgainEvery"/>.</summary>
        private float _lookAgain;

        /// <summary>Where he is, in village coordinates — the sim's own space, so the tile he is
        /// on and the cost of the ground under him are one lookup rather than a conversion.</summary>
        private Vec2 _officerAt;

        /// <summary>Which way he is pointing, in world space. Kept so a step of zero length —
        /// standing still, or snapping onto a tile he was already on — cannot spin him.</summary>
        private Vector3 _officerFacing = Vector3.forward;

        /// <summary>The route he is walking. Reused, never reallocated: `FindPath` clears it.</summary>
        private readonly List<Tile> _walk = new List<Tile>();

        /// <summary>How far along <see cref="_walk"/> he is — the index of the tile he is walking
        /// TOWARDS, so `_walked == _walk.Count` means he is there.</summary>
        private int _walked;

        private Foot _foot = Foot.Idle;
        private Tile _door;
        private System.Action _atDoor;

        /// <summary>Sim seconds left of the kerb stand, or spent so far on giving-up searches.
        /// Only one of the two states that read it can be current at a time.</summary>
        private float _standing, _searching;

        private const string CarsCity =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City/";

        /// <summary>
        /// The pack's one cruiser and its one ambulance — the same two paths
        /// <see cref="CityBuildings"/> parks outside the precinct and the hospital
        /// (`CityBuildings.cs:540-541`). A bespoke county livery is not in the pack and is not
        /// worth chasing for a car seen from across a street.
        /// </summary>
        private static string PrefabOf(Rig rig) => rig == Rig.Ambulance
            ? CarsCity + "Car_Ambulance_Modern.prefab"
            : CarsCity + "Car_Police_Modern.prefab";   // county AND cruiser: the pack's one police car

        private static string NameOf(Rig rig) =>
            rig == Rig.County ? "county car" : rig == Rig.Ambulance ? "ambulance" : "cruiser";

        public static CityResponse Create(WorldModel world, Transform parent, CityTraffic traffic)
        {
            var go = new GameObject("CityResponse");
            go.transform.SetParent(parent, false);
            var response = go.AddComponent<CityResponse>();
            response._world = world;
            response._traffic = traffic;
            response._host = VillageHost.Instance;

            // AUDIBLE, NEVER SILENT. The prefabs are reached through AssetDatabase, which exists
            // only in the editor, so a shipped player drives two vehicles nobody can see. That is
            // a deliberate degradation — the response still happens, the case still closes — and
            // it says so once at build time rather than leaving somebody to wonder why no car ever
            // turned up. Same shape as the `[people] all 1400 of Rossville is PRIMITIVE CAPSULES`
            // line and for the same reason.
            int drawn = 0, invisible = 0;
            foreach (Rig rig in System.Enum.GetValues(typeof(Rig)))
            {
#if UNITY_EDITOR
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOf(rig)) != null)
                { drawn++; continue; }
#endif
                invisible++;
            }

            // THE OFFICER IS THE THIRD ACTOR, and he degrades exactly as the vehicles do:
            // invisible but real, spawning at the same spot, walking the same route and reporting
            // the same arrivals. He is counted in this line rather than given one of his own so
            // that a session reading the log gets one number for "how much of the response can be
            // seen" instead of two that have to be added up.
            bool officerDrawn = false;
#if UNITY_EDITOR
            officerDrawn = AssetDatabase.LoadAssetAtPath<GameObject>(OfficerPrefab) != null;
#endif
            if (officerDrawn) drawn++; else invisible++;

            Debug.Log($"[response] {drawn} actors drawn, {invisible} invisible (editor-only prefabs)");

            return response;
        }

        // ---- orders ---------------------------------------------------------------------

        /// <summary>
        /// Send a vehicle in from the named map edge to the scene. It spawns off-stage at the
        /// southmost (or northmost) lane entry — Route 1 owns both edges of this map — drives the
        /// lane graph, and fires <paramref name="onArrived"/> exactly once as it stops beside the
        /// scene.
        /// </summary>
        public void DriveIn(Rig rig, bool edgeSouth, Tile scene, System.Action onArrived)
        {
            int slot = (int)rig;
            if (slot < 0 || slot >= _cars.Length) return;

            // ALREADY OUT. The case machine emits each order once per case, but cases queue and a
            // second one can want the same vehicle while the first still has it. The newest order
            // wins: the standing vehicle is taken away rather than left parked at a scene that is
            // no longer being worked.
            if (_cars[slot] != null)
            {
                Debug.LogWarning($"[response] the {NameOf(rig)} was still out; recalling it");
                Despawn(slot);
            }

            var car = new Car
            {
                SceneAt = Space3D.ToWorld(scene),
                OnArrived = onArrived,
            };
            _cars[slot] = car;

            if (_traffic == null || _traffic.Graph == null || !PlanRoute(car, edgeSouth, scene))
            {
                Debug.Log($"[response] no route from the {(edgeSouth ? "south" : "north")} edge "
                        + $"to tile ({scene.X},{scene.Y}) — arriving off-stage");
                car.OffStage = OffStageSeconds;
                return;
            }

            Dress(car, rig);
            Seat(car);
        }

        /// <summary>
        /// Send it home: from where it stands to an exit at the same edge it came in by, and off
        /// the map. The GameObject goes when it reaches the end of the last lane, and it stays an
        /// entry in <see cref="CityTraffic.Obstacles"/> until then — a vehicle pulling out into
        /// the carriageway is not less of a hazard than one standing at the kerb, and the
        /// registration is dropped in one place only, <see cref="Despawn"/>.
        /// </summary>
        public void Depart(Rig rig, bool edgeSouth)
        {
            int slot = (int)rig;
            if (slot < 0 || slot >= _cars.Length) return;

            // HE DOES NOT WALK BACK TO THE CAR — v1 DESTROYS HIM WHERE HE STANDS, mid-stride if
            // that is where the order finds him. A walk back is a second journey to plan and a
            // departure that cannot begin until it finishes, which is a wedge shape this file
            // already knows too well; the honest v1 is that the officer stops existing as the car
            // pulls away, and from across a street nobody was watching him get in anyway.
            //
            // ORDERED HERE AS WELL AS IN DESPAWN because this is the path on which the county car
            // DOES NOT despawn — it plans a route out and drives it — and an officer left behind
            // would stand in the street for the rest of the game with nothing that could ever pick
            // him up again.
            if (rig == Rig.County) SendHimHome();

            var car = _cars[slot];
            if (car == null) return;

            // It never took the stage, so there is nothing to drive away.
            if (car.What == null || _traffic == null || _traffic.Graph == null)
            { Despawn(slot); return; }

            if (!PlanOut(car, edgeSouth))
            {
                Debug.Log($"[response] no route off the {(edgeSouth ? "south" : "north")} edge "
                        + $"for the {NameOf(rig)} — leaving off-stage");
                Despawn(slot);
                return;
            }

            car.Arrived = false;
            car.Leaving = true;
            car.OnArrived = null;
        }

        /// <summary>True when the cruiser slot is free — the host only sends the officer by car
        /// when there is a car to send.</summary>
        public bool CruiserAvailable => _cars[(int)Rig.Cruiser] == null;

        /// <summary>
        /// The cruiser, from the precinct to the scene. Same machinery as <see cref="DriveIn"/>
        /// with one difference: the route starts at the lane nearest <paramref name="fromNear"/>
        /// (the precinct's own kerb) rather than at a map edge. <paramref name="onArrived"/> gets
        /// the car's actual stop position — the host turns that into the kerb tile the officer
        /// alights at — and fires exactly once, off-stage fallback included, so the officer can
        /// never be sealed inside a cruiser that failed to route.
        /// </summary>
        public void CruiserOut(Vec2 fromNear, Tile scene, System.Action<Vector3> onArrived)
        {
            int slot = (int)Rig.Cruiser;
            if (_cars[slot] != null)
            {
                Debug.LogWarning("[response] the cruiser was still out; recalling it");
                Despawn(slot);
            }

            var car = new Car { SceneAt = Space3D.ToWorld(scene) };
            car.OnArrived = () => onArrived(car.What != null ? car.What.position : car.SceneAt);
            _cars[slot] = car;

            if (_traffic == null || _traffic.Graph == null || !PlanRouteFrom(car, fromNear, scene))
            {
                Debug.Log($"[response] no route from the precinct to tile ({scene.X},{scene.Y})"
                        + " — the cruiser arrives off-stage");
                car.OffStage = OffStageSeconds;
                return;
            }

            Dress(car, Rig.Cruiser);
            Seat(car);
        }

        /// <summary>
        /// Back to the station: from where it stands to the lane nearest the precinct, and gone.
        /// The Leaving arm despawns it at the end of its last lane — a block past the station at
        /// worst, which from across a street is a cruiser driving home. The set-dressing police
        /// car parked outside the precinct never moved, so there is nothing to re-park.
        /// </summary>
        public void CruiserHome(Vec2 toNear)
        {
            int slot = (int)Rig.Cruiser;
            var car = _cars[slot];
            if (car == null) return;

            if (car.What == null || _traffic == null || _traffic.Graph == null)
            { Despawn(slot); return; }

            CandidatesNear(toNear);
            bool planned = false;
            for (int c = 0; c < _candidates.Count && c < MostPlans && !planned; c++)
                planned = LaneRoutes.Plan(_traffic.Graph, car.Segment, _candidates[c].Seg, car.Turns);
            if (!planned) { Despawn(slot); return; }

            // The old plan's turn is not this plan's turn — PlanOut's own rule.
            car.Leg = 0;
            car.InTurn = false;
            car.T = 0f;
            car.HeldFor = 0f;
            car.Arrived = false;
            car.Leaving = true;
            car.OnArrived = null;
        }

        /// <summary>Is that vehicle standing at the scene? False before it gets there and false
        /// again the moment it is ordered away.</summary>
        public bool Arrived(Rig rig)
        {
            int slot = (int)rig;
            return slot >= 0 && slot < _cars.Length && _cars[slot] != null && _cars[slot].Arrived;
        }

        /// <summary>Where that vehicle is standing, or null when it is not parked anywhere.</summary>
        public Vector3? ParkedAt(Rig rig)
        {
            int slot = (int)rig;
            if (slot < 0 || slot >= _cars.Length) return null;
            var car = _cars[slot];
            return car != null && car.Arrived ? car.ParkedAt : (Vector3?)null;
        }

        // ---- routing --------------------------------------------------------------------

        /// <summary>
        /// Find an entry at the named edge and a lane beside the scene that are joined by legal
        /// turns, and seat the car on it.
        ///
        /// WHY THIS IS A SEARCH RATHER THAN TWO LOOKUPS. `LaneRoutes.NearestSegment` answers with
        /// the single nearest lane by centre-line distance and breaks ties by lowest index with no
        /// regard for which way that lane travels — so for a scene on a two-way street it returns
        /// one of the two directions arbitrarily, and the lane graph offers no U-turn to fix the
        /// choice. Worse, the graph has no lane-change turns either, so "no route" is a perfectly
        /// legal answer about two real segments. Asking once and believing the answer would strand
        /// the response on about half the scenes in the town.
        ///
        /// So: every lane within <see cref="SceneReach"/> of the scene, both directions, nearest
        /// first; every entry at that edge, the outright southmost or northmost first; the first
        /// pair that routes wins. If none does, the caller reports an off-stage arrival.
        /// </summary>
        private bool PlanRoute(Car car, bool edgeSouth, Tile scene)
        {
            var graph = _traffic.Graph;

            CandidatesNear(Vec2.CentreOf(scene));
            if (_candidates.Count == 0) return false;

            EdgeEntries(edgeSouth, entry: true);

            int plans = 0;
            for (int e = 0; e < _edge.Count; e++)
            {
                int from = _edge[e];
                float fromS = graph.Segments[from].FromS;

                for (int c = 0; c < _candidates.Count && plans < MostPlans; c++)
                {
                    var (seg, s, _) = _candidates[c];

                    // A STOP BEHIND THE SPAWN IS NOT A DRIVE. Only reachable when the scene sits
                    // on the entry lane itself, which on a village edge is a real case: the car
                    // would be parked before it had moved.
                    if (seg == from && s <= fromS + car.Reach + 1f) continue;

                    plans++;
                    if (!LaneRoutes.Plan(graph, from, seg, car.Turns)) continue;

                    car.Segment = from;
                    car.S = fromS;
                    car.StopAtS = s;
                    car.Leg = 0;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// <see cref="PlanRoute"/> with the map edge swapped for a point in town: the route
        /// starts on a lane beside <paramref name="fromNear"/> (the precinct) instead of at an
        /// entry. Same both-directions, nearest-first search on BOTH ends, for PlanRoute's own
        /// reason — NearestSegment's single answer picks a direction arbitrarily and the graph
        /// has no U-turns, so asking once would strand the cruiser on half the town's scenes.
        /// </summary>
        private bool PlanRouteFrom(Car car, Vec2 fromNear, Tile scene)
        {
            var graph = _traffic.Graph;

            CandidatesNear(Vec2.CentreOf(scene));
            if (_candidates.Count == 0) return false;
            var dests = _candidates.ToArray();

            CandidatesNear(fromNear);
            if (_candidates.Count == 0) return false;

            int plans = 0;
            for (int f = 0; f < _candidates.Count; f++)
            {
                var (from, fromS, _) = _candidates[f];

                for (int c = 0; c < dests.Length && plans < MostPlans; c++)
                {
                    var (seg, s, _) = dests[c];

                    // A stop behind the start is not a drive — PlanRoute's own guard, one
                    // street over: precinct and scene on the same lane, scene behind the car.
                    if (seg == from && s <= fromS + car.Reach + 1f) continue;

                    plans++;
                    if (!LaneRoutes.Plan(graph, from, seg, car.Turns)) continue;

                    car.Segment = from;
                    car.S = fromS;
                    car.StopAtS = s;
                    car.Leg = 0;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The reverse: from where it stands to a lane that runs off the same edge.</summary>
        private bool PlanOut(Car car, bool edgeSouth)
        {
            var graph = _traffic.Graph;
            EdgeEntries(edgeSouth, entry: false);

            for (int e = 0; e < _edge.Count && e < MostPlans; e++)
            {
                if (!LaneRoutes.Plan(graph, car.Segment, _edge[e], car.Turns)) continue;

                // THE OLD PLAN'S TURN IS NOT THIS PLAN'S TURN. `Depart` can be ordered at any
                // moment, including one where the vehicle is part-way round a junction — an
                // ambulance recalled mid-turn, or a second case taking the county car. Leaving
                // `InTurn`/`T` alone applies however far it had got through the abandoned turn to
                // the FIRST turn of the new route, which is a different arc entirely: the vehicle
                // would appear part-way through a corner it had never entered.
                car.Leg = 0;
                car.InTurn = false;
                car.T = 0f;
                car.HeldFor = 0f;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Every lane segment whose road passes within <see cref="SceneReach"/> of this point,
        /// with the travel coordinate of the nearest spot on it, nearest first.
        ///
        /// The projection is `LaneRoutes.NearestSegment`'s own, arc → point → axis coordinate →
        /// `TravelOf`, kept identical because FromS/ToS are travel-signed AXIS coordinates while
        /// `RoadPath.Project` returns ARC length, and the two disagree on every curve.
        /// </summary>
        private void CandidatesNear(Vec2 point)
        {
            _candidates.Clear();

            var graph = _traffic.Graph;
            var roads = _world.Roads;

            for (int i = 0; i < graph.Segments.Count; i++)
            {
                var seg = graph.Segments[i];
                var line = roads.Lines[seg.Line];
                if (line.Path == null) continue;

                var (arc, lateral) = line.Path.Project(point);
                var on = line.Path.PointAt(arc);
                float axis = line.IsNorthSouth ? on.Y : on.X;
                float travel = LaneGraph.TravelOf(seg.Way, axis);
                if (travel < seg.FromS || travel > seg.ToS) continue;

                float d = lateral < 0f ? -lateral : lateral;
                if (d > SceneReach) continue;

                _candidates.Add((i, travel, d));
            }

            _candidates.Sort((a, b) => a.Lateral.CompareTo(b.Lateral));
        }

        /// <summary>
        /// The lanes at one edge of the map, the extreme one first: entries to come in on, or
        /// exits to leave by.
        ///
        /// Village y increases SOUTH and world z is its negation, so the southmost lane is the one
        /// with the greatest village y. The extreme is what the brief calls for and what the
        /// county would use — Route 1 owns both edges of this map — and the rest of the band is
        /// only ever reached when the extreme one cannot be routed to.
        /// </summary>
        private void EdgeEntries(bool south, bool entry)
        {
            var graph = _traffic.Graph;

            _edge.Clear();
            if (entry) { for (int i = 0; i < graph.Entries.Count; i++) _edge.Add(graph.Entries[i]); }
            else
            {
                for (int i = 0; i < graph.Segments.Count; i++)
                    if (graph.Segments[i].IsExit) _edge.Add(i);
            }
            if (_edge.Count == 0) return;

            _edge.Sort((a, b) =>
            {
                float ya = Southness(a, entry), yb = Southness(b, entry);
                return south ? yb.CompareTo(ya) : ya.CompareTo(yb);
            });

            float best = Southness(_edge[0], entry);
            for (int i = _edge.Count - 1; i >= LeastEdgeEntries; i--)
            {
                float d = Southness(_edge[i], entry) - best;
                if (d < 0f) d = -d;
                if (d > EdgeBand) _edge.RemoveAt(i);
            }
        }

        /// <summary>Village y of a lane's outer end — its start for an entry, its finish for an
        /// exit. World -z, as everywhere.</summary>
        private float Southness(int segment, bool entry)
        {
            var seg = _traffic.Graph.Segments[segment];
            return -_traffic.PointOn(segment, entry ? seg.FromS : seg.ToS).z;
        }

        // ---- driving --------------------------------------------------------------------

        private void Update()
        {
            // Unity's own ==, not `??=`, so a host destroyed and rebuilt between scenes is picked
            // up again rather than left as a "fake null" the C# operators walk past.
            if (_host == null) _host = VillageHost.Instance;
            if (_host == null || _host.Sim == null) return;

            // HIS LEGS ARE DRIVEN ABOVE EVERY RETURN BELOW, AND THAT IS THE POINT. A paused sim
            // takes the `dtSim <= 0` return, so an Animate called from inside the walk never ran
            // on a paused frame — and the animator is on UnscaledTime, so it carried on playing
            // whatever speed it was last given while all 1,385 citizens stood frozen around him.
            // One man treadmilling in a stopped town. Nothing here needs a delta any more (see
            // Animate), so it is simply hoisted: paused means SpeedIndex 0, which means a pace of
            // zero, which means Drive holds his pose exactly as it holds everybody else's.
            Animate();

            long now = _host.Sim.Clock.Tick;
            if (_lastTick < 0) { _lastTick = now; return; }

            float dtSim = (now - _lastTick) / (float)GameClock.TicksPerSecond;
            _lastTick = now;
            if (dtSim <= 0f) return;                      // paused, or the same tick twice

            // The man on foot, on the same clock and the WHOLE of this frame's slice — he is not
            // in the slicing loop below because nothing he does depends on where the vehicles are.
            // He has no junction to cross, nothing to queue behind and no stop to ease into; he
            // walks a list of tiles, and his own MostTiles guard is what keeps a fast-forwarded
            // frame from carrying him across the town in one go. Ordered BEFORE the loop, which
            // consumes `dtSim`.
            WalkTheOfficer(dtSim);

            // The held deadline charges TOWN time now, like everything else — see Patience for
            // the 2026-08-16 reversal. Captured here while dtSim still holds the whole frame's
            // delta, and handed to the first slice only, because charging each slice would
            // multiply one frame's wait by the slice count: the ambient fleet and the player's
            // car do not move between slices; the sibling rig does, but a slice in which
            // anything clears is a slice in which this rig covers ground, which zeroes the
            // charge anyway.
            float heldDt = dtSim;

            int steps = 0;
            while (dtSim > 0f && steps++ < MostSteps)
            {
                float slice = Mathf.Min(Step, dtSim);
                dtSim -= slice;
                for (int i = 0; i < _cars.Length; i++) Advance(i, slice, heldDt);
                heldDt = 0f;                              // at most once per frame
            }
        }

        private void Advance(int slot, float dt, float heldDt)
        {
            var car = _cars[slot];
            if (car == null) return;

            // The un-routable case: no drive is drawn, the clock still runs, and the arrival is
            // reported. See OffStageSeconds.
            if (car.OffStage >= 0f)
            {
                car.OffStage -= dt;
                if (car.OffStage <= 0f) Park(car);
                return;
            }

            if (car.Arrived || car.What == null) return;
            if (_traffic == null || _traffic.Graph == null) return;

            if (car.InTurn) CrossJunction(slot, car, dt, heldDt);
            else RunSegment(slot, car, dt, heldDt);

            // Destroyed by the branch above, or parked by it — Park has already put it where it
            // stands and Seat would undo the offset.
            if (_cars[slot] == null || car.Arrived || car.What == null) return;
            Seat(car);
        }

        /// <summary>Along a straight piece of lane, up to the stop beside the scene or the end.
        /// `dt` is sim seconds of driving; `heldDt` is this frame's REAL seconds, charged only
        /// when held (see <see cref="Patience"/>).</summary>
        private void RunSegment(int slot, Car car, float dt, float heldDt)
        {
            var segment = _traffic.Graph.Segments[car.Segment];
            float step = ResponseSpeed * dt;

            bool last = car.Leg >= car.Turns.Count;
            bool stopping = last && !car.Leaving;
            float toStop = stopping ? car.StopAtS - car.S : float.MaxValue;

            if (stopping)
            {
                // THE NOSE STOPS AT THE SCENE, NOT THE MIDDLE OF THE CAR — `RunSegment`'s own
                // arithmetic (`CityTraffic.cs:1039`), for the same reason it is written there:
                // `S` is the car's CENTRE, so easing the centre to the stop leaves the front
                // `Reach` metres past it. The taper is the same one a car eases to a stop line
                // with, so a response vehicle arrives rather than stopping dead on the spot.
                float allowed = Mathf.Max(0f, toStop - car.Reach - 0.4f);
                step = Mathf.Min(step, allowed, Mathf.Max(step * (toStop / Braking), 0f));

                if (allowed <= 0.05f) { Park(car); return; }
            }

            if (Held(car))
            {
                // THE BAND IS THE GOOD-LOOKING CASE: a clean stop AT the scene. Held on the final
                // approach, within the following box of the stop, is close enough to be there —
                // which is what makes the sibling vehicle parked directly in front an arrival
                // rather than a wait. See ArrivalMargin.
                if (stopping && toStop <= car.Reach + ObstacleReach + Headway + ArrivalMargin)
                { Park(car); return; }

                // THE DEADLINE IS THE LIVENESS GUARANTEE, and it applies ANYWHERE en route — see
                // HeldTooLong. It charges REAL seconds, once per frame.
                HeldTooLong(slot, car, heldDt);
                return;
            }

            car.S += step;

            // MOVED, so the wait starts again from nothing. Anything that covers ground is
            // progress; the deadline above is only ever meant to catch a vehicle that is not
            // moving at all.
            if (step > 0.001f) car.HeldFor = 0f;

            if (car.S < segment.ToS) return;

            if (last)
            {
                // Off the end of the last lane: away over the edge of the map if it is leaving,
                // and otherwise a stop it drove past — park where it stands rather than carry on
                // into a lane nothing planned.
                if (car.Leaving) { Despawn(slot); return; }
                car.S = segment.ToS;
                Park(car);
                return;
            }

            car.InTurn = true;
            car.T = 0f;
        }

        /// <summary>Through the junction on the next planned turn. `dt` is sim seconds of
        /// driving; `heldDt` is this frame's REAL seconds, charged only when held.</summary>
        private void CrossJunction(int slot, Car car, float dt, float heldDt)
        {
            int turn = car.Turns[car.Leg];

            // The chord rather than the arc: a quadratic through three points a junction wide is
            // barely longer than the straight line between its ends, and the difference is a few
            // tenths of a second of crossing time at this scale.
            _traffic.TurnArc(turn, out var a, out _, out var c);
            float length = Mathf.Max(0.5f, Vector3.Distance(a, c));

            // A JUNCTION IS NOT AN EXEMPTION. Held mid-arc counts against the same deadline it
            // would on a straight, because a vehicle stopped inside a junction is if anything more
            // urgent to resolve than one stopped on a lane. See HeldTooLong.
            if (Held(car)) { HeldTooLong(slot, car, heldDt); return; }

            car.T += ResponseSpeed * dt / length;
            car.HeldFor = 0f;                    // moving, so the wait starts again from nothing
            if (car.T < 1f) return;

            var to = _traffic.Graph.Segments[_traffic.Graph.Turns[turn].To];
            car.Segment = to.Index;
            car.S = to.FromS;
            car.Leg++;
            car.InTurn = false;
            car.T = 0f;
        }

        /// <summary>
        /// Charge this frame's REAL seconds of standing still, and end the journey if it has gone
        /// on too long. True when the vehicle is finished with — parked or destroyed — so the
        /// caller must not touch it again.
        ///
        /// THIS IS THE LIVENESS GUARANTEE, AND IT IS DELIBERATELY NOT A GEOMETRY. Three wedges
        /// were found and patched here one shape at a time — the sibling parked nose to nose, then
        /// ambient cars queued between them, then the same thing part-way round a junction — and
        /// each patch only moved the failure somewhere the last one could not see. They are all
        /// the same fault: this system parks a vehicle that blocks a lane, and everything stopped
        /// behind that block is stopped until the case closes, which cannot happen until the
        /// vehicle it is blocking arrives. So the rule is not about where the vehicle is. Anything
        /// answering a call that has not covered ground in <see cref="Patience"/> REAL seconds, on
        /// any segment, mid-turn included, ARRIVES WHERE IT STANDS. Arrival is what unblocks the
        /// case; Task 12's officer walks whatever difference is left, and a police car stopped a
        /// hundred feet short of a scene is a cost this project has accepted in writing.
        ///
        /// ONE ON ITS WAY OUT VANISHES INSTEAD, because nothing is waiting on a departure — the
        /// case is already closed — so there is no arrival to report and no reason to leave a
        /// vehicle standing in a street it cannot get out of. It says so in the log rather than
        /// disappearing quietly.
        /// </summary>
        private bool HeldTooLong(int slot, Car car, float realDt)
        {
            car.HeldFor += realDt;
            if (car.HeldFor < Patience) return false;

            if (car.Leaving)
            {
                Debug.Log($"[response] {NameOf((Rig)slot)} boxed in while leaving — "
                        + "despawned in place");
                Despawn(slot);
                return true;
            }

            Park(car);
            return true;
        }

        /// <summary>
        /// Is there something close in front, going roughly this way?
        ///
        /// The moving half is <see cref="CityTraffic.AnyMoverWithin"/>, which is `Blocked`'s own
        /// loop — parallel-only, because cross traffic at a junction is the signals' business and
        /// counting it stops a car dead in a box it has already claimed.
        ///
        /// THE OBSTACLE LIST IS NO LONGER "THINGS THAT ARE STANDING STILL", and this comment said
        /// it was until this file put every response vehicle in it at spawn (see the class header
        /// and <see cref="Dress"/>). It now holds the player's parked car, any response vehicle
        /// parked at a scene, AND both response vehicles while they are driving. The arm stays
        /// HEADING-BLIND anyway: a `Transform` in that list carries no travel direction to compare
        /// against — `Mover.Forward` is the fleet's private business — and the alternative, judging
        /// a hazard on where it happens to be pointing, is how a parked car sideways-on to the lane
        /// becomes invisible. A box test with no heading is the conservative reading.
        ///
        /// WHAT IT SKIPS: itself, and nothing else. Both response transforms are in this list from
        /// the moment they spawn, so the two rigs see EACH OTHER through this same scan — and this
        /// scan is the ONLY channel they can appear to each other on: response vehicles are never
        /// in `_movers`, so `AnyMoverWithin` has never heard of them. Skipping a moving sibling
        /// here (tried, then removed) made a rig following its sibling drive straight into it.
        /// What heading-blindness costs is the head-on case — two rigs meeting in a narrow alley
        /// each fall inside the other's box and each holds for the other — and that standoff is
        /// not resolved here, it is BOUNDED: the <see cref="Patience"/> deadline, on the REAL
        /// clock, parks an arriving rig where it stands and despawns a leaving one, so neither
        /// waits for ever.
        /// </summary>
        private bool Held(Car car)
        {
            var at = car.What.position;

            if (_traffic.AnyMoverWithin(at, car.Forward, car.Reach)) return true;

            for (int i = 0; i < _traffic.Obstacles.Count; i++)
            {
                var what = _traffic.Obstacles[i];
                if (what == null || what == car.What) continue;

                var gap = what.position - at;
                float ahead = Vector3.Dot(gap, car.Forward);
                if (ahead <= 0f) continue;
                if (ahead > car.Reach + ObstacleReach + Headway) continue;
                if (Vector3.Cross(car.Forward, gap).magnitude > LookWide) continue;
                return true;
            }
            return false;
        }

        /// <summary>Put the vehicle where its position says it is, pointing where it is going —
        /// <see cref="CityTraffic"/>'s `Seat`, through the public wrappers.</summary>
        private void Seat(Car car)
        {
            Vector3 at, ahead;

            if (car.InTurn)
            {
                int turn = car.Turns[car.Leg];
                at = _traffic.PointInTurn(turn, car.T);
                ahead = _traffic.PointInTurn(turn, Mathf.Min(1f, car.T + 0.05f));
                if ((ahead - at).sqrMagnitude < 0.0001f) ahead = at + car.Forward;
            }
            else
            {
                at = _traffic.PointOn(car.Segment, car.S);
                ahead = _traffic.PointOn(car.Segment, car.S + 1f);
            }

            var forward = ahead - at;
            if (forward.sqrMagnitude > 0.0001f) car.Forward = forward.normalized;

            car.What.position = at;
            car.What.rotation = Quaternion.LookRotation(car.Forward, Vector3.up);
        }

        /// <summary>
        /// Stop, pull over, and tell the case it is here.
        ///
        /// HALF A LANE TOWARD THE VERGE, MEASURED RATHER THAN TYPED: `CityStreets.LaneOffset` is
        /// the distance from the centre line to the middle of a lane, so its lane-0 value is
        /// exactly half a lane width off the asphalt this road was actually paved with. A vehicle
        /// answering a call stands at the kerb rather than square in the running lane.
        ///
        /// IT STILL BLOCKS THE LANE, AND THAT IS THE INTENDED FICTION. Half a lane is well inside
        /// `LookWide`, and <see cref="CityTraffic.Blocked"/> only ever STOPS a car — there is no
        /// overtake, no lane change and no rerouting anywhere in that file — so what forms behind
        /// a parked cruiser is a permanent queue, not a stream flowing round it. A cruiser at a
        /// scene closing a lane is exactly what a town sees; what bounds the jam is that the
        /// response is FINITE. The case machine orders `VehiclesLeave`, <see cref="Depart"/>
        /// despawns, and the lane opens. If a case ever fails to close, that queue is permanent —
        /// which is one more reason nothing in this file may wedge on a route it cannot plan.
        /// </summary>
        private void Park(Car car)
        {
            car.Arrived = true;
            car.OffStage = -1f;

            if (car.What != null)
            {
                var line = _world.Roads.Lines[_traffic.Graph.Segments[car.Segment].Line];
                car.What.position += car.What.right * CityStreets.LaneOffset(line.Class, 0);
                car.ParkedAt = car.What.position;

                // Normally already registered — Dress does it at spawn — but a vehicle that is
                // standing at a scene is the one that MUST be in this list, so it is asserted
                // here rather than assumed.
                if (!_traffic.Obstacles.Contains(car.What)) _traffic.Obstacles.Add(car.What);
            }
            else car.ParkedAt = car.SceneAt;

            // Nulled BEFORE it fires, so a callback that orders this vehicle away — or sends it
            // somewhere else — cannot be answered by a second arrival.
            var fire = car.OnArrived;
            car.OnArrived = null;
            fire?.Invoke();
        }

        private void Despawn(int slot)
        {
            // WHEREVER THE COUNTY CAR GOES, SO DOES ITS OFFICER. This is the other half of the
            // order in Depart, and it is the half that catches everything else: the recall in
            // DriveIn, the boxed-in despawn in HeldTooLong, and the vehicle that reaches the map
            // edge on its way out. He exists only while his car is at a scene, and this is the one
            // place that is true of every way a car can stop being at one.
            if (slot == (int)Rig.County) SendHimHome();

            var car = _cars[slot];
            if (car == null) return;

            if (car.What != null)
            {
                if (_traffic != null) _traffic.Obstacles.Remove(car.What);
                Destroy(car.What.gameObject);
            }
            _cars[slot] = null;
        }

        private void OnDestroy()
        {
            if (_traffic == null) return;
            for (int i = 0; i < _cars.Length; i++)
                if (_cars[i] != null && _cars[i].What != null)
                    _traffic.Obstacles.Remove(_cars[i].What);
        }

        // ---- the vehicle itself ---------------------------------------------------------

        /// <summary>
        /// Give the car a body. In the editor that is the pack's prefab; anywhere else it is a
        /// bare GameObject — INVISIBLE BUT REAL, so the drive, the arrival, the obstacle it makes
        /// of itself and the case it is answering all happen identically in a shipped build. What
        /// is missing is the mesh, and <see cref="Create"/> has already said so.
        /// </summary>
        private void Dress(Car car, Rig rig)
        {
            GameObject go = null;
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOf(rig));
            if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
#endif
            // Unity's own == , not `??=`: a destroyed or unloadable prefab instance is a "fake
            // null" that the C# null-coalescing operators walk straight past.
            if (go == null) go = new GameObject(NameOf(rig));
            go.name = NameOf(rig);
            go.transform.SetParent(transform, false);

            car.What = go.transform;
            car.Reach = LengthOf(go) * 0.5f;

            // VISIBLE TO THE AMBIENT FLEET FROM THE MOMENT IT EXISTS, not merely once it parks.
            //
            // A response vehicle is in neither `_movers` nor `Obstacles` while it drives, and
            // those two lists are the whole of what `CityTraffic.Blocked` consults — so an
            // ambulance crossing a junction against the light was something no ambient car could
            // see, and cross traffic drove straight through it. Registering here rather than at
            // the stop costs nothing (the obstacle arm is a box test against a transform, and it
            // does not care whether that transform is moving) and closes the one case where this
            // system's deliberate disregard for the signals could put two vehicles in the same
            // space. It is removed in Despawn and nowhere else.
            if (_traffic != null && !_traffic.Obstacles.Contains(car.What))
                _traffic.Obstacles.Add(car.What);
        }

        /// <summary>A vehicle's length nose to tail, off the mesh — <see cref="CityTraffic"/>'s
        /// `Length`, restated because it is private there. These are modelled facing +z.</summary>
        private static float LengthOf(GameObject car)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                lo = Mathf.Min(lo, b.min.z);
                hi = Mathf.Max(hi, b.max.z);
            }
            return hi > lo ? hi - lo : DefaultReach * 2f;
        }

        // ---- the officer on foot --------------------------------------------------------

        /// <summary>Where the county officer is standing, or null when there is no officer —
        /// which is every moment the county car is not at a scene.</summary>
        public Vector3? CountyOfficerAt =>
            _officer != null ? _officer.position : (Vector3?)null;

        /// <summary>
        /// Walk the county officer from wherever he stands to this tile; fires
        /// <paramref name="onArrived"/> exactly once. He exists only while his car is at a scene.
        ///
        /// HE IS SPAWNED HERE, LAZILY, ON THE FIRST DOOR HE IS SENT TO. There is no arrival hook
        /// to hang it on that would be honest: <see cref="Park"/> fires the case machine's own
        /// callback, and a callback is the machine's to decide about — putting a spawn inside it
        /// would mean an officer standing beside every parked county car in the game whether
        /// anybody was going to canvass or not, including the ones that parked a hundred feet
        /// short because <see cref="Patience"/> ran out. First-order-spawns is the simplest thing
        /// that is exactly true: he gets out of the car when there is a door to knock on.
        ///
        /// ORDERED WITH NO CAR AT THE SCENE, he reports the door immediately and says so. That
        /// cannot happen from Task 13's machine, which canvasses only after an arrival; the
        /// alternative to reporting is a callback that never fires, which is the wedge shape this
        /// whole file is written against.
        /// </summary>
        public void WalkCountyTo(Tile door, System.Action onArrived)
        {
            if (_officer == null && !SendHimOut())
            {
                Debug.LogWarning($"[response] a door at ({door.X},{door.Y}) was canvassed with no "
                               + "county car at the scene — reporting it unwalked");
                onArrived?.Invoke();
                return;
            }

            // A SECOND ORDER BEFORE THE FIRST FINISHED. The case machine knocks on one door at a
            // time and waits for the callback, so this is defensive only — but the alternative to
            // replacing the walk outright is two routes and two callbacks racing over one
            // transform, and the first one's promise is dropped rather than fired because he
            // plainly did not reach that door.
            if (_foot != Foot.Idle)
                Debug.LogWarning($"[response] the officer was still on his way to "
                               + $"({_door.X},{_door.Y}); sending him to ({door.X},{door.Y}) "
                               + "instead — the first door goes unanswered");

            _door = door;
            _atDoor = onArrived;
            _standing = 0f;
            _searching = 0f;
            _lookAgain = 0f;
            PlanTheWalk();
        }

        /// <summary>
        /// Put him on the pavement beside the parked county car. False when there is no county
        /// car standing anywhere, which is the one case the caller has to report around.
        /// </summary>
        private bool SendHimOut()
        {
            if (_world == null || _world.Grid == null) return false;

            // THE RIG'S OWN PUBLIC ANSWER, not a second copy of where it parked. `ParkedAt` is
            // already the half-lane offset for a car that drove and the scene point for one that
            // arrived off-stage, and both of those are decisions this method must not re-take.
            var parked = ParkedAt(Rig.County);
            if (parked == null) return false;

            GameObject go = null;
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OfficerPrefab);
            if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
#endif
            // Unity's own ==, not `??=`: an unloadable prefab instance is a "fake null" the C#
            // null-coalescing operators walk straight past. Same trap, same shape, as Dress.
            if (go == null) go = new GameObject("county officer");
            go.name = "county officer";
            go.transform.SetParent(transform, false);

            // The same navy the precinct wears — one treatment, one table of measured cells,
            // in AgentBody.PoliceCells. A no-op on the bare-GameObject fallback.
            AgentBody.UniformThisInstance(go);

            // ---- how tall he is ----
            //
            // UNIFORM, and that is the safe one. The warning in `AgentBody` is against the
            // NON-uniform version — `new Vector3(wide, tall, wide)`, which shears a skinned
            // hierarchy as its bones swing — and this is the arm that replaced it and has run on
            // every rigged figure in Rossville since 2026-08-08. See AdultHeight for why 1.71.
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                if (b.size.y > 0.01f)
                    go.transform.localScale = Vector3.one * (AdultHeight / b.size.y);
            }

            // ---- and something to play ----
            //
            // The prefab brings its own Animator with its avatar bound; only the controller is
            // missing. See TownsfolkController. Every one of the four settings below is
            // `AgentBody`'s, for the reasons argued out at length there — root motion off because
            // the walk decides where he is, CullUpdateTransforms because CullCompletely stops a
            // figure dead when it leaves view, and UnscaledTime because the sim itself is unscaled
            // and legs on a different clock from positions is what skating is.
            _officerAnim = go.GetComponentInChildren<Animator>();
            if (_officerAnim != null)
            {
#if UNITY_EDITOR
                var controller =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(TownsfolkController);
                if (controller != null) _officerAnim.runtimeAnimatorController = controller;
#endif
                _officerAnim.applyRootMotion = false;
                _officerAnim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                _officerAnim.updateMode = AnimatorUpdateMode.UnscaledTime;
            }

            _officer = go.transform;
            _officerAt = NearestFoothold(Space3D.FromWorld(OutOfTheCar(parked.Value)));
            _officerFacing = Vector3.forward;
            _officerPace = 0f;
            _lookAgain = 0f;
            _foot = Foot.Idle;
            _walk.Clear();
            _walked = 0;

            // Snapped rather than swung: there is no previous facing to turn away from.
            SeatOfficer(360f);
            return true;
        }

        /// <summary>
        /// The middle of the pavement beside the parked cruiser, rather than the driver's seat.
        ///
        /// <see cref="ParkedAt"/> IS THE CAR'S CENTRE, and standing him on it puts him inside the
        /// bodywork. The tile under it is ordinary walkable carriageway, so
        /// <see cref="NearestFoothold"/> is perfectly happy and hands it straight back. Nothing
        /// downstream can catch this; it is only visible by looking at him.
        ///
        /// THE PARKED CAR IS AT THE ASPHALT EDGE, NOT HALF A LANE OUT, AND THE OFFSET IS COUNTED
        /// TWICE TO GET THERE. `CityTraffic.PointOn` has ALREADY placed the car on its own lane
        /// centre — `LaneOffset(class, segment.Lane)` — before <see cref="Park"/> adds a further
        /// `LaneOffset(class, 0)` on top. For a class with one lane each way those two sum to the
        /// half-width of the asphalt, which is to say the kerb; a main road's outer lane lands on
        /// its own kerb the same way. So reaching the pavement means subtracting BOTH, and an
        /// earlier version of this subtracted only the second — overshooting by exactly one lane
        /// offset, which is 4.9 ft past the kerb on a street and clean outside the corridor on an
        /// alley.
        ///
        /// `CityStreets.VergeOffset` is the measured middle of the pavement — its own docstring
        /// says "for anything that stands rather than drives", which is this exactly. Everything
        /// here comes off the asphalt this road class was really paved with, so an alley and a
        /// main road each get their own answer, and asking the segment for its own `Lane` rather
        /// than assuming lane 0 keeps it right for a vehicle parked in an outer lane.
        ///
        /// A VEHICLE WITH NO BODY NEVER DROVE: it arrived off-stage and `ParkedAt` is the scene
        /// point rather than a spot on any lane, so there is no road class to ask about and no
        /// bodywork to stand outside of. It is returned unmoved.
        /// </summary>
        private Vector3 OutOfTheCar(Vector3 parked)
        {
            var car = _cars[(int)Rig.County];
            if (car == null || car.What == null) return parked;
            if (_traffic == null || _traffic.Graph == null || _world == null) return parked;

            var segment = _traffic.Graph.Segments[car.Segment];
            var klass = _world.Roads.Lines[segment.Line].Class;

            return parked + car.What.right
                 * (CityStreets.VergeOffset(klass)
                    - CityStreets.LaneOffset(klass, segment.Lane)
                    - CityStreets.LaneOffset(klass, 0));
        }

        /// <summary>
        /// The car has left, so he goes with it — destroyed where he stands rather than walked
        /// back to the kerb; see the note in <see cref="Depart"/>. Everything he was doing goes
        /// too, INCLUDING ANY PENDING CALLBACK, which is DROPPED RATHER THAN FIRED: a callback is
        /// a promise that he reached a door, and a scene whose car has been ordered away is not a
        /// scene anybody is still canvassing.
        /// </summary>
        private void SendHimHome()
        {
            if (_officer != null) Destroy(_officer.gameObject);
            _officer = null;
            _officerAnim = null;

            _walk.Clear();
            _walked = 0;
            _foot = Foot.Idle;
            _standing = 0f;
            _searching = 0f;
            _lookAgain = 0f;
            _officerPace = 0f;
            _atDoor = null;
        }

        /// <summary>
        /// One frame of walking, on SIM seconds — the same clock the vehicles drive on, so a
        /// paused sim stands him still and a fast-forwarded one hurries him along at the same
        /// rate it hurries a citizen going to work.
        ///
        /// WHERE HE IS, ONLY. What his legs do is <see cref="Animate"/>, which is driven from
        /// <see cref="Update"/> above every early return in this one — a paused sim reaches the
        /// animation and does not reach this.
        /// </summary>
        private void WalkTheOfficer(float dt)
        {
            if (_officer == null) return;

            switch (_foot)
            {
                case Foot.Idle:
                    return;

                case Foot.Standing:
                    _standing -= dt;
                    if (_standing <= 0f) ReachedTheDoor();
                    return;

                case Foot.Looking:
                    _searching += dt;

                    // NOT EVERY FRAME. A refusal costs the whole node allowance before it can be
                    // reported, so asking again immediately is how a bounded retry turns into the
                    // most expensive thing in the town. See LookAgainEvery.
                    _lookAgain -= dt;
                    if (_lookAgain > 0f) return;

                    if (!PlanTheWalk())
                    {
                        // STILL GIVING UP, AND THE RETRIES ARE BOUNDED. He has already stood here
                        // for the whole of SearchPatience, which is the same two sim minutes the
                        // kerb fallback would have made him stand — so the door is reported now
                        // rather than after another two. See SearchPatience.
                        if (_foot == Foot.Looking && _searching >= SearchPatience)
                        {
                            Debug.Log($"[response] the search for a walk to door "
                                    + $"({_door.X},{_door.Y}) kept giving up, "
                                    + "canvassing from the kerb");
                            ReachedTheDoor();
                        }
                        return;
                    }
                    break;
            }

            StepAlong(dt);
        }

        /// <summary>
        /// Ask the walkable grid for a route to <see cref="_door"/> and set him walking it. False
        /// when there is nothing to walk this pass — he is either standing out the kerb fallback
        /// or waiting to be asked again.
        /// </summary>
        private bool PlanTheWalk()
        {
            if (_world == null || _world.Grid == null) { StandAtTheKerb(); return false; }
            if (_finder == null) _finder = new Pathfinder(_world.Grid);

            // PLAIN FindPath, NEVER FindPathViaAlley. That one models a boy taking the back lane
            // because it is the back lane, at up to 1.8x the direct route; an officer walking to
            // a front door goes the way that gets him there.
            var outcome = _finder.FindPath(_officerAt.ToTile(), _door, _walk);

            if (outcome == PathOutcome.Found)
            {
                _walked = 0;
                _foot = Foot.Walking;
                return true;
            }

            // A PROPERTY OF THE MAP, so asking again is wasted work — the door is behind
            // something, or he is standing somewhere the grid does not call walkable.
            if (outcome == PathOutcome.NoRouteExists) { StandAtTheKerb(); return false; }

            // A PROPERTY OF THIS MOMENT: the search ran out of node budget. Worth asking again
            // shortly, and PathOutcome's own header says so — but SHORTLY, not immediately. This
            // is the one place the retry clock is armed, so there is exactly one thing deciding
            // how often the town pays for a refusal.
            _foot = Foot.Looking;
            _lookAgain = LookAgainEvery;
            return false;
        }

        /// <summary>He cannot walk there, so he does the interview from where he is: two sim
        /// minutes on his feet and then the door is reported answered. See <see cref="KerbStand"/>
        /// for why a door the grid cannot reach may not stop a case.</summary>
        private void StandAtTheKerb()
        {
            Debug.Log($"[response] no walkable route to door ({_door.X},{_door.Y}), "
                    + "canvassing from the kerb");

            _walk.Clear();
            _walked = 0;
            _standing = KerbStand;
            _foot = Foot.Standing;
        }

        /// <summary>
        /// Cover this slice's ground along the planned tiles.
        ///
        /// The pace is the simulation's own, terrain and all — see <see cref="WalkSpeed"/> — and
        /// the leftovers of a slice carry on into the next tile rather than being thrown away, so
        /// his speed does not depend on how many tile boundaries happened to fall inside a frame.
        /// </summary>
        private void StepAlong(float dt)
        {
            var grid = _world.Grid;

            int guard = 0;
            while (dt > 0f && _walked < _walk.Count && guard++ < MostTiles)
            {
                var here = _officerAt.ToTile();
                float cost = grid.InBounds(here)
                    ? grid.MoveCost(here.X, here.Y)
                    : TileGrid.TypicalMoveCost;
                if (cost > 100f) cost = TileGrid.TypicalMoveCost;
                float speed = WalkSpeed / cost;
                _officerPace = speed;          // what his legs have to keep up with; see Animate

                var target = Vec2.CentreOf(_walk[_walked]);
                var delta = target - _officerAt;
                float dist = Mathf.Sqrt(delta.LengthSquared);

                // Village y runs SOUTH and world z is its negation, as everywhere in Space3D.
                if (dist > 0.0001f)
                    _officerFacing = new Vector3(delta.X, 0f, -delta.Y).normalized;

                float step = speed * dt;
                if (dist <= step)
                {
                    _officerAt = target;
                    _walked++;
                    dt -= dist / speed;
                }
                else
                {
                    _officerAt += delta * (step / dist);
                    dt = 0f;
                }
            }

            SeatOfficer(TurnRate * Time.unscaledDeltaTime);
            if (_walked >= _walk.Count) ReachedTheDoor();
        }

        /// <summary>He is there. Report it once.</summary>
        private void ReachedTheDoor()
        {
            _walk.Clear();
            _walked = 0;
            _standing = 0f;
            _searching = 0f;
            _lookAgain = 0f;
            _officerPace = 0f;
            _foot = Foot.Idle;

            // Nulled BEFORE it fires, exactly as Park does it and for the same reason: a callback
            // that sends him straight on to the next door must not find this walk's arrival still
            // pending behind it.
            var fire = _atDoor;
            _atDoor = null;
            fire?.Invoke();
        }

        /// <summary>
        /// Put him where his position says he is, on the ground and turning towards the way he is
        /// walking. <see cref="Space3D.ToWorld(Vec2)"/> folds the relief in, so he walks over what
        /// little of it Rossville has rather than through it.
        ///
        /// <paramref name="turn"/> is how many degrees he may swing this pass — see
        /// <see cref="TurnRate"/> for why he swings at all rather than snapping. Pass 360 to place
        /// him outright, which is what spawning him is.
        /// </summary>
        private void SeatOfficer(float turn)
        {
            if (_officer == null) return;

            _officer.position = Space3D.ToWorld(_officerAt);
            if (_officerFacing.sqrMagnitude > 0.0001f)
                _officer.rotation = Quaternion.RotateTowards(
                    _officer.rotation,
                    Quaternion.LookRotation(_officerFacing, Vector3.up),
                    turn);
        }

        /// <summary>
        /// Give his legs something to do, once a pass.
        ///
        /// THE PACE IS METRES PER *REAL* SECOND AND HIS WALK IS METRES PER *SIM* SECOND, and those
        /// are two different numbers whenever the clock is wound. `AgentAnimation.Drive` divides
        /// the pace by the speed the clip was animated at to decide how fast to play it, and the
        /// animator itself runs on `UnscaledTime` — real seconds — so handing it a sim-time pace
        /// would play the walk cycle at 1x while the man covered ten times the ground, which is
        /// the skating fault `Drive`'s whole pace argument exists to remove.
        ///
        /// THE RATIO IS READ OFF THE SPEED DIAL, NOT MEASURED PER FRAME. `dtSim / unscaledDelta`
        /// looks like the same quantity and is not: `dtSim` comes from the sim's own TICK counter,
        /// which is quantised, so the quotient is only that ratio in long-run AVERAGE and is a
        /// frame-rate-dependent lurch in any single frame. Measured at 1x and 60 fps it reads 3.0
        /// where it should read 1.0 — his legs ran at twice the rate of a town walking at 0.9x,
        /// beside him, from the same clip. `VillageHost.Speeds[SpeedIndex]` is the exact multiplier
        /// by construction, and it is what <see cref="AgentMeshView"/> already uses and documents
        /// for precisely this sum.
        ///
        /// HE IS `Talking` WHEN HE IS NOT WALKING, AND THE ROW WAS CHOSEN BY READING IT. `Moving`
        /// short-circuits the activity entirely while he walks — the row is `moving` — so the
        /// activity is only ever consulted while he is stopped, which for this man means standing
        /// at a door interviewing somebody, or doing the same thing from the kerb. The `talking`
        /// row is fifteen standing conversational clips (Talking, Arm Gesture, Shrugging,
        /// Acknowledging, Agreeing) and it is exactly that.
        ///
        /// `visiting` WOULD HAVE SAT HIM DOWN ON THE PAVEMENT. Its row opens `Sitting Idle,
        /// Sitting Talking, Sitting Laughing, Seated Idle` — it is written for somebody in
        /// somebody else's front room — and with a FIXED `who` seed he would have picked one of
        /// those and played it at every door of every case. Nothing would have failed; he would
        /// simply have been sitting in the road. A row name that reads right is not a row that
        /// looks right, and the only way to tell is to open `Content/animations.txt`.
        /// </summary>
        private void Animate()
        {
            if (_officer == null || _officerAnim == null || _host == null) return;

            float pace = _officerPace
                * VillageHost.Speeds[
                    Mathf.Clamp(_host.SpeedIndex, 0, VillageHost.Speeds.Length - 1)];

            AgentAnimation.Drive(
                _officerAnim,
                new AgentAnimation.Situation(Activity.Talking, moving: _foot == Foot.Walking),
                who: OfficerSeed,
                pace: pace,

                // NOTHING TO CORRECT: he is built to AdultHeight, which is the height the clips
                // were authored for, so the stride term would be exactly 1. See AdultHeight.
                stride: 0f);
        }

        /// <summary>
        /// The nearest spot he can actually stand on, searched outwards in rings.
        ///
        /// WHERE HE IS PUT DOWN DECIDES WHETHER HE CAN WALK AT ALL: `Pathfinder.FindPath` answers
        /// `NoRouteExists` for an unwalkable ORIGIN as readily as for an unwalkable destination,
        /// and a bad origin fails EVERY door of the canvass rather than one.
        ///
        /// THE ORDINARY CASE DOES NOT NEED THIS, and an earlier version of this comment claimed it
        /// did. A cruiser that drove parks on carriageway, and both the carriageway and the
        /// pavement <see cref="OutOfTheCar"/> steps him onto are walkable — so the first test
        /// passes and he is handed straight back. What this is really for is the vehicle that did
        /// NOT drive to a spot chosen for standing on: an off-stage arrival is put at the SCENE
        /// tile, which is wherever the player struck somebody, and a <see cref="HeldTooLong"/>
        /// park is wherever the traffic happened to stop it. Neither was picked for being ground a
        /// man can stand on.
        /// </summary>
        private Vec2 NearestFoothold(Vec2 at)
        {
            var grid = _world.Grid;

            var here = at.ToTile();
            if (grid.InBounds(here) && grid.IsWalkable(here)) return at;

            for (int r = 1; r <= Foothold; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        // The ring only: everything inside it was tried on an earlier pass.
                        if (dx > -r && dx < r && dy > -r && dy < r) continue;

                        int x = here.X + dx, y = here.Y + dy;
                        if (!grid.InBounds(x, y) || !grid.IsWalkable(x, y)) continue;
                        return Vec2.CentreOf(new Tile(x, y));
                    }

            // NOWHERE AT ALL, AND IT SAYS SO. The kerb fallback below will handle it — he reports
            // every door of the case from where he stands — but it will do that by logging "no
            // walkable route to door (x,y)" once per door, which points at the doors and not at
            // the cause. This is the only line that names the real one, and it is the difference
            // between a two-minute diagnosis and an afternoon spent auditing addresses.
            Debug.LogWarning($"[response] the officer found nowhere to stand within {Foothold} "
                           + $"tiles of ({at.X:0.0},{at.Y:0.0}) — every door of this case will be "
                           + "canvassed from the kerb");
            return at;
        }
    }
}
