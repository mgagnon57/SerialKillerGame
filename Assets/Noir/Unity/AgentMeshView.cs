using UnityEngine;
using Noir.Core.Contracts;

namespace Noir.Unity
{
    /// <summary>
    /// The people.
    ///
    /// Each villager is an AgentFigure - a body, a head, two legs and two arms built from Unity
    /// primitives, which arrive lit, shadow-casting and shadow-receiving with no work. This
    /// class owns none of that shape: it decides where everybody is, which way they are facing
    /// and how far into a stride they are, and hands those three numbers to the figure.
    ///
    /// Everything that varies per person - height, build, clothing - is decided once at build
    /// from a hash of the citizen id, so nothing here has to be recomputed and nothing here
    /// allocates. Colour goes through one shared MaterialPropertyBlock, which keeps all of them
    /// on a single instanced material.
    /// </summary>
    public sealed class AgentMeshView : MonoBehaviour
    {
        private VillageHost _host;
        private AgentFigure[] _figures;
        private AgentLook[] _looks;
        private MaterialPropertyBlock _block;

        // Per-person animation state. Parallel arrays rather than fields on the figure, because
        // this is the renderer's business and the figure is meant to be swappable.
        private Vector2[] _lastPosition;
        private float[] _phase;
        private float[] _swing;
        private float[] _yaw;

        /// <summary>Who is currently wearing the selection colour, so it is repainted once, not every frame.</summary>
        private int _tinted = -1;

        /// <summary>Degrees a second. Fast enough that a corner is turned, slow enough to be a turn.</summary>
        private const float TurnRate = 540f;

        /// <summary>Degrees a second the leg swing grows and fades. Stopping settles in a quarter second.</summary>
        private const float SettleRate = 120f;

        private const float TwoPi = Mathf.PI * 2f;

        public static AgentMeshView Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("People");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<AgentMeshView>();
            view.Build(host);
            return view;
        }

        private void Build(VillageHost host)
        {
            _host = host;
            _block = new MaterialPropertyBlock();

            int n = host.People.Count;
            _figures = new AgentFigure[n];
            _looks = new AgentLook[n];
            _lastPosition = new Vector2[n];
            _phase = new float[n];
            _swing = new float[n];
            _yaw = new float[n];

            for (int i = 0; i < n; i++)
            {
                var citizen = host.People.Get(new CitizenId(i));
                var look = AgentLook.Of(citizen);

                _looks[i] = look;
                _figures[i] = AgentFigure.Build(transform, citizen.FullName, look, _block);

                // Everybody starts somewhere different in the stride, or the first street you
                // watch is a marching band.
                _phase[i] = look.Phase;

                if (host.Sim != null)
                {
                    var at = host.Sim.GetAgent(i).Position;
                    _lastPosition[i] = new Vector2(at.X, at.Y);
                }
            }

            Debug.Log($"People: {n} figures, {n * AgentFigure.PartCount} parts.");
        }

        public void Refresh()
        {
            var sim = _host.Sim;
            if (sim == null) return;

            float dt = Time.unscaledDeltaTime;

            // Paused, the stride is left exactly where it was caught. Legs folding themselves
            // back to attention the moment you hit space would say "the simulation stopped",
            // which is not the same thing as "this man has stopped walking".
            bool running = VillageHost.Speeds[
                Mathf.Clamp(_host.SpeedIndex, 0, VillageHost.Speeds.Length - 1)] > 0f;

            Reselect(_host.Selected.Value);

            for (int i = 0; i < _figures.Length; i++)
            {
                var agent = sim.GetAgent(i);
                bool walking = agent.Heading.X != 0f || agent.Heading.Y != 0f;

                // ---- which way they are facing ----
                //
                // Heading is zero whenever somebody is standing still, including - deliberately -
                // while they are talking, so a conversation has to be aimed from the pair rather
                // than read off the walk. Two figures turning to face each other in the street is
                // the whole of the dialogue this game ever writes.
                Vector3 forward = Vector3.zero;
                if (walking)
                {
                    forward = Space3D.ToWorld(agent.Heading);
                }
                else if (agent.TalkingTo.IsValid)
                {
                    var them = sim.GetAgent(agent.TalkingTo.Value).Position;
                    forward = Space3D.ToWorld(them - agent.Position);
                }
                else if (agent.QueueSlot >= 0 && agent.At.IsValid)
                {
                    // Face the counter, not the way you came in. Standing still zeroes Heading,
                    // so without this a queue is a line of people all pointing at whichever
                    // door they happened to walk through - which reads as a jumble rather than
                    // as a queue, and a queue that does not read as one was not worth building.
                    var line = _host.World.CounterAt(agent.At);
                    if (line != null && line.Count > 0)
                        forward = Space3D.ToWorld(line.Counter) - Space3D.ToWorld(agent.Position, 0f);
                }

                float target = forward.sqrMagnitude > 0.0001f
                    ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
                    : _yaw[i];                       // nothing to aim at: hold the last facing
                _yaw[i] = Mathf.MoveTowardsAngle(_yaw[i], target, TurnRate * dt);

                // ---- how far into a stride they are ----
                //
                // Driven by ground covered, never by Time.time. That is what makes the legs match
                // the pace at every speed the clock runs at, and what makes them stop when the
                // person does rather than when the renderer decides.
                var here = new Vector2(agent.Position.X, agent.Position.Y);
                float moved = Vector2.Distance(here, _lastPosition[i]);
                _lastPosition[i] = here;

                if (running)
                {
                    float stride = _looks[i].Stride;
                    _phase[i] = Mathf.Repeat(_phase[i] + moved / stride * TwoPi, TwoPi);

                    // The swing answers two questions and is the product of both answers, and
                    // keeping them apart is the whole of it. HOW HARD IS THIS PERSON WALKING is a
                    // fact about them, and has to come out the same on every machine. CAN THIS
                    // DISPLAY SHOW IT is a fact about the frame rate, which a renderer is
                    // entitled to know. What went wrong was letting the second answer the first:
                    // amplitude came off ground covered since the last RENDERED frame, so a
                    // 1.71 m adult at 10x swung eighteen degrees at sixty frames a second and ten
                    // at thirty, and one hitch folded the whole crowd's legs flat together and
                    // sprang them back.

                    // The first, off the SIMULATION's step. It runs at a fixed 20 Hz and a faster
                    // clock calls it more often rather than lengthening its stride, so this moves
                    // with the person's pace and with nothing else. Twenty samples to a stride at
                    // walking speed, so this factor is a guard and never fires.
                    float perTick = Mathf.Sqrt((agent.Position - agent.PreviousPosition).LengthSquared);

                    // The second, off the frame. At 300x a villager crosses ten strides between
                    // frames and the phase drawn is noise, so the legs glide instead of strobing -
                    // which is the one thing the fade was always right about.
                    //
                    // Whether they are walking at all is read from Heading rather than from
                    // distance since the last frame: at quarter speed most frames advance the
                    // clock by no ticks at all, so a per-frame test called everybody still eleven
                    // frames in twelve and settled the village's legs straight while it was
                    // visibly walking.
                    float wanted = walking
                        ? AgentFigure.MaxSwing * Sampled(stride, perTick) * Sampled(stride, moved)
                        : 0f;
                    _swing[i] = Mathf.MoveTowards(_swing[i], wanted, SettleRate * dt);
                }

                // ---- where they stand ----
                //
                // Somebody walking with a companion is nudged sideways so the two of them read as
                // a pair rather than as one dot. The offset is purely visual: both follow the same
                // path in the simulation, which is what guarantees they arrive together and never
                // walk into a wall trying to stay abreast.
                var ground = Space3D.ToWorld(agent.Position, 0f);
                if (agent.WalkingWith.IsValid && walking)
                {
                    var heading = Space3D.ToWorld(agent.Heading);
                    ground += new Vector3(-heading.z, 0f, heading.x).normalized * 0.62f;
                }

                _figures[i].Pose(ground, _yaw[i], _phase[i], _swing[i], agent.Carrying);
            }
        }

        /// <summary>
        /// How much of the leg swing survives being sampled this coarsely: all of it where a
        /// stride is comfortably resolved, none where it is not, ramping between the two.
        ///
        /// A whole stride to one sample and there is no cycle left to draw at all; by three there
        /// is enough of one to read and the swing is left completely alone. The shoulders sit
        /// this low deliberately. Two and four is the textbook answer and it is the wrong one
        /// here: at the default 10x a stride lasts three rendered frames at thirty frames a
        /// second, so two-and-four would dock a slow machine ten degrees of swing - which is the
        /// exact fault this was separated out to stop. One and three still empties at 60x, where
        /// sixty frames a second get barely a sample to the stride.
        /// </summary>
        private static float Sampled(float stride, float perSample) =>
            Mathf.Clamp01((stride / Mathf.Max(perSample, 0.0001f) - 1f) * 0.5f);

        /// <summary>
        /// Move the selection colour. Repainting is the one thing here that touches renderers, so
        /// it happens on the two people involved when the selection changes and at no other time.
        /// </summary>
        private void Reselect(int selected)
        {
            if (selected == _tinted) return;

            if (_tinted >= 0 && _tinted < _figures.Length)
                _figures[_tinted].SetClothing(_block, _looks[_tinted].Top, _looks[_tinted].Bottom);

            if (selected >= 0 && selected < _figures.Length)
                _figures[selected].SetClothing(_block, Palette.Selected, Palette.Selected);

            _tinted = selected;
        }

        /// <summary>
        /// Nearest person to a point on the ground. Used for click-to-select.
        ///
        /// Asks the simulation where people are, not the figures. Those agree only for as long
        /// as every figure is posed every frame, and the moment any of them are distance-gated
        /// or their pose deferred a click would start answering about where somebody WAS - which
        /// is the one question a murder game must never get wrong, and the least likely kind of
        /// wrong to be noticed. It also means the sideways nudge given to somebody walking with
        /// a companion, which is a lie told to the renderer, is not told to the selection.
        /// </summary>
        public CitizenId Pick(Vector3 world, float maxDistance)
        {
            var sim = _host.Sim;
            if (sim == null) return CitizenId.None;

            var best = CitizenId.None;
            float bestSq = maxDistance * maxDistance;

            for (int i = 0; i < _figures.Length; i++)
            {
                var p = Space3D.ToWorld(sim.GetAgent(i).Position);
                float dx = p.x - world.x, dz = p.z - world.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = new CitizenId(i); }
            }
            return best;
        }
    }
}
