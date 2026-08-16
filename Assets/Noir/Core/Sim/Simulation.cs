using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Core.Sim
{
    /// <summary>Where one person is and what they are doing, right now. Mutable; owned by the sim.</summary>
    public struct AgentState
    {
        public Vec2 Position;
        public Vec2 PreviousPosition;   // for smooth rendering between ticks
        public PlaceId At;              // the place they are currently inside, if any
        public PlaceId Destination;
        public Activity Doing;
        public bool Travelling;

        /// <summary>How far along the current path they are. Simulation's own working state —
        /// public because AgentState is a plain struct, but no consumer outside Simulation.cs
        /// reads it, and none should: it means nothing without the path it indexes into.</summary>
        public int PathIndex;

        /// <summary>
        /// Unit vector they are moving along, and zero when standing still.
        ///
        /// THIS IS THE ANSWER TO "is this person walking", and the only one. <see cref="Travelling"/>
        /// is not: somebody mid-conversation or paused on a threshold is still travelling, they
        /// are simply not moving this second, and the renderer needs to tell those apart to know
        /// whether to swing their legs.
        ///
        /// Which means every path that stops somebody has to say so here. Every one of them goes
        /// through Simulation.StandStill for that reason; a stop that forgets leaves a motionless
        /// figure pointing somewhere they are not going and holding a mid-stride pose forever.
        /// </summary>
        public Vec2 Heading;

        /// <summary>
        /// Carrying something: shopping home from the shop, produce back from the allotment.
        ///
        /// Small, and it does a disproportionate amount of work. A figure with a bag has a
        /// visible reason to be where it is - you can read the purpose of a journey from forty
        /// metres away, without a label and without following anyone.
        /// </summary>
        public bool Carrying;

        /// <summary>
        /// Who they are walking with, if anyone.
        ///
        /// A hundred people each walking their own straight line is the single clearest sign
        /// that a crowd is a simulation rather than a place. Real people leave the house
        /// together: siblings to school, a family to church, a couple to the pub. This is the
        /// follower's leader; the leader's own field stays None.
        /// </summary>
        public CitizenId WalkingWith;

        /// <summary>Ticks left standing in the street talking. Zero when not.</summary>
        public int TalkTicks;

        /// <summary>Who they are talking to.</summary>
        public CitizenId TalkingTo;

        /// <summary>Ticks before they will stop for anyone again. Stops a pair looping forever.
        /// Simulation's own working state — no consumer outside Simulation.cs reads it.</summary>
        public int TalkCooldown;

        /// <summary>
        /// Their place in the line at a counter: 0 is at the front being served, and -1 means
        /// they are not queuing at all.
        ///
        /// Which position each slot is on the ground comes from
        /// <see cref="WorldModel.CounterAt"/> for the place in <see cref="At"/>.
        /// </summary>
        public int QueueSlot;

        /// <summary>
        /// Ticks left standing on a threshold. Zero when not.
        ///
        /// Somebody who steps through a front door without breaking stride is the single
        /// clearest tell that a building is a hole in a wall rather than a place with a door in
        /// it. Four-tenths of a second is enough: a hand on the handle, a step through, and on.
        /// </summary>
        public int DoorPauseTicks;

        /// <summary>
        /// They want to be somewhere and no route to it came back.
        ///
        /// The honest state for a failed journey, and it has to be its own field rather than a
        /// shrug. Everything downstream of <see cref="At"/> — the inspector, the density table,
        /// which windows are lit, and eventually an investigation reasoning about who was where
        /// — believes what the sim wrote there. Writing the destination into it because the walk
        /// could not be worked out does not hide the failure; it converts a person who is late
        /// into a person the game is wrong about, which is far harder to notice and far worse.
        /// </summary>
        public bool Stranded;

        /// <summary>When they first failed to set off. Zero when not stranded.</summary>
        public long StrandedSinceTick;

        /// <summary>Struck down and staying down. Live state that outranks the plan while it
        /// holds — see Activity.Downed. Only Simulation.Down sets it, and only
        /// Simulation.Revive clears it.</summary>
        public bool Downed;

        /// <summary>
        /// The absolute minute (day * 1440 + minuteOfDay) the ambulance brings them back, or
        /// zero when nobody has been taken away. Outranks the plan the same way Downed does —
        /// see Activity.AwayFromTown — while it holds; only Simulation.TakeAway sets it, and
        /// only Simulation.Return, whether called by a hand or by the tick loop reaching this
        /// minute, clears it. int.MaxValue is how "never" is spelled: no clock reaches it.
        /// </summary>
        public int AwayUntilMinute;

        /// <summary>
        /// On their way to, or standing over, a scene nobody's plan sent them to — the first
        /// off-plan walk in the game. Outranks the plan the same way Downed does while it holds:
        /// the tick loop runs RespondTick for them instead of the ordinary journey/Doing logic,
        /// and Arrive is taught to write Activity.Responding rather than whatever the plan's
        /// current block says. Only Simulation.Respond sets it; only Simulation.Release or
        /// Simulation.Down clears it.
        /// </summary>
        public bool Responding;

        /// <summary>Where a Responding agent is walking to, or already standing over. Meaningless
        /// unless Responding is true.</summary>
        public Tile RespondTarget;
    }

    /// <summary>
    /// Who is standing where in one place's queue, and how far through being served the person
    /// at the front is.
    ///
    /// Holders are kept packed from the front, so the first free entry is always the back of the
    /// line and "take the first free slot" and "join the back of the queue" are the same
    /// operation.
    /// </summary>
    internal sealed class CounterQueue
    {
        public readonly CounterLine Line;

        /// <summary>
        /// Everybody waiting, in the order they walked in, packed from the front.
        ///
        /// Allowed to grow past the line's standing positions, and deliberately: your PLACE in a
        /// queue and the TILE you stand on are not the same thing. Somebody eighth in a line six
        /// deep waits by the door until a position frees up, and they are still served before
        /// anybody who came in after them. Capping the order at the number of tiles is what
        /// turns a full shop into a scrum, because the people who could not get on the line have
        /// to be sorted out again somehow, and any answer to that is arbitrary.
        ///
        /// Grown on demand rather than sized to the population. One array of every citizen in
        /// the settlement, per counter, is O(places x population) for a queue that is four deep
        /// on a busy morning — 40 MB at city scale to hold four names.
        /// </summary>
        public CitizenId[] Holders;

        /// <summary>How many of <see cref="Holders"/> are in use. They are always packed.</summary>
        public int Waiting;

        /// <summary>The most that can ever be waiting: everybody in the village at once.</summary>
        private readonly int _limit;

        /// <summary>Who the counter is currently dealing with, and for how much longer.</summary>
        public CitizenId Serving;
        public int ServiceTicks;

        /// <summary>Deep enough for the line itself plus a couple standing by the door, which is
        /// what a busy village shop actually holds. Doubling past it is amortised to nothing.</summary>
        private const int InitialDepth = 8;

        public CounterQueue(CounterLine line, int limit)
        {
            Line = line;
            _limit = limit < 1 ? 1 : limit;

            int depth = _limit < InitialDepth ? _limit : InitialDepth;
            Holders = new CitizenId[depth];
            for (int i = 0; i < Holders.Length; i++) Holders[i] = CitizenId.None;
            Serving = CitizenId.None;
        }

        /// <summary>Join the back of the line, or fail because the whole village is in the shop.</summary>
        public bool TryJoin(CitizenId who, out int slot)
        {
            slot = -1;
            if (Waiting >= _limit) return false;

            if (Waiting >= Holders.Length)
            {
                int grown = Holders.Length * 2;
                if (grown > _limit) grown = _limit;

                var bigger = new CitizenId[grown];
                Array.Copy(Holders, bigger, Holders.Length);
                for (int i = Holders.Length; i < bigger.Length; i++) bigger[i] = CitizenId.None;
                Holders = bigger;
            }

            slot = Waiting;
            Holders[Waiting] = who;
            Waiting++;
            return true;
        }
    }

    /// <summary>
    /// The village, running.
    ///
    /// Fixed 20 Hz step. Speed is never a property of this class — running faster means the host
    /// calls Tick() more often. That is what keeps a fast-forwarded day identical to a watched
    /// one, which in turn is what makes "the same seed gives the same village" true in practice
    /// rather than just in principle.
    /// </summary>
    public sealed class Simulation
    {
        public readonly WorldModel World;
        public readonly Population People;
        public readonly ulong Seed;

        private readonly Pathfinder _pathfinder;
        private readonly AgentState[] _agents;

        /// <summary>
        /// The route each traveller is on. Null for anybody who is not walking anywhere.
        ///
        /// Null rather than an empty list, because a list that has been Cleared still owns the
        /// array it grew: every agent used to keep the longest route they had ever walked for
        /// the rest of the run — 902 bytes a head measured at village scale, and trending toward
        /// map-diameter x 8 as the map grows, which on a 2720x1920 map is 24 KB per citizen who
        /// once crossed it. Routes come from <see cref="_pathPool"/> and go back on arrival, so
        /// what is held is proportional to how many people are walking rather than to how many
        /// people exist.
        /// </summary>
        private readonly List<Tile>[] _paths;
        private readonly Stack<List<Tile>> _pathPool = new Stack<List<Tile>>();

        private readonly DayPlan[] _plans;

        /// <summary>Which day each plan describes. Not one number for the lot, because they are
        /// no longer all rebuilt at the same instant — see <see cref="RefreshSomePlans"/>.</summary>
        private readonly int[] _planDay;

        private int _refreshingDay = -1;
        private int _refreshCursor;

        /// <summary>
        /// How many day plans are rebuilt per tick after midnight.
        /// </summary>
        private readonly int _plansPerTick;

        private GameClock _clock;
        private readonly List<Tile> _scratchPath = new List<Tile>();

        /// <summary>Paths computed per tick, at most. Stops the school bell from causing a spike.</summary>
        public int PathBudgetPerTick = 12;

        /// <summary>
        /// Search nodes expanded per tick, at most.
        ///
        /// A count of queries is not a bound on work: twelve searches across a village cost
        /// almost nothing and twelve across a town can cost a quarter of a million node
        /// expansions between them, so the frame that happens to draw twelve long ones is the
        /// one that stutters. This is the bound that actually holds.
        ///
        /// A fixed integer, never a millisecond budget. A time limit would make the number of
        /// journeys started depend on how busy the machine was, and two runs of the same seed
        /// would diverge the first time one of them was unlucky.
        /// </summary>
        public int PathNodeBudgetPerTick = 60_000;

        /// <param name="pathNodeCap">
        /// Zero, the normal case, lets <see cref="Pathfinder"/> size its own guard. A value above
        /// zero overrides it, INCLUDING past <see cref="Pathfinder.HardNodeCeiling"/>.
        ///
        /// It exists so the ceiling can be re-measured rather than argued about. That number has
        /// to be sized from what honest journeys really cost, and there is no way to learn that
        /// from a run in which the ceiling is already refusing them - the measurement needs a
        /// town whose searches are allowed to finish. This is the seam that lets a diagnostic do
        /// that without editing a const and recompiling. Do not use it to make the game generous.
        /// </param>
        public Simulation(WorldModel world, Population people, ulong seed, int startMinuteOfDay = 0,
                          int pathNodeCap = 0)
        {
            World = world;
            People = people;
            Seed = seed;

            _pathfinder = new Pathfinder(world.Grid, pathNodeCap);

            _counters = new CounterQueue[world.PlaceCount];
            for (int i = 0; i < _counters.Length; i++)
            {
                var line = world.CounterAt(new PlaceId(i));
                if (line != null && line.Count > 0) _counters[i] = new CounterQueue(line, people.Count);
            }

            _agents = new AgentState[people.Count];
            _paths = new List<Tile>[people.Count];
            _plans = new DayPlan[people.Count];
            _planDay = new int[people.Count];
            _retryDelay = new int[people.Count];
            _retryAtTick = new long[people.Count];

            // Enough that the whole settlement is replanned inside one game-minute of midnight,
            // whatever its size. See RefreshSomePlans for why a minute is a safe window.
            _plansPerTick = (people.Count + GameClock.TicksPerMinute - 1) / GameClock.TicksPerMinute;
            if (_plansPerTick < 1) _plansPerTick = 1;

            _clock = new GameClock(GameClock.TickAt(0, startMinuteOfDay));

            // The one place a whole population is planned at once, because there is nothing yet
            // to fall back on and the simulation may be starting at half past eight.
            for (int i = 0; i < people.Count; i++) BuildPlan(i, _clock.Day);
            _refreshingDay = _clock.Day;
            _refreshCursor = people.Count;

            // Everyone starts wherever their plan says they should be at this minute.
            for (int i = 0; i < people.Count; i++)
            {
                var citizen = people.Get(new CitizenId(i));
                var block = _plans[i].At(_clock.MinuteOfDay);
                var spot = TargetIn(block, citizen);

                _agents[i].Position = Vec2.CentreOf(spot);
                _agents[i].PreviousPosition = _agents[i].Position;
                _agents[i].At = block.Where;
                _agents[i].Destination = block.Where;
                _agents[i].Doing = block.What;
                _agents[i].Travelling = false;
                _agents[i].PathIndex = 0;
                _agents[i].WalkingWith = CitizenId.None;
                _agents[i].Heading = Vec2.Zero;
                _agents[i].TalkingTo = CitizenId.None;
                _agents[i].TalkTicks = 0;
                _agents[i].TalkCooldown = 0;
                _agents[i].QueueSlot = -1;
                _agents[i].DoorPauseTicks = 0;
                _agents[i].Stranded = false;
                _agents[i].StrandedSinceTick = 0;
            }
        }

        public GameClock Clock => _clock;
        public int AgentCount => _agents.Length;

        /// <summary>How many people want to be somewhere and have no way of getting there.
        /// Zero is the only acceptable value on a map the village is meant to run on.</summary>
        public int StrandedCount => _strandedCount;

        /// <summary>Searches abandoned at the node cap since the simulation started. A rising
        /// number means the map has outgrown the budget, not that the map is broken.</summary>
        public long GaveUpTotal => _gaveUpTotal;

        /// <summary>
        /// Searches that came back with a route, and ones that came back with a definite no.
        ///
        /// GaveUpTotal on its own cannot be judged. A thousand abandoned searches beside a
        /// million successful ones is a rounding error; beside ten thousand it means the cap is
        /// refusing journeys people could walk, which is the very fault the cap was widened to
        /// fix in the first place. The denominator has to be there or the number is unreadable.
        /// </summary>
        public long PathsFoundTotal => _pathsFoundTotal;

        /// <summary>Searches answered "there is no route" without looking, from the region map.</summary>
        public long NoRouteTotal => _noRouteTotal;

        /// <summary>The walkable areas of the map, for anything that wants to ask why somebody
        /// is stranded rather than merely count them.</summary>
        public WalkableRegions Regions => _pathfinder.Regions;

        /// <summary>
        /// Everyone who cannot reach where they are trying to be, and where that is.
        ///
        /// StrandedCount says how bad it is; this says WHERE, which is the only form of the
        /// answer anybody can act on. A stranded person is not a simulation fault - it is a
        /// map fault, a door with no route to it, and it stays until the map is repaired.
        /// </summary>
        public IEnumerable<(CitizenId Who, PlaceId Where, Tile At, Tile Standing,
                            int TheirRegion, int ItsRegion)> StrandedPeople()
        {
            var regions = Regions;
            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Stranded) continue;

                var citizen = People.Get(new CitizenId(i));
                var block = _plans[i].At(_clock.MinuteOfDay);
                var here = _agents[i].Position.ToTile();
                var there = TargetIn(block, citizen);

                // BOTH ends, because either can be the fault and they need opposite repairs.
                // A destination in a sealed pocket is a door to reconnect; a PERSON in one is
                // somebody the map has built a wall around, and the first version of this
                // reported only the destination - which said 70 of 77 were fine when they were
                // not, because it was looking at the wrong end of the journey.
                yield return (new CitizenId(i), block.Where, there, here,
                              regions.RegionAt(here), regions.RegionAt(there));
            }
        }

        public AgentState GetAgent(int index) => _agents[index];
        public AgentState GetAgent(CitizenId id) => _agents[id.Value];

        /// <summary>
        /// Put one person down where they stand, until <see cref="Revive"/> says otherwise. One
        /// of the sim's four external mutations (Down, Revive, TakeAway/Return, Respond/Release),
        /// because a player's car is genuine history the plan cannot know about. Entry cleanup
        /// mirrors Arrive + StandStill so every derived system — queues, conversations, the
        /// renderer — lets go of them on its own. Consumes no RNG, so every other agent's day is
        /// byte-identical to the un-downed run.
        /// </summary>
        public void Down(CitizenId who)
        {
            int i = who.Value;
            if (_agents[i].AwayUntilMinute != 0) return;  // a body off-map cannot be hit again
            if (_agents[i].Downed) return;

            _agents[i].Downed = true;
            _agents[i].Doing = Activity.Downed;
            _agents[i].Travelling = false;
            ReleasePath(i);
            StandStill(i);
            _agents[i].WalkingWith = CitizenId.None;
            _agents[i].Carrying = false;
            _agents[i].TalkTicks = 0;
            _agents[i].TalkingTo = CitizenId.None;
            _agents[i].TalkCooldown = 0;
            _agents[i].DoorPauseTicks = 0;
            _agents[i].QueueSlot = -1;

            // A responding officer struck down mid-walk has to lose the assignment here, or
            // RespondTick would keep running for a body: the host sees Responding drop and
            // Downed rise on the same tick and reports the officer lost.
            _agents[i].Responding = false;

            // Safe unconditionally - it only decrements StrandedCount when the flag is set.
            // Without this, downing somebody who was mid-retry left StrandedCount permanently
            // one too high: nobody would ever clear a flag whose owner stopped taking journeys.
            ClearStranded(i);
        }

        /// <summary>
        /// Undo <see cref="Down"/>. Clears Downed and hands the citizen back to their plan -
        /// WITHIN THE MINUTE, not whenever the plan's current block next happens to change.
        /// Not literally the very next tick: <see cref="Tick"/>'s wants-check also gates on
        /// DepartureOffset's own per-citizen jitter within the minute, so a revive can still
        /// wait up to that long before the departure fires. <see cref="Tick"/>'s wants-check
        /// fires on `block.Where != Destination`, and Down never touched Destination - it is
        /// left frozen at whatever block was active the moment they went down. For a PROMPT
        /// revive (this method's whole reason to exist: the ambulance, a test's own teardown)
        /// that is very likely STILL the current block, so without more, block.Where would
        /// still equal Destination, wants would stay false, and the citizen would stand exactly
        /// where they were hit - Doing correct, feet wrong - until the plan moved on by itself,
        /// which could be a long wait. Resetting Destination to PlaceId.None makes that
        /// mismatch true immediately, so the departure fires within the minute rather than
        /// waiting for the plan's next scheduled change.
        /// StartJourney overwrites Destination with the real block.Where the moment it starts
        /// (before Travelling is ever set true), so the invalid value never survives past the
        /// one departure it exists to trigger. Consumes no RNG, so reviving somebody disturbs
        /// nobody else's day.
        ///
        /// Exists for two consumers, neither built yet when this was written: the ambulance that
        /// will one day take the body away, and a test that has to hand a shared town back
        /// exactly as it found it after proving a hit works.
        /// </summary>
        public void Revive(CitizenId who)
        {
            int i = who.Value;
            if (!_agents[i].Downed) return;

            _agents[i].Downed = false;
            _agents[i].Destination = PlaceId.None;
        }

        /// <summary>
        /// The ambulance leaves with them. Requires Downed (Phase 2's ambulance only ever
        /// collects a body) — clears it and replaces it with an absent state the renderer
        /// already knows how to not-draw: Doing becomes AwayFromTown, the same value the
        /// out-of-town commuters carry, so no display site needed teaching. int.MaxValue
        /// never returns — dead, and still in the population: 1,300 is load-bearing and
        /// nobody is ever removed, they are frozen out of the world. Consumes no RNG.
        /// </summary>
        public void TakeAway(CitizenId who, int returnAbsoluteMinute)
        {
            int i = who.Value;
            if (!_agents[i].Downed) return;
            _agents[i].Downed = false;
            _agents[i].Doing = Activity.AwayFromTown;
            _agents[i].AwayUntilMinute = returnAbsoluteMinute;
        }

        /// <summary>
        /// Back from the hospital, at their own front door, and the plan takes them from
        /// there — Destination = None is Revive's own trick to fire the departure within
        /// the minute. Public for the same second consumer Revive has: a test handing a
        /// shared town back the way it found it.
        /// </summary>
        public void Return(CitizenId who)
        {
            int i = who.Value;
            if (_agents[i].AwayUntilMinute == 0) return;
            _agents[i].AwayUntilMinute = 0;

            var home = World.GetPlace(People.Get(who).Home);
            if (home != null)
            {
                _agents[i].Position = Vec2.CentreOf(home.Door);
                _agents[i].PreviousPosition = _agents[i].Position;
                _agents[i].At = People.Get(who).Home;
            }
            _agents[i].Destination = PlaceId.None;
        }

        /// <summary>
        /// Send one citizen off their plan entirely and toward a scene nobody staged for them —
        /// the first walk in the game with no Block behind it. Mirrors Down's own entry cleanup
        /// (the same reasons: queues, conversations, the renderer all have to let go of them),
        /// then leaves them to RespondTick, which is the tick loop's whole handling for anyone
        /// with Responding set. Works from Asleep, because the plan is simply not consulted
        /// while Responding holds — the same relationship Downed already has to it. Consumes no
        /// RNG, so nobody else's day moves when an officer is called out.
        ///
        /// No-op if already Responding, or Downed, or the ambulance already has them
        /// (AwayUntilMinute != 0) — a body cannot be dispatched to a scene.
        /// </summary>
        public void Respond(CitizenId who, Tile scene)
        {
            int i = who.Value;
            if (_agents[i].Responding) return;
            if (_agents[i].Downed) return;
            if (_agents[i].AwayUntilMinute != 0) return;

            _agents[i].Travelling = false;
            ReleasePath(i);
            StandStill(i);
            _agents[i].WalkingWith = CitizenId.None;
            _agents[i].Carrying = false;
            _agents[i].TalkTicks = 0;
            _agents[i].TalkingTo = CitizenId.None;
            _agents[i].TalkCooldown = 0;
            _agents[i].DoorPauseTicks = 0;
            _agents[i].QueueSlot = -1;
            ClearStranded(i);

            _agents[i].Responding = true;
            _agents[i].RespondTarget = scene;
            _agents[i].Destination = PlaceId.None;
        }

        /// <summary>
        /// Undo <see cref="Respond"/>. Clears Responding and resets Destination — Revive's own
        /// trick — so the wants-check picks the citizen back up onto their plan within the
        /// minute rather than waiting for the plan's current block to change on its own. No-op
        /// if not Responding.
        /// </summary>
        public void Release(CitizenId who)
        {
            int i = who.Value;
            if (!_agents[i].Responding) return;
            _agents[i].Responding = false;
            _agents[i].Destination = PlaceId.None;
        }

        /// <summary>Forces this one plan up to date first: the tick loop is content to run a few
        /// hundred ticks behind at midnight, and anybody asking from outside is not.</summary>
        public DayPlan PlanFor(CitizenId id)
        {
            if (_planDay[id.Value] != _clock.Day) BuildPlan(id.Value, _clock.Day);
            return _plans[id.Value];
        }

        /// <summary>Where this person is heading, and what they intend to do there.</summary>
        public Block CurrentBlock(CitizenId id) => PlanFor(id).At(_clock.MinuteOfDay);

        public void Tick()
        {
            _clock = _clock.Advance(1);
            RefreshSomePlans();

            int minute = _clock.MinuteOfDay;
            int tickOfMinute = (int)(_clock.Tick % GameClock.TicksPerMinute);
            _pathsThisTick = 0;
            _pathNodesThisTick = 0;
            const float dt = 1f / GameClock.TicksPerSecond;

            for (int i = 0; i < _agents.Length; i++)
            {
                var citizen = People.Get(new CitizenId(i));
                var block = _plans[i].At(minute);

                // A downed agent has left the plan for good: no journeys, no talk, no queue,
                // no Doing overwrite. One branch, taken by nobody in a healthy town — the
                // byte-identical replays behind watched.floor depend on this being a no-op
                // when the flag is never set.
                if (_agents[i].Downed) continue;

                // The ambulance has them, and the plan stays frozen out until the clock reaches
                // the return minute — same shape as the Downed skip above, same no-op guarantee
                // when AwayUntilMinute is never set. `minute` here IS minuteOfDay (see above),
                // so `_clock.Day * 1440 + minute` is the same absolute-minute unit TakeAway was
                // handed.
                if (_agents[i].AwayUntilMinute != 0)
                {
                    int now = _clock.Day * 1440 + minute;
                    if (now < _agents[i].AwayUntilMinute) continue;
                    Return(new CitizenId(i));      // falls through: they rejoin the plan this tick
                }

                // Sent to a scene by nobody's plan: no journeys, no talk, no queue, and Doing is
                // Simulation's to write, not the plan's — RespondTick is this agent's whole tick.
                // Same no-op guarantee as the two gates above when Responding is never set.
                if (_agents[i].Responding) { RespondTick(i, citizen, dt); continue; }

                // Somewhere new to be, and their own moment within this minute to leave for it —
                // or a failed attempt whose backoff has run out, which is not a departure and is
                // not jittered.
                bool wants = (block.Where != _agents[i].Destination
                                 && tickOfMinute >= DepartureOffset(citizen, minute))
                          || (_agents[i].Stranded && _clock.Tick >= _retryAtTick[i]);

                if (wants && CanAffordAPath())
                {
                    StartJourney(i, citizen, block);
                    _pathsThisTick++;
                }
                // Over budget: they set off on the next tick instead. Fifty milliseconds
                // late is invisible, and it keeps the frame flat.

                if (_agents[i].TalkTicks > 0)
                {
                    // Standing in the street mid-sentence. They keep their destination and
                    // their path; they simply are not walking along it this second.
                    _agents[i].TalkTicks--;
                    if (_agents[i].TalkTicks == 0)
                    {
                        _agents[i].TalkingTo = CitizenId.None;
                        _agents[i].Doing = Activity.TravellingTo;
                    }
                    continue;
                }

                if (_agents[i].TalkCooldown > 0) _agents[i].TalkCooldown--;

                // A hand on the handle. They are still Travelling and still hold their path —
                // they are simply not on it for the next fifth of a second.
                if (_agents[i].DoorPauseTicks > 0) { _agents[i].DoorPauseTicks--; continue; }

                if (_agents[i].Travelling)
                {
                    Advance(i, citizen, dt);
                }
                else
                {
                    // Somebody stranded keeps doing whatever they were doing, because that is
                    // what they are still doing. Writing the plan's activity here would be the
                    // same lie as writing the plan's place into At.
                    if (!_agents[i].Stranded) _agents[i].Doing = block.What;

                    if (_agents[i].QueueSlot >= 0) ShuffleUp(i, citizen, dt);
                    else JoinCounter(i, citizen, block);
                }
            }

            ServeCounters();

            // Once a second is plenty - people do not decide to stop for a chat twenty times
            // a second, and it keeps the pair scan off the per-tick path entirely.
            if (_clock.Tick % GameClock.TicksPerSecond == 0) LookForConversations();

            if (_pathNodesThisTick > WorstPathNodesInATick)
                WorstPathNodesInATick = _pathNodesThisTick;
            if (_pathsThisTick > WorstPathsInATick) WorstPathsInATick = _pathsThisTick;
        }

        /// <summary>
        /// The most A* nodes any single tick has expanded since this was last reset, and the most
        /// journeys any single tick started.
        ///
        /// Here to size PathNodeBudgetPerTick against measurement instead of guesswork. The
        /// budget bounds a tick's search work in nodes, but nobody has ever checked what a node
        /// COSTS, and a bound whose units you cannot convert to milliseconds is not a bound on
        /// the frame - which is the thing that actually stutters.
        /// </summary>
        public int WorstPathNodesInATick { get; private set; }

        /// <summary>Companion to <see cref="WorstPathNodesInATick"/>.</summary>
        public int WorstPathsInATick { get; private set; }

        /// <summary>Start a fresh high-water measurement.</summary>
        public void ResetPathHighWater() { WorstPathNodesInATick = 0; WorstPathsInATick = 0; }

        // ---- what the tick is allowed to roll for ----
        //
        // Named purposes, not streams. A stream has a POSITION, and a position is a channel
        // through which one agent's decisions reach another's: whether agent 5 crossed a
        // doorway this tick decided what number agent 6 got out of _doorRng. Substreams were
        // never any defence against that — they protect against adding a SYSTEM, and the thing
        // that actually varies is which agents ran, not which systems exist. Every roll below is
        // a pure function of (seed, purpose, who it is about, when, which roll), so there is
        // nothing left for scan order to influence.

        private static readonly ulong TalkPurpose = Rolls.Purpose("conversations");
        private static readonly ulong QueuePurpose = Rolls.Purpose("queues");
        private static readonly ulong DoorPurpose = Rolls.Purpose("doorways");

        /// <summary>
        /// The extra time somebody who lingers spends on a threshold, and its OWN purpose.
        ///
        /// A separate purpose rather than a wider range on DoorPurpose, deliberately: Rolls is a
        /// positionless hash, so drawing from a new purpose leaves every existing draw in the
        /// village byte-identical. Widening the door draw would change the pause of all 158
        /// people and regenerate nothing but would move everybody, which is the cost this whole
        /// change was scoped to avoid.
        ///
        /// Sized against the WATCHER, not against taste. Eyewitness samples once per simulated
        /// minute (1,200 ticks), so a 6-11 tick pause is caught under 1% of the time — which is
        /// why lingering read as 6 people of 158 and not as a habit. 400-800 ticks is 20-40 s of
        /// game time and a 33-67% chance per crossing, which is the difference between a
        /// coincidence and something a watcher would write down.
        /// </summary>
        private static readonly ulong LingerPurpose = Rolls.Purpose("lingering");
        private const int LingerBase = 400;
        private const int LingerSpread = 400;
        private static readonly ulong DeparturePurpose = Rolls.Purpose("departures");

        private readonly CounterQueue[] _counters;
        private int _pathsThisTick;
        private int _pathNodesThisTick;

        /// <summary>When each agent may ask for a route again, and how long they waited for
        /// this attempt. Kept beside the agents rather than in <see cref="AgentState"/> because
        /// it is bookkeeping about the search, not something true about the person.</summary>
        private readonly int[] _retryDelay;
        private readonly long[] _retryAtTick;

        private int _strandedCount;
        private long _gaveUpTotal;
        private long _pathsFoundTotal;
        private long _noRouteTotal;
        private int _worstNodesFound;

        /// <summary>
        /// HOW FAR THE ABANDONED JOURNEYS WERE TRYING TO GO, in tiles of Chebyshev distance -
        /// max(|dx|,|dy|), which is the number of steps an 8-way walker needs on open ground and
        /// therefore the honest floor on a journey's length. Integer, so it stays inside Core's
        /// ban on transcendentals.
        ///
        /// Added 2026-08-11 to tell two very different faults apart, because the give-up COUNT
        /// cannot. If the abandoned journeys are all enormous, the ceiling is simply below what
        /// this map's honest walks need and the fix is arithmetic. If short journeys are being
        /// abandoned, the search is going wrong somewhere and raising the ceiling would bury it.
        /// </summary>
        private int _gaveUpNearest = int.MaxValue;
        private int _gaveUpFarthest;
        private long _gaveUpDistanceTotal;
        private int _gaveUpUnder200;
        private Tile _gaveUpShortestFrom, _gaveUpShortestTo;

        /// <summary>
        /// How the cost of a SUCCESSFUL search is distributed, in buckets, because the worst case
        /// alone cannot size a ceiling. One freak cross-country walk costing 1.7 million nodes
        /// says nothing about where a cut-off should go; what matters is how many honest journeys
        /// sit under each candidate. Measured 2026-08-11 — see docs/IDEAS.md.
        /// </summary>
        private int _foundOver100k, _foundOver200k, _foundOver400k, _foundOver800k;

        public int FoundOver100k => _foundOver100k;
        public int FoundOver200k => _foundOver200k;
        public int FoundOver400k => _foundOver400k;
        public int FoundOver800k => _foundOver800k;

        public int GaveUpNearest => _gaveUpNearest == int.MaxValue ? 0 : _gaveUpNearest;
        public int GaveUpFarthest => _gaveUpFarthest;
        public int GaveUpMeanDistance => _gaveUpTotal > 0 ? (int)(_gaveUpDistanceTotal / _gaveUpTotal) : 0;
        public int GaveUpUnder200 => _gaveUpUnder200;
        public Tile GaveUpShortestFrom => _gaveUpShortestFrom;
        public Tile GaveUpShortestTo => _gaveUpShortestTo;

        /// <summary>Book-keeping for every search, so the give-up count has a denominator.</summary>
        private void Record(PathOutcome outcome, Tile from, Tile to)
        {
            if (outcome == PathOutcome.GaveUp)
            {
                _gaveUpTotal++;

                int dx = from.X > to.X ? from.X - to.X : to.X - from.X;
                int dy = from.Y > to.Y ? from.Y - to.Y : to.Y - from.Y;
                int far = dx > dy ? dx : dy;

                _gaveUpDistanceTotal += far;
                if (far > _gaveUpFarthest) _gaveUpFarthest = far;
                if (far < 200) _gaveUpUnder200++;
                if (far < _gaveUpNearest)
                {
                    _gaveUpNearest = far;
                    _gaveUpShortestFrom = from;
                    _gaveUpShortestTo = to;
                }
            }
            else if (outcome == PathOutcome.Found)
            {
                _pathsFoundTotal++;

                // The most any journey that actually WORKED had to look at. This is the number
                // the node ceiling has to clear, and the only honest way to size it: set the
                // ceiling below this and the search starts refusing walks people can make,
                // which is what 40,000 did - 73% of every search in the town abandoned, and
                // forty-four people unable to reach the burial ground because it is a long way
                // west and nothing was measuring how far.
                int nodes = _pathfinder.LastNodesExamined;
                if (nodes > _worstNodesFound) _worstNodesFound = nodes;

                if (nodes > 100_000) _foundOver100k++;
                if (nodes > 200_000) _foundOver200k++;
                if (nodes > 400_000) _foundOver400k++;
                if (nodes > 800_000) _foundOver800k++;
            }
            else _noRouteTotal++;
        }

        /// <summary>The most nodes any SUCCESSFUL search has examined. Sizes the node ceiling.</summary>
        public int WorstNodesFound => _worstNodesFound;

        /// <summary>
        /// The backoff schedule: 2 ticks, then 4, 8, and so on to a minute.
        ///
        /// A door nobody can reach is retried every tick without this, and a dozen of them
        /// between them eat the entire per-tick budget — so a handful of people who cannot move
        /// stop everybody else from setting off. Doubling is deterministic and has no cost to
        /// remember, and the ceiling keeps somebody whose route only opens at nine o'clock from
        /// waiting until noon to notice.
        /// </summary>
        private const int FirstRetryTicks = 2;
        private const int LongestRetryTicks = GameClock.TicksPerSecond * 60;

        /// <summary>Is there room in this tick for another search?</summary>
        private bool CanAffordAPath() =>
            _pathsThisTick < PathBudgetPerTick && _pathNodesThisTick < PathNodeBudgetPerTick;

        /// <summary>How close two people must pass to stop and speak, in tiles.</summary>
        private const float TalkRange = 2.4f;

        /// <summary>
        /// People who pass each other in the street sometimes stop and talk.
        ///
        /// This is the cheapest aliveness there is: two figures standing still and facing each
        /// other reads as conversation with no dialogue written, no animation and no audio.
        /// What sells it is that it INTERRUPTS something - they were going somewhere, and now
        /// they are late. A village where everyone walks briskly past everyone else is a
        /// village of strangers.
        ///
        /// Restricted to people walking outdoors, so nobody stops for a chat inside a wall or
        /// while asleep, and gated on sociability so the gregarious stop more often than the
        /// solitary.
        /// </summary>
        private void LookForConversations()
        {
            for (int a = 0; a < _agents.Length; a++)
            {
                if (!Available(a)) continue;

                for (int b = a + 1; b < _agents.Length; b++)
                {
                    if (!Available(b)) continue;

                    var delta = _agents[b].Position - _agents[a].Position;
                    if (delta.LengthSquared > TalkRange * TalkRange) continue;

                    // Companions are already together; they do not stop to greet each other.
                    if (_agents[a].WalkingWith == new CitizenId(b) ||
                        _agents[b].WalkingWith == new CitizenId(a)) continue;

                    var one = People.Get(new CitizenId(a));
                    var two = People.Get(new CitizenId(b));

                    // Rolled about the PAIR, from a key that is the same whichever of them the
                    // scan reached first. Whether these two stop is a fact about these two, and
                    // it has to stay one however the sweep happens to be walked.
                    ulong pair = Keys.Pair(one.Key.Value, two.Key.Value);

                    // Two sociable people stop often; two reserved ones rarely.
                    float chance = 0.06f + (one.Sociability + two.Sociability) / 510f * 0.34f;
                    if (!Rolls.Chance(Seed, TalkPurpose, pair, _clock.Tick, 0, chance)) continue;

                    int seconds = 12 + Rolls.Int(Seed, TalkPurpose, pair, _clock.Tick, 1, 38);
                    int ticks = seconds * GameClock.TicksPerSecond;

                    Begin(a, new CitizenId(b), ticks);
                    Begin(b, new CitizenId(a), ticks);
                    break;   // one conversation each per sweep
                }
            }
        }

        private void Begin(int index, CitizenId partner, int ticks)
        {
            _agents[index].TalkTicks = ticks;
            _agents[index].TalkingTo = partner;
            _agents[index].TalkCooldown = ticks + 90 * GameClock.TicksPerSecond;
            _agents[index].Doing = Activity.Talking;
            StandStill(index);
        }

        /// <summary>Walking, outdoors, and not already mid-conversation or just out of one.</summary>
        private bool Available(int index)
        {
            ref var a = ref _agents[index];
            if (!a.Travelling || a.TalkTicks > 0 || a.TalkCooldown > 0) return false;

            var tile = a.Position.ToTile();
            if (!World.Grid.InBounds(tile)) return false;
            return (World.Grid.FlagsAt(tile) & TileFlags.Indoor) == 0;
        }

        /// <summary>Run many ticks at once — used when fast-forwarding or skipping ahead.</summary>
        public void Tick(int count)
        {
            for (int i = 0; i < count; i++) Tick();
        }

        private void BuildPlan(int index, int day)
        {
            _plans[index] = DayPlanner.Plan(World, People, People.Get(new CitizenId(index)), day, Seed);
            _planDay[index] = day;
        }

        /// <summary>
        /// Midnight, spread over the minute after it rather than landing on one tick.
        ///
        /// Rebuilding every DayPlan at once cost 17.8 ms at five thousand people — a hundred
        /// ordinary ticks' work, on the single tick where the date changes. The plan document
        /// claimed the pure-function DayPlan meant there was "no day-boundary hitch where a
        /// hundred schedules regenerate on one tick"; there was, because the simulation
        /// regenerated all of them anyway.
        ///
        /// What makes spreading it safe rather than merely cheaper: DayPlanner always emits
        /// (0 .. wake) as Asleep at home, and the earliest wake anybody can have is 04:45 — the
        /// five o'clock milking, less fifteen minutes of punctuality. So for the first four and
        /// three quarter hours of any day, yesterday's plan and today's say exactly the same
        /// thing about where a person is and what they are doing. Finishing inside one minute
        /// leaves that margin nearly untouched. Anybody asked about from outside is brought up
        /// to date on the spot; see PlanFor.
        /// </summary>
        private void RefreshSomePlans()
        {
            int day = _clock.Day;
            if (day != _refreshingDay)
            {
                _refreshingDay = day;
                _refreshCursor = 0;
            }

            int end = _refreshCursor + _plansPerTick;
            if (end > _plans.Length) end = _plans.Length;

            for (; _refreshCursor < end; _refreshCursor++)
                if (_planDay[_refreshCursor] != day) BuildPlan(_refreshCursor, day);
        }

        /// <summary>
        /// Which tick of its minute this person leaves on.
        ///
        /// Everyone's day plan changes on a minute boundary, so every journey a day contains
        /// lands on one of 1,440 ticks out of 1,728,000 — and they land in heaps. At five
        /// thousand people, 1,317 of them start on the single 08:30 tick, and PathBudgetPerTick
        /// then turns from a smoother into a queue: 110 ticks to clear, with the last citizen
        /// setting off five and a half seconds after the first. That is not only a spike, it is
        /// the wrong picture — the school run dribbles out of the houses instead of the street
        /// emptying at once.
        ///
        /// So this is aliveness feature #5, punctuality jitter, promoted to load-bearing. It is
        /// no longer decoration that stops everybody moving on the same tick; it is the thing
        /// that keeps the busiest minute of the day inside its own minute.
        ///
        /// Keyed on the HOUSEHOLD, not the person, and that is the whole subtlety. A brother and
        /// sister have to come out of the same front door within a second of each other or
        /// FindCompanion has nothing within six tiles to pair them with, and the companion link
        /// — two dots moving together, which reads as "together" instantly — is exactly the
        /// effect this was supposed to protect. People who leave together leave together; the
        /// spreading happens between houses, which is where it is visible anyway.
        ///
        /// Derived rather than drawn: a pure function of (seed, household, day, minute), so it
        /// cannot depend on how many other agents asked first, and asking twice in the same
        /// minute gives the same answer.
        /// </summary>
        private int DepartureOffset(Citizen citizen, int minuteOfDay)
        {
            var household = People.GetHousehold(citizen.Household);
            ulong who = household != null ? household.Key : citizen.Key.Value;

            return Rolls.Int(Seed, DeparturePurpose, who,
                             (long)_clock.Day * 1440 + minuteOfDay, 0,
                             GameClock.TicksPerMinute);
        }

        /// <summary>How far apart two people can be and still set off together, in tiles.</summary>
        private const float CompanionRange = 6f;

        /// <summary>
        /// Somebody in the same household, already on their way to the same place, close
        /// enough to fall in beside.
        ///
        /// KNOWN ORDER DEPENDENCE, left in deliberately: this reads other agents' Travelling,
        /// Destination, WalkingWith and PathIndex, any of which a lower-indexed agent may
        /// already have changed on this same tick. Whether two siblings pair up therefore
        /// depends on which of them the loop reaches first. Removing it means a phased tick —
        /// decide from last tick's state, commit, then interact — which is a bigger change than
        /// this one and touches every system in the file, so it is not being made here. It is
        /// also the LEAST harmful of the order dependencies, because it decides who somebody
        /// walks beside rather than what any of them do, and both orders produce a real answer.
        ///
        /// Deliberately restricted to a shared roof. Two strangers happening to walk to the
        /// shop at once are not company; a brother and sister leaving for school are. The
        /// distance check is what keeps it honest - you join someone you are standing next to,
        /// not someone already halfway down the lane.
        /// </summary>
        private CitizenId FindCompanion(int index, Citizen citizen, PlaceId destination)
        {
            var household = People.HouseholdMembers(citizen);
            if (household.Count < 2) return CitizenId.None;

            var mine = _agents[index].Position;

            for (int m = 0; m < household.Count; m++)
            {
                int other = household[m].Value;
                if (other == index) continue;

                ref var them = ref _agents[other];
                if (!them.Travelling) continue;
                if (them.Destination != destination) continue;
                if (them.WalkingWith.IsValid) continue;      // no chains of followers

                var delta = them.Position - mine;
                if (delta.LengthSquared > CompanionRange * CompanionRange) continue;

                return household[m];
            }
            return CitizenId.None;
        }

        private void StartJourney(int index, Citizen citizen, Block block)
        {
            // Give up your place in the line before you walk out of the shop, or it stays
            // reserved for you and everybody behind waits on somebody who has gone home.
            _agents[index].QueueSlot = -1;

            bool retry = block.Where == _agents[index].Destination;

            _agents[index].Destination = block.Where;
            _agents[index].WalkingWith = CitizenId.None;

            // What were they doing a minute ago? That is what decides whether they are leaving
            // somewhere with their hands full.
            var previous = _plans[index].At(Math.Max(0, _clock.MinuteOfDay - 1)).What;
            _agents[index].Carrying =
                previous == Activity.Shopping || previous == Activity.InTheGarden
                // Or because they always do. Somebody whose particulars say they never go out
                // empty-handed goes out with something in their hand, and this one line is the
                // whole of what "enacting a particular" means: the sentence in the inspector and
                // the bag you can see from across the road are now the same fact rather than two
                // that happen to agree.
                || (citizen.Beats & Beat.Carries) != 0;

            // Falling in with somebody costs no pathfinding at all: their route is already
            // computed and correct, so we simply adopt the rest of it.
            var companion = FindCompanion(index, citizen, block.Where);
            if (companion.IsValid)
            {
                var leader = _agents[companion.Value];
                var leaderPath = _paths[companion.Value];

                var mine = RentPath(index);
                mine.Clear();
                if (leaderPath != null)
                    for (int i = leader.PathIndex; i < leaderPath.Count; i++)
                        mine.Add(leaderPath[i]);

                if (mine.Count > 0)
                {
                    _agents[index].PathIndex = 0;
                    _agents[index].Travelling = true;
                    _agents[index].At = PlaceId.None;
                    _agents[index].Doing = Activity.TravellingTo;
                    _agents[index].WalkingWith = companion;
                    ClearStranded(index);
                    return;
                }
            }

            var from = _agents[index].Position.ToTile();
            var to = TargetIn(block, citizen);

            _scratchPath.Clear();
            var outcome = _pathfinder.FindPath(from, to, _scratchPath);
            _pathNodesThisTick += _pathfinder.LastNodesExamined;
            Record(outcome, from, to);

            if (outcome != PathOutcome.Found)
            {
                // No route came back, so they are exactly where they were a moment ago and
                // nothing about where they are may be touched. They are recorded as stranded
                // and asked again later; the one thing they must not become is a person the
                // simulation has quietly filed in a building they never walked to.
                Strand(index, retry);
                return;
            }

            ClearStranded(index);

            if (_scratchPath.Count == 0)
            {
                // Already standing on the tile they were headed for. There is no walk, but they
                // genuinely are there, so this is the one case where writing the place is true.
                _agents[index].Travelling = false;
                ReleasePath(index);
                StandStill(index);
                _agents[index].At = block.Where;
                _agents[index].Doing = block.What;
                return;
            }

            var route = RentPath(index);
            route.Clear();
            route.AddRange(_scratchPath);
            _agents[index].PathIndex = 0;
            _agents[index].Travelling = true;
            _agents[index].At = PlaceId.None;
            _agents[index].Doing = Activity.TravellingTo;
        }

        /// <summary>
        /// The tick loop's whole handling for a Responding agent — a trimmed StartJourney with
        /// no companions, no Carrying, no TargetIn, because there is no Block behind this walk
        /// to read any of that from. Standing at the target is itself the "arrived" state; there
        /// is no place to enter and nothing to queue for.
        ///
        /// A failed respond-path goes through the same Strand/backoff machinery every other
        /// failed journey does, and NOT because retrying every tick would otherwise be free:
        /// Regions answers <see cref="PathOutcome.NoRouteExists"/> in O(1), but
        /// <see cref="PathOutcome.GaveUp"/> means a real search ran and spent its whole budget
        /// before giving up, and re-spending that at 20 ticks/second is exactly what Strand's
        /// exponential backoff exists to stop paying for. The two are not told apart here, same
        /// as StartJourney isn't: Strand only doubles the delay when `_retryDelay` is already
        /// set from a previous failure AT THIS SAME TARGET, so a first failure of either kind
        /// still costs one search and backs off at FirstRetryTicks either way.
        /// </summary>
        private void RespondTick(int index, Citizen citizen, float dt)
        {
            if (_agents[index].Travelling) { Advance(index, citizen, dt); return; }

            var from = _agents[index].Position.ToTile();
            if (from == _agents[index].RespondTarget)
            {
                if (_agents[index].Doing != Activity.Responding)
                { _agents[index].Doing = Activity.Responding; StandStill(index); }
                return;
            }

            // Backed off from a failed attempt at this same target: stand and wait out
            // _retryAtTick rather than spending another search this tick. RespondTarget cannot
            // change without going through Respond() again, and Respond()'s own entry cleanup
            // calls ClearStranded — so this can never be reading a delay left over from some
            // OTHER target.
            if (_agents[index].Stranded && _clock.Tick < _retryAtTick[index])
            {
                if (_agents[index].Doing != Activity.Responding)
                { _agents[index].Doing = Activity.Responding; StandStill(index); }
                return;
            }

            if (!CanAffordAPath()) return;
            _scratchPath.Clear();
            var outcome = _pathfinder.FindPath(from, _agents[index].RespondTarget, _scratchPath);
            _pathNodesThisTick += _pathfinder.LastNodesExamined;
            _pathsThisTick++;
            if (outcome != PathOutcome.Found || _scratchPath.Count == 0)
            {
                // Stranded is already true only if this is a repeat failure at this same
                // target (see the comment above) — exactly the condition Strand's own doubling
                // wants, and false on a first failure gives it FirstRetryTicks instead.
                Strand(index, _agents[index].Stranded);
                if (_agents[index].Doing != Activity.Responding)
                { _agents[index].Doing = Activity.Responding; StandStill(index); }
                return;   // stand where they are; the host's per-minute poll sees no arrival and waits
            }
            ClearStranded(index);
            var route = RentPath(index);
            route.Clear();
            route.AddRange(_scratchPath);
            _agents[index].PathIndex = 0;
            _agents[index].Travelling = true;
            _agents[index].At = PlaceId.None;
            _agents[index].Doing = Activity.TravellingTo;
        }

        /// <summary>
        /// They are not moving. The single place that is recorded, so that no stop can forget.
        ///
        /// <see cref="AgentState.Heading"/> is doing double duty — a direction and an is-walking
        /// flag — and the two disagreed in exactly one place: a failed path left a stranded
        /// villager standing still while still pointing down the road they never took. Rather
        /// than add a second field the renderers would have to be taught about, every stop is
        /// routed through here and Heading is made honest enough to be relied on.
        /// </summary>
        private void StandStill(int index) => _agents[index].Heading = Vec2.Zero;

        // ---- routes ----

        /// <summary>This agent's route buffer, taken from the pool if they have not got one.</summary>
        private List<Tile> RentPath(int index)
        {
            var path = _paths[index];
            if (path == null)
            {
                path = _pathPool.Count > 0 ? _pathPool.Pop() : new List<Tile>();
                _paths[index] = path;
            }
            return path;
        }

        /// <summary>Hand the route back the moment they stop needing it. Everything after this
        /// must treat <see cref="AgentState.Travelling"/> as the authority on whether there is
        /// one to walk along.</summary>
        private void ReleasePath(int index)
        {
            var path = _paths[index];
            if (path == null) return;

            path.Clear();
            _paths[index] = null;
            _pathPool.Push(path);
        }

        /// <summary>They have somewhere to walk; forget any failure that came before it.</summary>
        private void ClearStranded(int index)
        {
            if (_agents[index].Stranded) _strandedCount--;
            _agents[index].Stranded = false;
            _agents[index].StrandedSinceTick = 0;
            _retryDelay[index] = 0;
            _retryAtTick[index] = 0;
        }

        private void Strand(int index, bool retry)
        {
            _agents[index].Travelling = false;
            ReleasePath(index);
            StandStill(index);

            if (!_agents[index].Stranded)
            {
                _agents[index].Stranded = true;
                _agents[index].StrandedSinceTick = _clock.Tick;
                _strandedCount++;
            }

            int delay = retry && _retryDelay[index] > 0
                ? _retryDelay[index] * 2
                : FirstRetryTicks;
            if (delay > LongestRetryTicks) delay = LongestRetryTicks;

            _retryDelay[index] = delay;
            _retryAtTick[index] = _clock.Tick + delay;
        }

        private void Advance(int index, Citizen citizen, float dt)
        {
            var path = _paths[index];
            if (path == null || _agents[index].PathIndex >= path.Count)
            {
                Arrive(index);
                return;
            }

            _agents[index].PreviousPosition = _agents[index].Position;

            var pos = _agents[index].Position;
            var target = Vec2.CentreOf(path[_agents[index].PathIndex]);

            // Terrain slows people down: the road is quick, a ploughed field is not.
            var here = pos.ToTile();
            float cost = World.Grid.InBounds(here) ? World.Grid.MoveCost(here.X, here.Y) : 1.3f;
            if (cost > 100f) cost = 1.3f;

            // Somebody walking with a companion keeps their pace, or the two would separate
            // over a few hundred metres and the whole effect would quietly come apart.
            var pace = citizen;
            if (_agents[index].WalkingWith.IsValid)
            {
                var leader = People.Get(_agents[index].WalkingWith);
                if (leader != null) pace = leader;
            }
            float speed = pace.BaseSpeedIn(_clock.Year) / cost;

            var delta = target - pos;
            float distSq = delta.LengthSquared;
            float step = speed * dt;

            if (distSq <= step * step)
            {
                _agents[index].Position = target;
                _agents[index].PathIndex++;
                int next = _agents[index].PathIndex;

                // About to cross a threshold, in or out: a hand on the handle BEFORE going
                // through rather than a stumble after. Looking one step ahead rather than one
                // step behind is also what makes it work on the first step of a journey, where
                // there is no step behind — and it fires exactly once per doorway, because the
                // tile on the far side is the only neighbour whose inside-ness differs.
                if (next < path.Count && CrossesThreshold(path[next - 1], path[next]))
                {
                    BeginDoorPause(index);
                    return;
                }

                if (next >= path.Count) Arrive(index);
            }
            else
            {
                // Normalise without a square root on the hot path where we can avoid it.
                float dist = Sqrt(distSq);
                _agents[index].Position = pos + delta * (step / dist);
                _agents[index].Heading = new Vec2(delta.X / dist, delta.Y / dist);
            }
        }

        private void Arrive(int index)
        {
            _agents[index].Travelling = false;
            ReleasePath(index);
            _agents[index].At = _agents[index].Destination;
            _agents[index].WalkingWith = CitizenId.None;
            StandStill(index);
            _agents[index].Carrying = false;
            _agents[index].TalkTicks = 0;
            _agents[index].TalkingTo = CitizenId.None;
            _agents[index].Doing = _agents[index].Responding
                ? Activity.Responding
                : _plans[index].At(_clock.MinuteOfDay).What;
        }

        // ---- doorways ----

        /// <summary>Does stepping from one tile to the other take you in or out of a building?</summary>
        private bool CrossesThreshold(Tile left, Tile entered)
        {
            if (left == entered) return false;
            bool wasIn = (World.Grid.FlagsAt(left) & TileFlags.Indoor) != 0;
            bool nowIn = (World.Grid.FlagsAt(entered) & TileFlags.Indoor) != 0;
            return wasIn != nowIn;
        }

        /// <summary>
        /// Six to eleven ticks — three to five and a half tenths of a second — for everybody,
        /// plus a second, separately-purposed draw for a citizen holding <see cref="Beat.Lingers"/>
        /// (see the <see cref="LingerPurpose"/>/<see cref="LingerBase"/>/<see cref="LingerSpread"/>
        /// block above, where the full rationale lives).
        ///
        /// Jittered rather than fixed because a family filing through their own front door in
        /// perfect lockstep reads as a stutter in the simulation. Varied, it reads as four
        /// people going through a door.
        /// </summary>
        private void BeginDoorPause(int index)
        {
            var who = People.Get(new CitizenId(index));
            ulong key = who != null ? who.Key.Value : (ulong)index + 1UL;

            int ticks = 6 + Rolls.Int(Seed, DoorPurpose, key, _clock.Tick, 0, 6);

            // The sentence in the inspector and the figure still on the step are now one fact.
            if (who != null && (who.Beats & Beat.Lingers) != 0)
                ticks += LingerBase + Rolls.Int(Seed, LingerPurpose, key, _clock.Tick, 0, LingerSpread);

            _agents[index].DoorPauseTicks = ticks;
            _agents[index].PreviousPosition = _agents[index].Position;
            StandStill(index);
        }

        // ---- queues ----

        private CounterQueue CounterQueueAt(PlaceId place) =>
            place.IsValid && place.Value < _counters.Length ? _counters[place.Value] : null;

        /// <summary>Is this person going somewhere to be served, rather than to work there?</summary>
        private static bool WillQueue(Citizen citizen, Block block) =>
            citizen.Work != block.Where &&
            (block.What == Activity.Shopping || block.What == Activity.Visiting);

        /// <summary>
        /// The tile a journey aims at.
        ///
        /// A customer walks to the DOOR of a place with a counter, not to a spot inside it. The
        /// queue is fed from the doorway, and coming in any other way would mean crossing the
        /// floor diagonally through whoever is already standing there.
        /// </summary>
        private Tile TargetIn(Block block, Citizen citizen)
        {
            if (WillQueue(citizen, block))
            {
                var line = World.CounterAt(block.Where);
                if (line != null && line.Count > 0) return line.Entry;
            }
            return SpotIn(block.Where, citizen);
        }

        /// <summary>Take the first free position, which — because the line is kept packed — is its back.</summary>
        private void JoinCounter(int index, Citizen citizen, Block block)
        {
            if (_agents[index].At != block.Where) return;
            if (!WillQueue(citizen, block)) return;

            var queue = CounterQueueAt(block.Where);
            if (queue == null) return;

            // Only from the doorway, or from somewhere already on the line. Anybody standing
            // elsewhere in the building — the person who has just been served and stepped
            // aside, most of all — would have to cross the floor to reach the back, and the
            // line is the only route in here we know is walkable without asking the pathfinder.
            var here = _agents[index].Position.ToTile();
            if (!queue.Line.Claims(here)) return;

            // Failing here means the whole village is in the shop at once.
            if (queue.TryJoin(citizen.Id, out int slot)) _agents[index].QueueSlot = slot;
        }

        /// <summary>
        /// Move up the line, one standing position at a time.
        ///
        /// Never a straight run to the reserved position: consecutive positions are orthogonally
        /// adjacent, so stepping between two of them cannot clip the corner of a doorway,
        /// whereas cutting across three at once could — and three at once is exactly what
        /// happens when the front of the queue clears.
        /// </summary>
        private void ShuffleUp(int index, Citizen citizen, float dt)
        {
            var queue = CounterQueueAt(_agents[index].At);
            if (queue == null) { _agents[index].QueueSlot = -1; return; }

            var line = queue.Line;
            int slot = _agents[index].QueueSlot;

            // Further back than the floor has room for: wait by the door until one frees up.
            if (slot >= line.Count) { StandStill(index); return; }

            // Off the line entirely means standing in the doorway, which is one step behind its
            // back: the door is what the line was built out from.
            int here = line.IndexOf(_agents[index].Position.ToTile());
            if (here < 0) here = line.Count;

            // Never walk into the back of the person in front, even if their place is free.
            int furthest = slot;
            if (slot > 0)
            {
                var ahead = queue.Holders[slot - 1];
                if (ahead.IsValid)
                {
                    int aheadAt = line.IndexOf(_agents[ahead.Value].Position.ToTile());
                    if (aheadAt < 0) aheadAt = line.Count;
                    if (aheadAt + 1 > furthest) furthest = aheadAt + 1;
                }
            }
            if (furthest >= line.Count) { StandStill(index); return; }

            int next = here > furthest ? here - 1 : furthest;

            var pos = _agents[index].Position;
            var target = Vec2.CentreOf(line.Slots[next]);
            var delta = target - pos;
            float distSq = delta.LengthSquared;

            if (distSq <= 0.0001f) { StandStill(index); return; }

            var tile = pos.ToTile();
            float cost = World.Grid.InBounds(tile) ? World.Grid.MoveCost(tile.X, tile.Y) : 1.3f;
            if (cost > 100f) cost = 1.3f;
            float step = citizen.BaseSpeedIn(_clock.Year) / cost * dt;

            _agents[index].PreviousPosition = pos;

            if (distSq <= step * step)
            {
                _agents[index].Position = target;
                if (next == slot) StandStill(index);
            }
            else
            {
                float dist = Sqrt(distSq);
                _agents[index].Position = pos + delta * (step / dist);
                _agents[index].Heading = new Vec2(delta.X / dist, delta.Y / dist);
            }
        }

        /// <summary>
        /// Close the line up, then deal with whoever is at the front of it.
        ///
        /// Compaction is what makes everybody move up when the person being served leaves, and
        /// doing it as one stable pass preserves the order they arrived in — which is the whole
        /// social contract of a queue.
        /// </summary>
        private void ServeCounters()
        {
            for (int p = 0; p < _counters.Length; p++)
            {
                var queue = _counters[p];
                if (queue == null) continue;

                var line = queue.Line;
                CloseUp(queue);

                var head = queue.Holders[0];
                if (!head.IsValid)
                {
                    queue.Serving = CitizenId.None;
                    queue.ServiceTicks = 0;
                    continue;
                }

                if (queue.Serving != head)
                {
                    // Being served does not start until they are actually at the counter.
                    if (_agents[head.Value].Position.ToTile() != line.Counter) continue;

                    var place = World.GetPlace(line.Place);
                    queue.Serving = head;
                    queue.ServiceTicks = ServiceTicksFor(place != null ? place.Kind : PlaceKind.Shop,
                                                         head);
                    continue;
                }

                if (queue.ServiceTicks > 0) { queue.ServiceTicks--; continue; }

                // Done — but the place is only given up once they have somewhere to walk to, or
                // the next person would step onto a tile that is still occupied.
                if (!StepAsideFromCounter(head.Value)) continue;

                _agents[head.Value].QueueSlot = -1;
                queue.Holders[0] = CitizenId.None;
                queue.Serving = CitizenId.None;
                queue.ServiceTicks = 0;

                // Close up again straight away. Left until the next tick, the gap at the front
                // is a free place for the length of a tick — and somebody walking in through
                // the door in that tick takes it and goes straight to the counter past four
                // people who were already waiting.
                CloseUp(queue);
            }
        }

        /// <summary>
        /// Drop anyone who is no longer really in the line, then pack what is left forward.
        ///
        /// One stable pass: it moves everybody up when the person in front leaves, and it keeps
        /// them in the order they arrived, which is the whole social contract of a queue. A
        /// place is only really held by somebody still here who still thinks they are in it —
        /// anything else is a leftover, and a leftover blocks the line for the rest of the day.
        /// </summary>
        private void CloseUp(CounterQueue queue)
        {
            int write = 0;
            for (int read = 0; read < queue.Waiting; read++)
            {
                var who = queue.Holders[read];
                queue.Holders[read] = CitizenId.None;
                if (!who.IsValid) continue;

                if (_agents[who.Value].QueueSlot != read) continue;
                if (_agents[who.Value].At != queue.Line.Place) continue;

                queue.Holders[write] = who;
                _agents[who.Value].QueueSlot = write;
                write++;
            }
            queue.Waiting = write;
        }

        /// <summary>
        /// How long one person takes at the counter, in minutes.
        ///
        /// Minutes rather than seconds, and that is the number the whole feature turns on. A
        /// visit to the shop is twenty-five minutes; at forty seconds a head the counter is
        /// free almost always, nobody ever waits, and the queue that was built never appears.
        /// At five to eleven minutes the counter is the reason you are in the shop, so anybody
        /// else who comes in while you are there is behind you — which is the thing worth
        /// looking at.
        ///
        /// Being served is not the whole visit either: what is left of it is spent out of the
        /// way, which is what keeps the line moving instead of one person owning the counter
        /// until their business hours run out.
        /// </summary>
        private int ServiceTicksFor(PlaceKind kind, CitizenId served)
        {
            var who = People.Get(served);
            ulong key = who != null ? who.Key.Value : (ulong)served.Value + 1UL;

            // Forms and the pension book take longer than a loaf; being looked at by a doctor
            // takes longer again. The two numbers are the `service` column of kinds.txt.
            var row = PlaceKindTable.Current.Row(kind);
            int minutes = row.ServiceMinutes
                        + Rolls.Int(Seed, QueuePurpose, key, _clock.Tick, 0, row.ServiceSpread);
            return minutes * GameClock.TicksPerMinute;
        }

        /// <summary>
        /// Served, and out of the way. A short walk across the same room rather than a journey:
        /// they keep their destination and their activity, so the UI still says they are in the
        /// shop, because they are.
        /// </summary>
        private bool StepAsideFromCounter(int index)
        {
            var citizen = People.Get(new CitizenId(index));
            if (citizen == null) return true;

            var place = _agents[index].At;
            var to = SpotIn(place, citizen);
            var from = _agents[index].Position.ToTile();
            if (from == to) { StandStill(index); return true; }

            if (!CanAffordAPath()) return false;

            _scratchPath.Clear();
            var outcome = _pathfinder.FindPath(from, to, _scratchPath);
            _pathNodesThisTick += _pathfinder.LastNodesExamined;
            Record(outcome, from, to);

            if (outcome != PathOutcome.Found || _scratchPath.Count == 0) return false;
            _pathsThisTick++;

            var route = RentPath(index);
            route.Clear();
            route.AddRange(_scratchPath);
            _agents[index].PathIndex = 0;
            _agents[index].Travelling = true;
            return true;
        }

        /// <summary>
        /// A deterministic spot inside a place for this particular person, so a family does not
        /// stack on one tile and the pub does not look like a single dot.
        ///
        /// Off the citizen's KEY, not their array slot: a village that has lost somebody should
        /// not find everybody else standing somewhere different in the pub that evening.
        /// </summary>
        private Tile SpotIn(PlaceId placeId, Citizen who)
        {
            var place = World.GetPlace(placeId);
            if (place == null) return new Tile(World.Width / 2, World.Height / 2);

            var b = place.Bounds;
            int area = b.W * b.H;
            if (area <= 0) return place.Door;

            // The queue's own tiles are not spots. Otherwise the shopkeeper spends the day
            // standing third in line for her own counter.
            var line = World.CounterAt(placeId);

            // Walk the footprint from a per-person offset and take the first walkable tile.
            int offset = (int)(Rolls.Avalanche(who.Key.Value) % (uint)area);
            for (int n = 0; n < area; n++)
            {
                int k = (offset + n) % area;
                int x = b.X + k % b.W;
                int y = b.Y + k / b.W;
                if (!World.Grid.IsWalkable(x, y)) continue;
                if (line != null && line.Claims(new Tile(x, y))) continue;
                return new Tile(x, y);
            }
            return place.Door.IsValid ? place.Door : b.Centre;
        }

        /// <summary>Local square root so Core never depends on System.Math's floating-point
        /// behaviour, which is not guaranteed identical across runtimes.</summary>
        private static float Sqrt(float v)
        {
            if (v <= 0f) return 0f;
            float x = v;
            float last = 0f;
            for (int i = 0; i < 12 && x != last; i++)
            {
                last = x;
                x = 0.5f * (x + v / x);
            }
            return x;
        }
    }
}
