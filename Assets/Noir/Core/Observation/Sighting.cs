using System;
using Noir.Core.Contracts;

// ---------------------------------------------------------------------------------------------
//  STAGED, NOT LIVE. NOTHING CONSTRUCTS A Sighting — 2026-07-29.
//
//  An audit went looking for `new Sighting(` across the whole repository and found zero hits,
//  including in the tests. The same is true of PersonDescription. The running game does not
//  reference this assembly at all: no file under Assets/Noir/Unity or Assets/Noir/Editor names
//  anything in Noir.Core.Observation.
//
//  What IS live in this assembly is the OTHER half — Observed, ObservationLog, ObservedAct,
//  ObservedManner and Salience — which tools/Noir.Sim/Eyewitness.cs fills and Ratio.cs grades.
//  That half is load-bearing and measured daily. This half, the witness DESCRIPTION, is waiting
//  for the investigation layer that will consume it, and until then every band below is
//  unreachable: ApparentSex, HeightBand, BuildBand, AgeBand, ClothingTone and CarriedThing have
//  sixteen values between them that no code path can produce.
//
//  Kept rather than deleted, deliberately: the firewall below is the reason this assembly exists
//  and it constrains everything written here later, so the shape is worth having in place before
//  there is a consumer. But do not read the file's confidence as evidence that it runs.
//
// ---------------------------------------------------------------------------------------------
//  THE FIREWALL.
//
//  Noir.Core.Observation.asmdef references Noir.Core.Contracts AND NOTHING ELSE. That one short
//  list is the whole guarantee of the eventual investigation: a type in this assembly *cannot*
//  hold an AgentState, a Citizen or a WorldModel, because the compiler cannot see them. Not
//  "should not" — cannot. The reference list is load-bearing, so:
//
//      IF SOMETHING IN HERE FAILS TO COMPILE BECAUSE IT CANNOT SEE A TYPE, THAT IS THE
//      FIREWALL WORKING. DO NOT ADD THE REFERENCE. Carry a coarse, fallible description of
//      what could have been noticed instead, and resolve it — fallibly — somewhere else.
//
//  The reason: the game's central mechanic is an investigation that deduces the player from
//  their behaviour. That is only a mechanic if the detective reasons from what could have been
//  observed. The moment it can read what actually happened, the deduction is theatre and the
//  game is over. Every other layer can be fixed later; this one cannot, because by the time it
//  matters there will be fifty types in here and no way to tell which ones cheated.
//
//  ObservationFirewallTests in tools/Noir.Core.Tests guards the same rule by reflection, and
//  pins this reference list, because the parallel netstandard build merges the Core assemblies
//  and so cannot enforce it on its own.
// ---------------------------------------------------------------------------------------------

namespace Noir.Core.Observation
{
    /// <summary>
    /// Identifies whoever made an observation — the witness, not the person seen.
    ///
    /// Deliberately NOT a CitizenId, even though CitizenId is visible from here. Two reasons.
    /// A statement can come from a source that never resolves to a villager: an anonymous call,
    /// a man in the pub, a passing driver. And more importantly, if identity types were
    /// idiomatic in this assembly, a `CitizenId Subject` field would one day look normal.
    /// No identity type appears anywhere in this assembly, which makes the rule a bright line.
    ///
    /// The register that maps an observer back to a person lives outside this assembly, with
    /// the case file. Observation cannot perform that lookup, and that is the point.
    /// </summary>
    public readonly struct ObserverId : IEquatable<ObserverId>
    {
        public readonly int Value;
        public ObserverId(int value) { Value = value; }

        /// <summary>An observation with no attributable source. Rumour, effectively.</summary>
        public static readonly ObserverId None = new ObserverId(-1);
        public bool IsValid => Value >= 0;

        public bool Equals(ObserverId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ObserverId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsValid ? $"W{Value}" : "W-none";
        public static bool operator ==(ObserverId a, ObserverId b) => a.Value == b.Value;
        public static bool operator !=(ObserverId a, ObserverId b) => a.Value != b.Value;
    }

    /// <summary>
    /// How good the look was: distance, light, and how much attention was being paid.
    ///
    /// Weakest value is zero so that a default Sighting is the least useful one possible.
    /// This qualifies the whole sighting, not just the description — a witness who only
    /// glimpsed a figure is also wrong about the minute and vague about the spot.
    /// </summary>
    public enum SightingClarity : byte
    {
        Glimpsed = 0,   // a moment, at distance or in poor light
        Partial = 1,    // seen properly but briefly, or from behind
        Clear = 2,      // near, lit, and looked at
    }

    /// <summary>
    /// What one person could have noticed about another, at a place and a minute.
    ///
    /// This is a claim, not a record. Nothing here asserts that the figure existed, that the
    /// minute is right, or that the description fits anyone in particular. There is deliberately
    /// no confirmation flag, no source id and no link back into the simulation: a sighting is
    /// only ever evidence about a *possible* person, and evidence has to be able to be wrong.
    ///
    /// Note what is absent. No Activity — Activity is the simulation's word for what someone was
    /// really doing, and a witness sees a figure walking, not `Activity.GoingToWork`. No PlaceId
    /// — a witness knows a doorway they saw, not the institution's identity in the world model.
    /// Both are one reference away and both would be a cheat.
    /// </summary>
    public readonly struct Sighting
    {
        /// <summary>Minutes in a day. Derived from the clock so the two can never drift apart.</summary>
        public const int MinutesPerDay = GameClock.TicksPerDay / GameClock.TicksPerMinute;

        /// <summary>Who says so.</summary>
        public readonly ObserverId Observer;

        /// <summary>
        /// Minutes since the simulation began. Minutes, not ticks, on purpose: people remember
        /// "about half seven", and a tick would offer a fiftieth of a second of precision that
        /// no witness has and that a consumer would inevitably start trusting.
        /// </summary>
        public readonly int Minute;

        /// <summary>Where the figure was, as the observer places it. A belief, not a position.</summary>
        public readonly Tile Where;

        public readonly SightingClarity Clarity;

        /// <summary>What was noticed. Carries no identity — see <see cref="PersonDescription"/>.</summary>
        public readonly PersonDescription Description;

        public Sighting(ObserverId observer, int minute, Tile where,
                        SightingClarity clarity, PersonDescription description)
        {
            Observer = observer;
            Minute = minute;
            Where = where;
            Clarity = clarity;
            Description = description;
        }

        /// <summary>The minute stamp for a moment on the clock.</summary>
        public static int MinuteOf(GameClock clock) => clock.Day * MinutesPerDay + clock.MinuteOfDay;

        public int Day => Minute / MinutesPerDay;
        public int MinuteOfDay => Minute % MinutesPerDay;

        public override string ToString() =>
            $"{Description} at {Where}, d{Day} {MinuteOfDay / 60:00}:{MinuteOfDay % 60:00} ({Clarity}, {Observer})";
    }
}
