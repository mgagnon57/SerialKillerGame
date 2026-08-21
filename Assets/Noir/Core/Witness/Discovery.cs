using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Who — if anyone — can see the body this minute, under EXACTLY the optics
    /// <see cref="Recollection.WhatTheySawOfEvents"/> runs testimony on.
    ///
    /// ONE OPTICS, TWO CONSUMERS. This walks the identical gate sequence as the event arm — the
    /// same interruption window, the same DayPlanner.Plan, the same TravellingTo/Asleep/
    /// invalid-Where skips, the same Sightlines call — so whoever this names as the finder is
    /// exactly somebody who could later testify to the same scene, and nobody this skips has
    /// anything to say about it either. A passer-by mid-walk does NOT discover a body, for the
    /// same reason they cannot testify to one: Recollection has no route to place them anywhere
    /// while TravellingTo, so inventing one here to let them "find" the scene would be evidence
    /// this assembly has no business manufacturing.
    ///
    /// FIRST PASS WINS, LOWEST CITIZEN INDEX — deterministic, so the same minute always names the
    /// same finder rather than whatever order a dictionary happened to enumerate.
    ///
    /// The host owns one instance per case and calls <see cref="WhoSees"/> once a sim minute for
    /// every undiscovered case. The per-day plan cache is why that stays affordable: 1,300
    /// DayPlanner.Plan calls a SCANNED MINUTE would not be free, but one per citizen a DAY is —
    /// so the cache is dropped the moment `day` moves rather than kept forever or rebuilt every
    /// call.
    /// </summary>
    public sealed class Discovery
    {
        private readonly Dictionary<int, DayPlan> _plans = new Dictionary<int, DayPlan>();
        private int _cachedDay = int.MinValue;

        /// <summary>The first citizen who can see this tile at this minute, by the SAME optics
        /// testimony runs on, or CitizenId.None. One optics, two consumers: whoever discovers
        /// the body is exactly somebody who could testify to the scene — a stationary witness
        /// at the door of the place their plan has them, in this light, at this range. A
        /// passer-by mid-walk does NOT discover it, because a passer-by cannot testify either
        /// (Recollection skips TravellingTo) — the deliberate consequence of one optics.</summary>
        public CitizenId WhoSees(WorldModel world, Population population, int day, int minuteOfDay,
                                 Tile body, ulong seed,
                                 INightWitnesses nightWitnesses = null,
                                 IInterruptions interruptions = null,
                                 ISightBlocked blocked = null)
        {
            if (day != _cachedDay)
            {
                _plans.Clear();
                _cachedDay = day;
            }

            int minute = day * Sighting.MinutesPerDay + minuteOfDay;
            var when = new GameClock(GameClock.TickAt(day, minuteOfDay));

            IReadOnlyList<Citizen> citizens = population.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Citizen who = citizens[i];

                // THE VICTIM NEVER DISCOVERS THEIR OWN BODY, and neither does anybody the
                // ambulance carried off — the identical [DownedFromMinute, BackFromMinute)
                // window Recollection's two arms both honour, checked here before the plan is
                // even fetched.
                int downedFrom = interruptions?.DownedFromMinute(who.Id) ?? int.MaxValue;
                int backFrom = interruptions?.BackFromMinute(who.Id) ?? int.MaxValue;
                if (minute >= downedFrom && minute < backFrom) continue;

                if (!_plans.TryGetValue(who.Id.Value, out DayPlan plan))
                {
                    plan = DayPlanner.Plan(world, population, who, day, seed);
                    _plans[who.Id.Value] = plan;
                }

                Block block = plan.At(minuteOfDay);
                if (block.What == Activity.TravellingTo) continue;

                bool seesWhileAsleep = nightWitnesses != null && nightWitnesses.AwakeEnough(who.Id);
                if (block.What == Activity.Asleep && !seesWhileAsleep) continue;
                if (!block.Where.IsValid) continue;

                Tile watcher = world.GetPlace(block.Where).Door;
                SightingClarity clarity = Sightlines.HowGoodALook(watcher, body, when, who);
                if (!Sightlines.SawAnythingAtAll(clarity, watcher, body, when, blocked)) continue;

                return who.Id;
            }

            return CitizenId.None;
        }
    }
}
