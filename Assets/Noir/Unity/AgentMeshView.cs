using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

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
        private IAgentBody[] _figures;
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

            // Who wears the navy: the precinct's roster, resolved once — WhoIsOnDuty's own
            // resolution. A town whose kind table declares no precinct simply uniforms nobody.
            var officers = new HashSet<int>();
            if (PlaceKindTable.IsInstalled
                && PlaceKindTable.Current.TryKindOf("precinct", out PlaceKind precinctKind))
            {
                var stations = host.World.PlacesOfKind(precinctKind);
                if (stations != null && stations.Count > 0)
                    foreach (var officer in host.People.WorkersAt(stations[0]))
                        officers.Add(officer.Value);
            }

            int n = host.People.Count, rigged = 0;
            _figures = new IAgentBody[n];
            _looks = new AgentLook[n];
            _lastPosition = new Vector2[n];
            _phase = new float[n];
            _swing = new float[n];
            _yaw = new float[n];

            for (int i = 0; i < n; i++)
            {
                var citizen = host.People.Get(new CitizenId(i));
                var look = AgentLook.Of(citizen, VillageHost.Year);

                _looks[i] = look;

                // A BOUGHT PERSON IF THERE IS ONE, and the primitives if there is not. The pack
                // has about twenty figures in register for an ordinary town, so the fallback is
                // not dead code - it is what a map whose people the pack cannot cast still looks
                // like, and it is what Rossville has always looked like.
                IAgentBody body = null;
#if UNITY_EDITOR
                body = AgentBody.Build(transform, citizen, look, uniformed: officers.Contains(i));
                if (body != null) rigged++;
#endif
                _figures[i] = body ?? AgentFigure.Build(transform, citizen.FullName, look, _block);

                // Everybody starts somewhere different in the stride, or the first street you
                // watch is a marching band.
                _phase[i] = look.Phase;

                if (host.Sim != null)
                {
                    var at = host.Sim.GetAgent(i).Position;
                    _lastPosition[i] = new Vector2(at.X, at.Y);
                }
            }

            // THE TOWN ALREADY KNEW IT HAD DRAWN NOBODY, AND SAID SO IN A WHISPER.
            //
            // `rigged` is zero in every shipped build - `AgentBody` reads the figure prefabs
            // through `AssetDatabase`, which does not exist outside the editor - and zero on a
            // fresh clone, where `Assets/polyperfect` is gitignored and simply is not there. Both
            // came out as a `Debug.Log` reading "0 of them bought and animated", which is a
            // sentence somebody has to already suspect something to notice.
            //
            // SPLIT ON `Application.isEditor`, and the split is not tidiness. Told flatly that
            // "outside the editor this is expected", the fresh-clone case would be actively
            // misinformed: it IS in the editor, and its cause is the missing pack. A warning that
            // confidently misattributes is worse than the quiet log it replaces.
            Debug.Log($"People: {n} figures, {rigged} of them bought and animated, "
                    + $"{(n - rigged) * AgentFigure.PartCount} primitive parts.");

            if (rigged == 0 && n > 0)
            {
                if (Application.isEditor)
                    Debug.LogWarning($"[people] all {n} of Rossville is PRIMITIVE CAPSULES - not "
                        + "one bought figure was found. In the editor that means the pack is "
                        + "missing: Assets/polyperfect is gitignored, so a fresh clone has none of "
                        + "it. Re-import it, then run Noir > Make City Meshes Readable.");
                else
                    Debug.LogWarning($"[people] all {n} of Rossville is PRIMITIVE CAPSULES, which "
                        + "is what a shipped build gets today: AgentBody reads the figure prefabs "
                        + "through AssetDatabase, and there is no AssetDatabase outside the "
                        + "editor. See docs/ANIMATION-FIXES.md PB-6/PB-7 - the cast manifest.");
            }
        }

        /// <summary>
        /// How many people are animating at once, whatever the camera is doing.
        ///
        /// A FIXED RADIUS WAS TRIED FIRST AND IT SWITCHED THE TOWN OFF. `OrbitCamera` opens in
        /// Overview at 330 m and goes to 1600 m, so an eighty-metre cull left **0 of 1,385**
        /// animating - which measures beautifully (12.0 ms to 1.4 ms) and is not an optimisation,
        /// it is a static town. It also took `WhyAreThePeopleNotAnimating` red, correctly: with
        /// nobody animating, the walk rate is 0.00x and the test that guards against skating feet
        /// has nothing left to measure.
        ///
        /// So the budget is a HEAD COUNT and the radius chases it. Cost is bounded and predictable
        /// whatever the view, the nearest people are always the ones moving, and the town is never
        /// completely still. At the measured ~4.5 us per animator, 150 is about 0.7 ms against the
        /// 6.2 ms all 1,385 were costing.
        /// </summary>
        public const int AnimatingBudget = 150;

        /// <summary>Where the radius starts, and the ends it may not pass. Metres.</summary>
        private const float NearestFloor = 15f, NearestCeiling = 2500f;

        /// <summary>
        /// The radius that currently delivers <see cref="AnimatingBudget"/>, chased frame by frame.
        ///
        /// A feedback loop rather than a sort: ordering 1,385 people by distance every frame to
        /// take the nearest 150 costs more than it saves, and this converges in well under a
        /// second and then sits still. Overshoot does not matter - being 20 short or 20 over for a
        /// few frames is invisible and costs microseconds.
        /// </summary>
        private float _nearest = 80f;

        /// <summary>How many figures had a running animator on the last <see cref="Refresh"/>.</summary>
        public int Animating { get; private set; }

        public void Refresh()
        {
            var sim = _host.Sim;
            if (sim == null) return;

            float dt = Time.unscaledDeltaTime;

            // Simulation steps to a real second, so a per-tick distance becomes a real speed.
            // Paused it is zero, which stops the walk dead rather than leaving it treadmilling.
            float perSecond = GameClock.TicksPerSecond
                            * VillageHost.Speeds[Mathf.Clamp(_host.SpeedIndex, 0,
                                                             VillageHost.Speeds.Length - 1)];

            // Paused, the stride is left exactly where it was caught. Legs folding themselves
            // back to attention the moment you hit space would say "the simulation stopped",
            // which is not the same thing as "this man has stopped walking".
            bool running = VillageHost.Speeds[
                Mathf.Clamp(_host.SpeedIndex, 0, VillageHost.Speeds.Length - 1)] > 0f;

            Reselect(_host.Selected.Value);

            // Where the eye is, fetched once rather than 1,385 times. Null when there is no camera
            // at all - an offline render between shots - and then nobody is culled, because a still
            // that quietly froze half the town would be a worse lie than a slow one.
            var camera = Camera.main;
            Vector3? eye = camera != null ? camera.transform.position : (Vector3?)null;
            int animating = 0;

            for (int i = 0; i < _figures.Length; i++)
            {
                var agent = sim.GetAgent(i);

                // OUT OF TOWN, SO NOT DRAWN. The men who work in Hoopeston or Danville are
                // anchored at their own front door in the simulation - every consumer of a
                // block's place assumes a real one - and simply not rendered between half six
                // and ten past five. An empty pavement on a Tuesday morning is the point.
                //
                // Toggling the root is safe here where it would not be elsewhere: AgentFigure is
                // a plain class, not a MonoBehaviour, and is posed from this loop every frame, so
                // there is no Update to lose. That distinction is exactly what the frozen-traffic
                // bug turned on.
                bool away = agent.Doing == Activity.AwayFromTown;
                var root = _figures[i].Root;
                if (root != null && root.gameObject.activeSelf == away)
                    root.gameObject.SetActive(!away);
                if (away) continue;

                // A BODY, NOT A PERSON. Ordered after the away-toggle rather than before it,
                // and that ordering never fights: Downed and AwayFromTown are different
                // Activity values, so a downed figure always has `away == false` and the
                // toggle above has already made sure its root is active - exactly what a body
                // left in the street needs.
                //
                // No lying clip exists in the set (checked 2026-08-15, pack included), so the
                // figure is laid flat procedurally instead. THE FREEZE IS DONE RIGHT HERE, BY
                // ZEROING THE ANIMATOR'S SPEED - not by skipping the Drive call below. Skipping
                // Drive only stops this loop from asking for a NEW clip; Unity keeps advancing
                // whichever clip was already playing on its own, so without this a victim caught
                // mid-stride would keep cycling their walk animation lying flat on their back.
                // The missing-state fallback (see Report's Stateless counter) freezes a figure by
                // a completely different route - a wanted clip with no matching controller state
                // - and never runs here at all, since it lives downstream of the very Drive call
                // this branch skips. Works identically for the primitive fallback bodies, which
                // have no Animator to begin with (null-guarded below) and were never driven
                // regardless. Replace with a real clip via the 'downed' row in animations.txt
                // when one is imported.
                if (agent.Doing == Activity.Downed)
                {
                    if (root != null) root.localRotation = Quaternion.Euler(90f, _yaw[i], 0f);
                    if (_figures[i].Animator != null) _figures[i].Animator.speed = 0f;
                    continue;   // past the pose/Drive calls for this figure
                }

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

                // ---- THE ANIMATOR LOD, WHICH IS HALF THE FRAME ----
                //
                // Measured by PerfCensus.WhatIsEatingTheFrame: switching the People layer off took
                // the frame from 12.0 ms to 5.9 ms. The 1,385 animators cost MORE THAN EVERYTHING
                // ELSE IN THE TOWN PUT TOGETHER - the traffic is 1.0 ms and the 611 parked cars
                // measure zero.
                //
                // NOT AnimatorCullingMode.CullCompletely, which was tried and reverted and whose
                // failure is written up at AgentBody.cs:204: a disabled animator stops updating the
                // bounds that decide visibility, and the cull keys on that visibility, so anybody
                // who leaves the view never comes back. Forty of forty froze.
                //
                // THAT FAILURE IS CIRCULAR, AND THIS IS NOT. A camera position and a simulated
                // position are both known whatever the animator is doing, so a DISTANCE cull has no
                // state to get stuck in. Nothing here can wedge: the test that decides re-enabling
                // does not depend on the thing being disabled.
                //
                // THE FIGURE IS STILL DRAWN. Only `enabled` goes, not the renderer and not the
                // root - a distant person keeps standing there in the pose they were caught in,
                // which at eighty metres is not something an eye can pick out. Switching the root
                // off instead would pop people out of a view they are plainly inside.
                var animator = _figures[i].Animator;
                if (animator != null && eye.HasValue)
                {
                    // Hysteresis: a person walking the boundary must not flicker on and off every
                    // few frames, so it costs a tenth of the radius to change your mind.
                    float far = (ground - eye.Value).sqrMagnitude;
                    float edge = animator.enabled ? _nearest * 1.1f : _nearest;
                    bool animate = far < edge * edge;

                    if (animator.enabled != animate) animator.enabled = animate;
                    if (!animate) continue;      // and skip Drive, which is not free either
                    animating++;
                }

                // WHERE THE BOUGHT ANIMATION WILL LAND. The primitive figure above swings its own
                // legs off `_phase` and needs none of this; a rigged character does, and the
                // simulation state it needs - what this person is doing, and whether they are on
                // the move - is only assembled here. So the call is made now, against whatever
                // Animator a figure may or may not have, and AgentAnimation.Drive returns
                // immediately when there is nothing to drive. Costs a null check per person per
                // frame until the clips exist, and means importing them is a download rather than
                // a refactor. See docs/ASSETS.md.
                //
                // Running is the child at play and nothing else: an adult jogging across
                // Rossville reads as fleeing, which is a story event rather than a commute.
                //
                // The pace handed over is metres per REAL second, which is the speed the eye sees
                // the person travel at and the only speed a stride can honestly be matched to. At
                // 10x the town moves ten times faster than anybody walks, so it is deliberately
                // not the walking speed the simulation believes in - AgentAnimation clamps the
                // ratio, and beyond about twice speed the legs stop trying and say so.
                //
                // Taken from the SIMULATION's step and not from ground covered since the last
                // rendered frame, for the same reason the leg swing above is. The sim runs at a
                // fixed 20 Hz and the renderer does not: at 1x on a quick machine most frames
                // advance the clock by no ticks at all, so a per-frame delta reads zero nine
                // frames in ten and would peg the playback rate to its floor while the person is
                // visibly walking. Per tick times ticks a second times the clock's multiplier is
                // the same number, and it is the same on every machine.
                AgentAnimation.Drive(_figures[i].Animator, SituationOf(i, agent, walking),
                                     who: _host.People.Get(new CitizenId(i)).Key.Value,
                                     pace: Mathf.Sqrt((agent.Position - agent.PreviousPosition)
                                                          .LengthSquared) * perSecond,
                                     // RIG-2. The legs doing the walking, so a child's cycle runs
                                     // faster than an adult's over the same ground instead of
                                     // gliding. Already to hand - the primitive figures have used
                                     // this same number for their leg swing all along.
                                     stride: _looks[i].Stride);
            }

            Animating = animating;

            // Chase the budget. Only when there is a camera to measure from - with none, nothing
            // was culled this frame and moving the radius on that would be reacting to nothing.
            if (eye.HasValue)
            {
                if (animating < AnimatingBudget * 9 / 10) _nearest *= 1.08f;
                else if (animating > AnimatingBudget * 11 / 10) _nearest *= 0.94f;
                _nearest = Mathf.Clamp(_nearest, NearestFloor, NearestCeiling);
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
        /// What the town is doing and what it is playing, for the diagnostics.
        ///
        /// The question "do people animate" cannot be answered from outside: it needs the
        /// simulation's idea of who is on the move lined up against the Animator's idea of what
        /// they are playing, and only this class holds both. A person WALKING while their animator
        /// sits in the idle state is the whole of the gliding fault, and it is invisible in either
        /// number on its own.
        /// </summary>
        public struct Census
        {
            public int People, Animated, Moving, Wrong;

            /// <summary>
            /// How many animators are actually RUNNING, after the distance LOD in
            /// <see cref="Refresh"/> has switched off everybody past
            /// <see cref="AnimatingBudget"/> has kept only the nearest people running.
            ///
            /// Reported because without it the saving cannot be read. "Switching the People layer
            /// off now costs 0.0 ms" means the LOD is working if a few dozen are still running,
            /// and means the town has quietly been turned off if the answer is none.
            /// </summary>
            public int Running;

            /// <summary>
            /// The rate the walk is playing at, averaged over everybody on the move.
            ///
            /// The AVERAGE and not the extremes, because a person whose last simulation step
            /// happened to land on zero - one who has just been given a path, or who is a frame
            /// from stopping - reads as 0.00x for that frame and is not a fault. One such person
            /// ruins a minimum and moves an average of forty by nothing.
            /// </summary>
            public float Rate;
            public float Slowest, Fastest;
            public string Wanted;

            /// <summary>Out of town and not drawn. See Report - they are skipped, not counted.</summary>
            public int Away;

            /// <summary>Downed and not counted - see Report. A frozen body's animator is left
            /// wherever Refresh's own zero-speed freeze caught it (mid-stride, most likely), not
            /// in the "downed" clip this census would otherwise go looking for, so counting it
            /// here would either inflate Stateless or, worse, silently pass as "right" for a
            /// question the frozen pose was never trying to answer.</summary>
            public int Downed;

            /// <summary>
            /// People whose wanted clip has NO state in the controller, so Drive freezes them.
            ///
            /// A counter and not yet an assert: animations.txt is allowed to name a clip nobody
            /// has downloaded, which is the whole "import a few at a time" workflow.
            /// </summary>
            public int Stateless;

            public override string ToString() =>
                $"{People} people, {Animated} with an animator, {Running} animating "
              + $"(nearest {AnimatingBudget}, the rest hold a pose), "
              + $"{Moving} on the move, "
              + $"{Wrong} of those NOT in the state they should be. "
              + $"{Away} out of town and not drawn. "
              + $"{Downed} down and not counted toward the clip census. "
              + (Stateless > 0 ? $"{Stateless} WANT A CLIP THE CONTROLLER HAS NO STATE FOR. " : "")
              + (Moving > 0 ? $"Walk playing at {Rate:0.00}x "
                            + $"({Slowest:0.00}-{Fastest:0.00}). " : "")
              + $"Wanted: {Wanted}";
        }


        /// <summary>
        /// What the animation table is allowed to know about this person, right now.
        ///
        /// WHERE and WHO, which `Drive` used to throw away before the lookup - see
        /// AgentAnimation.Situation for the eight faults that came out of that. The place is the
        /// KIND's name from kinds.txt (`diner`, `tavern`, `farm`), not the building's own name, so
        /// a row applies to every diner in the town rather than to Dot's in particular.
        /// </summary>
        private AgentAnimation.Situation SituationOf(int i, in Noir.Core.Sim.AgentState agent, bool moving)
        {
            var citizen = _host.People.Get(new CitizenId(i));

            string place = null;
            if (agent.At.IsValid)
            {
                var at = _host.World.GetPlace(agent.At);
                if (at != null) place = PlaceKindTable.Current.Row(at.Kind).Name;
            }

            return new AgentAnimation.Situation(
                agent.Doing, moving,
                place: place,
                stage: citizen.StageIn(VillageHost.Year).ToString().ToLowerInvariant(),
                sex: citizen.Male ? "male" : "female");
        }

        public Census Report()
        {
            var sim = _host.Sim;
            if (sim == null) return new Census { Wanted = "no simulation" };

            int walking = 0, walkingAndIdle = 0, animated = 0, rated = 0;
            int away = 0, downed = 0, stateless = 0;
            float slowest = float.MaxValue, fastest = 0f, rates = 0f;
            var states = new Dictionary<string, int>();

            for (int i = 0; i < _figures.Length; i++)
            {
                var agent = sim.GetAgent(i);

                // OUT OF TOWN, SO NOT COUNTED - `Refresh` already refuses to draw these.
                //
                // About two hundred people are in Hoopeston or Danville at any weekday hour, with
                // their figure switched off entirely. They were being counted here as ordinary
                // townsfolk: they inflated `People`, they were asked what clip they wanted, and
                // their answers went into the state census - so a fifth of the town's "Wanted"
                // histogram described people nobody can see. Skipping them is not hiding a fault,
                // it is the census agreeing with the renderer about who is here.
                if (agent.Doing == Activity.AwayFromTown) { away++; continue; }

                // DOWNED, SO NOT COUNTED EITHER - the same reasoning as away, aimed at a
                // different silent miscount. `Refresh` freezes a downed figure by zeroing its
                // Animator's speed rather than asking for a new clip (see Refresh's own comment
                // on the Downed branch), so whatever state the animator was in the instant they
                // went down is what keeps showing - mid-stride, most likely - while this census
                // would still ask ClipFor for the "downed" row (Breathing Idle) and either count
                // a body that will never play it as Stateless, or, since a non-moving agent skips
                // the right/wrong state check below entirely, count it as fine when nobody ever
                // checked. Neither answer is about the fault this census exists to catch, so a
                // downed figure is skipped from the clip census exactly like an absent one.
                if (agent.Doing == Activity.Downed) { downed++; continue; }

                bool moves = agent.Heading.X != 0f || agent.Heading.Y != 0f;
                if (moves) walking++;

                var animator = _figures[i].Animator;
                if (animator == null || animator.runtimeAnimatorController == null) continue;

                // A FROZEN DISTANT FIGURE IS NOT THE GLIDING BUG THIS CENSUS HUNTS. The animator
                // LOD in Refresh keeps only the nearest AnimatingBudget people running, so a walker out
                // there really is holding a pose - deliberately, and invisibly at that range.
                // Counting them as "walking but idle" would report the optimisation as the very
                // fault it was measured to be safe against, and hide a real one behind the noise.
                if (!animator.enabled) continue;
                animated++;

                string want = AgentAnimation.ClipFor(
                    SituationOf(i, agent, moves),
                    who: _host.People.Get(new CitizenId(i)).Key.Value) ?? "(nothing)";

                states[want] = states.TryGetValue(want, out int n) ? n + 1 : 1;

                // WANTED A CLIP THE CONTROLLER HAS NO STATE FOR - counted for EVERYBODY, above the
                // moving check on purpose. Below it, only walkers could ever be seen, and the
                // dotted-clip fault's victims were people standing at doors: non-moving by
                // definition, and therefore invisible to a counter placed one line lower.
                //
                // A COUNTER AND NOT AN ASSERT IN THIS WAVE. `animations.txt` is explicitly allowed
                // to name a clip that has not been downloaded yet - that is the "import a few at a
                // time" workflow the whole system is built for - so asserting on this today would
                // turn the six-minute gate red on a case the format promises is legal. Read the
                // number first.
                if (want != "(nothing)" && !animator.HasState(0, Animator.StringToHash(want)))
                    stateless++;

                if (!moves) continue;

                // MID-CROSSFADE COUNTS AS RIGHT. While a transition runs, the current state is
                // still the one being left - so somebody who has just started walking and is a
                // frame into the quarter-second fade INTO the walk reads as being in the idle,
                // which is the opposite of the truth. Asking where they are going as well as
                // where they are turns a false alarm back into a pass.
                int wanted = Animator.StringToHash(want);
                bool right = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == wanted
                          || (animator.IsInTransition(0)
                              && animator.GetNextAnimatorStateInfo(0).shortNameHash == wanted);
                if (!right) walkingAndIdle++;

                // The rate the walk is being played at, which is the number that decides whether
                // the feet plant or skate. Read only off people who are moving: everybody standing
                // still is on 1.00x by definition and would flatten the span into meaninglessness.
                slowest = Mathf.Min(slowest, animator.speed);
                fastest = Mathf.Max(fastest, animator.speed);
                rates += animator.speed;
                rated++;
            }

            var parts = new List<string>();
            foreach (var s in states) parts.Add($"{s.Key}={s.Value}");
            parts.Sort(System.StringComparer.Ordinal);

            return new Census
            {
                People = _figures.Length, Animated = animated, Running = Animating,
                Away = away, Downed = downed, Stateless = stateless,
                Moving = walking, Wrong = walkingAndIdle,
                Rate = rated > 0 ? rates / rated : 0f,
                Slowest = slowest == float.MaxValue ? 0f : slowest, Fastest = fastest,
                Wanted = string.Join(", ", parts),
            };
        }

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
