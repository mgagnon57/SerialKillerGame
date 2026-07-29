using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;
using Noir.Unity;
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Editor
{
    /// <summary>
    /// The villagers, in a still.
    ///
    /// This exists because of a hole in the feedback loop rather than because a picture needs
    /// decorating. The snapshot renderer builds the village in edit mode with nothing running,
    /// so for a long time every offline frame was of an empty village - which meant the one
    /// part of the project that actually matters, the people, was the one part that could never
    /// be checked without a human pressing Play and describing what they saw.
    ///
    /// The whole point of this milestone is "does watching them feel alive", and you cannot
    /// answer that from a photograph of the buildings.
    ///
    /// It runs the real simulation rather than scattering figures about. Anything else would be
    /// a picture of a village that does not exist: people would stand in walls, nobody would be
    /// on the road to the mill at eight, and the pub would be empty at seven.
    /// </summary>
    public sealed class Crowd
    {
        /// <summary>
        /// The village is run forward from four in the morning, once, and the shots read off it
        /// as the clock passes them.
        ///
        /// The obvious approach - build a fresh simulation twenty minutes before each shot -
        /// does not work, and it fails in a way that looks like the renderer is broken rather
        /// than the method. A new Simulation places everyone AT wherever their day plan says
        /// they should be, so nobody is between places. Twenty minutes later the school run has
        /// already happened and the frame at half past eight, the busiest minute of the day,
        /// had exactly one person visibly out of doors instead of seventeen.
        ///
        /// Journeys have to be entered from before they start.
        /// </summary>
        private const int DayStartsAt = 4 * 60;

        private readonly WorldModel _world;
        private readonly Population _people;
        private readonly AgentFigure[] _figures;
        private readonly AgentLook[] _looks;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        public int Count => _figures.Length;

        public static Crowd Assemble(WorldModel world, Transform parent)
        {
            var names = NameTable.Parse(ContentLoader.Read("names.txt"));
            var particulars = ParticularsTable.Parse(ContentLoader.Read("particulars.txt"));
            var people = PopulationGenerator.Generate(world, names, particulars, VillageHost.Seed);

            var root = new GameObject("SnapshotCrowd");
            root.transform.SetParent(parent, false);

            var crowd = new Crowd(world, people);
            for (int i = 0; i < people.Count; i++)
            {
                var citizen = people.Get(new CitizenId(i));
                var look = AgentLook.Of(citizen);

                crowd._looks[i] = look;
                crowd._figures[i] = AgentFigure.Build(root.transform, citizen.FullName, look, crowd._block);
                crowd._figures[i].SetClothing(crowd._block, look.Top, look.Bottom);
            }

            Debug.Log($"[snap] crowd of {people.Count}.");
            return crowd;
        }

        private Crowd(WorldModel world, Population people)
        {
            _world = world;
            _people = people;
            _figures = new AgentFigure[people.Count];
            _looks = new AgentLook[people.Count];
        }

        private Simulation _sim;

        /// <summary>
        /// Put everyone where they would be at this hour.
        ///
        /// Callers should ask for hours in ascending order; the simulation is then run once
        /// across the day and each shot is read off it in passing. Asking for an earlier hour
        /// than the last one is allowed and simply starts the day again, which is correct but
        /// costs the whole run.
        /// </summary>
        public void PoseAt(float hour)
        {
            int minute = Mathf.RoundToInt(hour * 60f);

            if (_sim == null || _sim.Clock.MinuteOfDay > minute)
                _sim = new Simulation(_world, _people, VillageHost.Seed, startMinuteOfDay: DayStartsAt);

            // Bounded, because the loop condition is a clock that wraps at midnight: asked for
            // an hour the run can never reach, this would spin for ever inside a batch job with
            // nobody watching it.
            var sim = _sim;
            int guard = 24 * 60 * GameClock.TicksPerMinute;
            while (sim.Clock.MinuteOfDay < minute && guard-- > 0) sim.Tick();

            _outdoors.Clear();
            int travelling = 0, standingOutside = 0;

            for (int i = 0; i < _figures.Length; i++)
            {
                var agent = sim.GetAgent(i);
                bool walking = agent.Heading.X != 0f || agent.Heading.Y != 0f;

                // Facing, worked out the same way the live view does it. A still is actually the
                // harder case: there is no previous frame to have turned from, so it has to be
                // right first time or two people mid-conversation face the same way like a
                // pair of shop dummies.
                Vector3 forward = Vector3.zero;
                if (walking)
                    forward = Space3D.ToWorld(agent.Heading);
                else if (agent.TalkingTo.IsValid)
                    forward = Space3D.ToWorld(sim.GetAgent(agent.TalkingTo.Value).Position - agent.Position);
                else if (agent.QueueSlot >= 0 && agent.At.IsValid)
                {
                    var line = _world.CounterAt(agent.At);
                    if (line != null && line.Count > 0)
                        forward = Space3D.ToWorld(line.Counter) - Space3D.ToWorld(agent.Position, 0f);
                }

                float yaw = forward.sqrMagnitude > 0.0001f
                    ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
                    : _looks[i].Phase * Mathf.Rad2Deg;   // standing about: any stable facing will do

                // Mid-stride, not mid-air: a crowd frozen with every leg together reads as a
                // rank of statues. The starting phase already varies per person, so simply
                // holding it puts everyone at a different point in their step.
                float swing = walking ? AgentFigure.MaxSwing : 0f;

                var ground = Space3D.ToWorld(agent.Position, 0f);
                if (agent.WalkingWith.IsValid && walking)
                {
                    var heading = Space3D.ToWorld(agent.Heading);
                    ground += new Vector3(-heading.z, 0f, heading.x).normalized * 0.62f;
                }

                _figures[i].Pose(ground, yaw, _looks[i].Phase, swing, agent.Carrying);

                // Outdoors means visible, not moving. At the busiest minute of the day only two
                // people in a hundred and nine are actually walking - the other fourteen are
                // standing in the churchyard, on the green, at the allotments or at the bus
                // stop, and those are the ones a camera can see. Aiming at walkers alone found
                // a cluster of one.
                if (agent.Travelling) travelling++;
                else if (_world.Grid.TerrainAt((int)agent.Position.X, (int)agent.Position.Y) != Terrain.Floor)
                    standingOutside++;
                else continue;

                _outdoors.Add(ground);
            }

            // Logged every time, because "the street is empty" and "the renderer is not placing
            // anybody" produce identical pictures and I have now been fooled by that twice.
            Debug.Log($"[snap] {minute / 60:00}:{minute % 60:00} - {travelling} walking, "
                    + $"{standingOutside} standing outside, {_figures.Length - travelling - standingOutside} indoors.");
        }

        private readonly System.Collections.Generic.List<Vector3> _outdoors =
            new System.Collections.Generic.List<Vector3>();

        /// <summary>
        /// Where the most people are, of those actually out in the open.
        ///
        /// A fixed camera is a poor way to photograph this village. At its busiest about
        /// seventeen people in a hundred and sixteen are outdoors at once, spread over twenty
        /// thousand square metres, so any hand-placed view is overwhelmingly likely to frame an
        /// empty street and say nothing about whether the people look right - which is the one
        /// question these renders exist to answer.
        ///
        /// Aiming at the largest cluster is still deterministic: same seed, same day, same
        /// simulation, same answer every run. The camera moves between builds only when the
        /// village does.
        /// </summary>
        public bool TryFocus(out Vector3 centre, out int howMany)
        {
            centre = Vector3.zero;
            howMany = 0;
            if (_outdoors.Count == 0) return false;

            const float Together = 16f;

            for (int i = 0; i < _outdoors.Count; i++)
            {
                var sum = Vector3.zero;
                int near = 0;

                for (int j = 0; j < _outdoors.Count; j++)
                    if ((_outdoors[j] - _outdoors[i]).sqrMagnitude <= Together * Together)
                    {
                        sum += _outdoors[j];
                        near++;
                    }

                if (near <= howMany) continue;
                howMany = near;
                centre = sum / near;
            }

            return howMany > 0;
        }
    }
}
