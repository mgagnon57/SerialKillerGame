using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Response
{
    /// <summary>Where a case sits in the response, minute by minute.</summary>
    public enum CaseState : byte
    {
        Undiscovered, Alarm, OfficerEnRoute, SceneHeld, CountyEnRoute, Canvassing,
        AmbulanceEnRoute, Loading, Closed
    }

    /// <summary>
    /// What the machine wants done. The machine never does any of these itself — it has no car,
    /// no officer roster, no witness list of its own — it only says WHICH order, for WHICH case,
    /// at WHAT tile, and (where one applies) WHICH citizen. The host carries the order out and
    /// reports the result back through the arrival methods below.
    /// </summary>
    public enum OrderKind : byte
    {
        DispatchOfficer, CountyCarIn, CanvassNext, AmbulanceIn, TakeBodyAway,
        ReleaseOfficer, VehiclesLeave
    }

    /// <summary>One instruction out of the machine. See <see cref="OrderKind"/> for who Who is.</summary>
    public readonly struct CaseOrder
    {
        public readonly OrderKind Kind;
        public readonly int Case;
        public readonly Tile Scene;

        /// <summary>Witness for CanvassNext, officer for ReleaseOfficer, victim for
        /// TakeBodyAway; CitizenId.None otherwise.</summary>
        public readonly CitizenId Who;

        public CaseOrder(OrderKind kind, int caseId, Tile scene, CitizenId who)
        { Kind = kind; Case = caseId; Scene = scene; Who = who; }
    }

    /// <summary>
    /// The police response, as a minute-driven state machine: ORDERS OUT, ARRIVALS IN, and
    /// nothing else. It never picks who — not the officer, not the county car, not which
    /// citizen counts as a witness — because it cannot: <c>Noir.Core.Response.asmdef</c>
    /// references <c>Noir.Core.Contracts</c> and nothing else, so this file cannot see an
    /// AgentState, a Citizen or a WorldModel any more than the observation layer can. The host
    /// chooses who and reports the choice back in; the machine only ever reasons about minutes,
    /// tiles and the coarse identifiers it was handed.
    ///
    /// CASES ARE WORKED ONE AT A TIME, earliest-discovered first, and THE ACTIVE CASE IS STICKY:
    /// once a case is being worked it holds that slot until it Closes, no matter what turns up
    /// meanwhile or what id it has. Rossville does not have two response teams, and it does not
    /// abandon a scene it is already standing in because a body two blocks over was just found —
    /// a LATER discovery never preempts a case already in progress, even one with a lower id.
    /// That is why <see cref="BodySeen"/> records a discovery minute rather than starting the
    /// alarm clock outright: a case that had to queue reads its alarm clock from whichever is
    /// later, its own discovery or the moment the case ahead of it closes, so queued time is
    /// never silently swallowed and a case never skips its own four-minute alarm delay just
    /// because the clock happened to pass 4 minutes after discovery while some other case still
    /// held the slot.
    ///
    /// Every order is emitted exactly once, guarded per case, because <see cref="Tick"/> is called
    /// every minute of the game — sometimes several times at the same minute, sometimes with whole
    /// hours skipped when the host fast-forwards — and must be safe to call redundantly either
    /// way. Every comparison against a due minute below is therefore <c>&gt;=</c>, never
    /// <c>==</c>: a machine that only recognised the exact minute a threshold fell on would miss
    /// it the moment the host skipped past it.
    /// </summary>
    public sealed class ResponseCases
    {
        public const int AlarmDelayMinutes = 4;
        public const int CountyOffMapMinutes = 18;
        public const int CanvassMinutesPerDoor = 5;
        public const int NoWitnessSceneMinutes = 10;   // zero-canvass cases still get worked
        public const int AmbulanceOffMapMinutes = 10;
        public const int LoadingMinutes = 3;
        public const float FatalSpeed = 8f;            // m/s at impact, ~18 mph
        public const int SurvivorAwayDays = 3;

        /// <summary>One case's whole life: the facts fixed at Open, the stamps each arrival call
        /// records, the witness queue Canvassing walks, the file it is building, and the
        /// emitted-flags that keep every order to exactly one appearance.</summary>
        private sealed class Case
        {
            public CaseState State = CaseState.Undiscovered;

            public readonly CitizenId Victim;
            public readonly int HitMinute;
            public readonly Tile Scene;
            public readonly CarTone Tone;
            public readonly CarShape Shape;
            public readonly bool Fatal;

            public int DiscoveredAt = -1;
            public int ClosedAt;               // 0 until Closed

            public CitizenId Officer = CitizenId.None;
            public bool DispatchOfficerEmitted;

            public int CountyDueAt = -1;
            public bool CountyCarInEmitted;

            public int CountyArrivedAt = -1;

            public CitizenId[] Witnesses = Array.Empty<CitizenId>();
            public int WitnessIndex;
            public bool CanvassNextEmittedForIndex;
            public int NextDoorReadyAt = -1;
            public int AmbulanceDueAt = -1;     // -1 until the canvass (or its empty-list dwell)
                                                 // has actually finished
            public bool AmbulanceInEmitted;

            public int LoadingDoneAt = -1;
            public bool ClosingEmitted;

            public readonly List<string> File = new List<string>();

            public Case(CitizenId victim, int hitMinute, Tile scene, CarTone tone, CarShape shape, bool fatal)
            { Victim = victim; HitMinute = hitMinute; Scene = scene; Tone = tone; Shape = shape; Fatal = fatal; }
        }

        private readonly List<Case> _cases = new List<Case>();
        private readonly List<string> _pendingLog = new List<string>();
        private int _lastTickMinute = int.MinValue;

        /// <summary>The one case Tick is allowed to advance, or -1 when nobody is. STICKY: set
        /// only by <see cref="ActivateNextIfNeeded"/>, which refuses to touch it while it already
        /// names somebody — see the class header.</summary>
        private int _activeCaseId = -1;

        /// <summary>The close minute of whichever case was active most recently, or 0 before any
        /// case has ever closed. This is the floor a freshly-active case's alarm clock reads
        /// against — see TickAlarm.</summary>
        private int _lastActiveClosedAt;

        /// <summary>Appends a case in Undiscovered and returns its index. Two hits in one minute
        /// are two cases: nothing here folds them together.</summary>
        public int Open(CitizenId victim, int minute, Tile scene, CarTone tone, CarShape shape, bool fatal)
        {
            _cases.Add(new Case(victim, minute, scene, tone, shape, fatal));
            return _cases.Count - 1;
        }

        public int Count => _cases.Count;

        public int ClosedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cases.Count; i++)
                    if (_cases[i].State == CaseState.Closed) n++;
                return n;
            }
        }

        public CaseState StateOf(int caseId) => _cases[caseId].State;
        public CitizenId VictimOf(int caseId) => _cases[caseId].Victim;
        public Tile SceneOf(int caseId) => _cases[caseId].Scene;

        /// <summary>The hit's own absolute minute, fixed at Open — not the minute it was
        /// discovered, which can be much later.</summary>
        public int MinuteOf(int caseId) => _cases[caseId].HitMinute;

        public bool FatalOf(int caseId) => _cases[caseId].Fatal;

        /// <summary>Hit minute plus SurvivorAwayDays, or int.MaxValue when fatal — a fatal case
        /// has nobody to come back.</summary>
        public int ReturnMinuteOf(int caseId)
        {
            Case c = _cases[caseId];
            return c.Fatal ? int.MaxValue : c.HitMinute + SurvivorAwayDays * 1440;
        }

        /// <summary>CitizenId.None until OfficerDispatched has been called.</summary>
        public CitizenId OfficerOf(int caseId) => _cases[caseId].Officer;

        // ---- host reports an arrival, or that somebody found the body -----------------------

        public void BodySeen(int caseId, int minute, CitizenId discoverer)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.Undiscovered)
                throw new InvalidOperationException(
                    "case " + caseId + " was already discovered; BodySeen fires once, at the minute a passerby found it.");
            c.DiscoveredAt = minute;
            c.State = CaseState.Alarm;
            Emit(caseId, "case " + caseId + ": discovered at minute " + minute + " by citizen " + discoverer.Value);
            ActivateNextIfNeeded();
        }

        /// <summary>Records who was sent, once Tick has already moved the case to
        /// OfficerEnRoute. The machine never picks the officer itself.</summary>
        public void OfficerDispatched(int caseId, CitizenId officer)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.OfficerEnRoute)
                throw new InvalidOperationException(
                    "case " + caseId + " has no officer en route to dispatch to; it is in state " + c.State + ".");
            c.Officer = officer;
        }

        public void OfficerArrived(int caseId, int minute)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.OfficerEnRoute)
                throw new InvalidOperationException(
                    "case " + caseId + " has no officer en route; it is in state " + c.State + ".");
            c.State = CaseState.SceneHeld;
            c.CountyDueAt = minute + CountyOffMapMinutes;
            Emit(caseId, "case " + caseId + ": officer on scene at minute " + minute + ", holding for the county car");
        }

        public void CountyArrived(int caseId, int minute)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.CountyEnRoute)
                throw new InvalidOperationException(
                    "case " + caseId + " has no county car en route; it is in state " + c.State + ".");
            c.State = CaseState.Canvassing;
            c.CountyArrivedAt = minute;
            Emit(caseId, "case " + caseId + ": county car on scene at minute " + minute + ", canvass beginning");
        }

        /// <summary>The host hands over the witness list for the same minute as CountyArrived.
        /// An empty list still costs the scene NoWitnessSceneMinutes before the ambulance is
        /// sent for — the county car has to work the scene either way.</summary>
        public void CanvassBegins(int caseId, CitizenId[] witnesses)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.Canvassing)
                throw new InvalidOperationException(
                    "case " + caseId + " is not ready to canvass; it is in state " + c.State + ".");
            c.Witnesses = witnesses ?? Array.Empty<CitizenId>();
            c.WitnessIndex = 0;
            c.CanvassNextEmittedForIndex = false;
            c.AmbulanceDueAt = -1;
            c.NextDoorReadyAt = c.Witnesses.Length > 0
                ? c.CountyArrivedAt
                : c.CountyArrivedAt + NoWitnessSceneMinutes;
        }

        /// <summary>The county car finished one door. Its answer lines join the case file
        /// verbatim — this is the ONLY place a witness's own words land in it — and the next
        /// door (or the ambulance, if that was the last one) is not ready for
        /// CanvassMinutesPerDoor more minutes.
        ///
        /// The reported witness MUST be the one named by the outstanding CanvassNext order.
        /// There is no forgiving mismatch here: a mis-attributed answer sitting quietly in a case
        /// file — words from citizen 9 filed under citizen 3 — is worse than the crash this
        /// throws instead. The host is expected to wire this callback exactly once per order; a
        /// mismatch means that wiring is wrong, and that is a bug worth finding loudly, now,
        /// rather than as a testimony inconsistency months later.</summary>
        public void CountyReachedDoor(int caseId, int minute, CitizenId witness, string[] lines)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.Canvassing)
                throw new InvalidOperationException(
                    "case " + caseId + " is not canvassing; it is in state " + c.State + ".");
            if (c.WitnessIndex >= c.Witnesses.Length)
                throw new InvalidOperationException(
                    "case " + caseId + " has no outstanding CanvassNext order for anybody to have answered.");
            CitizenId expected = c.Witnesses[c.WitnessIndex];
            if (!witness.Equals(expected))
                throw new InvalidOperationException(
                    "case " + caseId + " was expecting an answer from " + expected +
                    " (the outstanding CanvassNext), not " + witness + ".");
            if (lines != null)
                for (int i = 0; i < lines.Length; i++)
                    c.File.Add("case " + caseId + ": citizen " + witness.Value + " said: " + lines[i]);
            c.WitnessIndex++;
            c.CanvassNextEmittedForIndex = false;
            c.NextDoorReadyAt = minute + CanvassMinutesPerDoor;
        }

        public void AmbulanceArrived(int caseId, int minute)
        {
            Case c = _cases[caseId];
            if (c.State != CaseState.AmbulanceEnRoute)
                throw new InvalidOperationException(
                    "case " + caseId + " has no ambulance en route; it is in state " + c.State + ".");
            c.State = CaseState.Loading;
            c.LoadingDoneAt = minute + LoadingMinutes;
            Emit(caseId, "case " + caseId + ": ambulance on scene at minute " + minute + ", loading");
        }

        // ---- the clock ------------------------------------------------------------------------

        /// <summary>Advances whichever case is active and appends any orders now due. Safe to
        /// call every minute, several times in one minute, or after skipping hours — see the
        /// class header for why every threshold check below is &gt;=.</summary>
        public void Tick(int minute, List<CaseOrder> orders)
        {
            if (minute < _lastTickMinute)
                throw new ArgumentException(
                    "Tick only runs forward. Tried minute " + minute + " after minute " + _lastTickMinute + ".",
                    nameof(minute));
            _lastTickMinute = minute;

            if (_activeCaseId < 0) return;
            int active = _activeCaseId;
            Case c = _cases[active];

            switch (c.State)
            {
                case CaseState.Alarm: TickAlarm(active, c, minute, orders); break;
                case CaseState.SceneHeld: TickSceneHeld(active, c, minute, orders); break;
                case CaseState.Canvassing: TickCanvassing(active, c, minute, orders); break;
                case CaseState.Loading: TickLoading(active, c, minute, orders); break;
                    // OfficerEnRoute, CountyEnRoute and AmbulanceEnRoute wait on a host call;
                    // Undiscovered and Closed are never the active case.
            }
        }

        /// <summary>Hands the active slot to somebody, but ONLY when nobody currently holds it —
        /// the sticky half of the rule in the class header. When the slot is free, the winner is
        /// whichever non-Closed, BodySeen case has the EARLIEST discovery minute, regardless of
        /// id; a tie goes to the lower id, which is arbitrary but at least deterministic. Called
        /// from BodySeen (in case nobody was working anything yet) and from TickLoading right
        /// after a case closes (to hand the slot on).</summary>
        private void ActivateNextIfNeeded()
        {
            if (_activeCaseId >= 0) return;

            int best = -1;
            for (int i = 0; i < _cases.Count; i++)
            {
                Case c = _cases[i];
                if (c.State == CaseState.Undiscovered || c.State == CaseState.Closed) continue;
                if (best < 0 || c.DiscoveredAt < _cases[best].DiscoveredAt) best = i;
            }
            _activeCaseId = best;
        }

        private void TickAlarm(int id, Case c, int minute, List<CaseOrder> orders)
        {
            if (c.DispatchOfficerEmitted) return;

            // The alarm clock reads from whichever is later: this case's own discovery, or the
            // moment the response team came free off whichever case held the slot before it. A
            // case that queued behind a long-running scene does not get to skip its four minutes
            // just because four minutes of clock time passed while someone else held the slot.
            int alarmStart = c.DiscoveredAt > _lastActiveClosedAt ? c.DiscoveredAt : _lastActiveClosedAt;
            if (minute < alarmStart + AlarmDelayMinutes) return;

            c.DispatchOfficerEmitted = true;
            c.State = CaseState.OfficerEnRoute;
            orders.Add(new CaseOrder(OrderKind.DispatchOfficer, id, c.Scene, CitizenId.None));
            Emit(id, "case " + id + ": alarm raised at minute " + minute + ", dispatching an officer");
        }

        private void TickSceneHeld(int id, Case c, int minute, List<CaseOrder> orders)
        {
            if (c.CountyCarInEmitted) return;
            if (minute < c.CountyDueAt) return;

            c.CountyCarInEmitted = true;
            c.State = CaseState.CountyEnRoute;
            orders.Add(new CaseOrder(OrderKind.CountyCarIn, id, c.Scene, CitizenId.None));
            Emit(id, "case " + id + ": county car sent for at minute " + minute);
        }

        private void TickCanvassing(int id, Case c, int minute, List<CaseOrder> orders)
        {
            if (c.AmbulanceDueAt < 0)
            {
                if (c.Witnesses.Length == 0)
                {
                    if (minute < c.NextDoorReadyAt) return;
                    c.AmbulanceDueAt = c.NextDoorReadyAt + AmbulanceOffMapMinutes;
                }
                else if (c.WitnessIndex < c.Witnesses.Length)
                {
                    if (c.CanvassNextEmittedForIndex) return;
                    if (minute < c.NextDoorReadyAt) return;

                    c.CanvassNextEmittedForIndex = true;
                    CitizenId witness = c.Witnesses[c.WitnessIndex];
                    orders.Add(new CaseOrder(OrderKind.CanvassNext, id, c.Scene, witness));
                    Emit(id, "case " + id + ": canvassing citizen " + witness.Value + " at minute " + minute);
                    return;   // the next witness needs its own CountyReachedDoor first
                }
                else
                {
                    if (minute < c.NextDoorReadyAt) return;
                    c.AmbulanceDueAt = c.NextDoorReadyAt + AmbulanceOffMapMinutes;
                }

                // Fall through: a fast-forwarding host can hand Tick a minute that already
                // clears the ambulance threshold too, in the same call that just computed it.
            }

            if (c.AmbulanceInEmitted) return;
            if (minute < c.AmbulanceDueAt) return;

            c.AmbulanceInEmitted = true;
            c.State = CaseState.AmbulanceEnRoute;
            orders.Add(new CaseOrder(OrderKind.AmbulanceIn, id, c.Scene, CitizenId.None));
            Emit(id, "case " + id + ": ambulance sent for at minute " + minute);
        }

        private void TickLoading(int id, Case c, int minute, List<CaseOrder> orders)
        {
            if (c.ClosingEmitted) return;
            if (minute < c.LoadingDoneAt) return;

            c.ClosingEmitted = true;
            c.State = CaseState.Closed;
            c.ClosedAt = minute;
            orders.Add(new CaseOrder(OrderKind.TakeBodyAway, id, c.Scene, c.Victim));
            orders.Add(new CaseOrder(OrderKind.VehiclesLeave, id, c.Scene, CitizenId.None));
            orders.Add(new CaseOrder(OrderKind.ReleaseOfficer, id, c.Scene, c.Officer));
            Emit(id, "case " + id + ": scene cleared at minute " + minute + ", case closed");

            // Hand the slot on. Whoever is queued longest (by discovery minute, not id) gets it;
            // their own alarm clock reads this same minute as its floor, via _lastActiveClosedAt.
            _lastActiveClosedAt = minute;
            _activeCaseId = -1;
            ActivateNextIfNeeded();
        }

        // ---- the record -----------------------------------------------------------------------

        /// <summary>Every transition line since the last drain, across every case, in the order
        /// it happened. Draining clears it — this is a queue, not a history.</summary>
        public void DrainLog(List<string> into)
        {
            for (int i = 0; i < _pendingLog.Count; i++) into.Add(_pendingLog[i]);
            _pendingLog.Clear();
        }

        /// <summary>One case's whole record: its own transition lines plus every canvass answer
        /// recorded against it, in order.</summary>
        public IReadOnlyList<string> FileOf(int caseId) => _cases[caseId].File;

        private void Emit(int caseId, string line)
        {
            _pendingLog.Add(line);
            _cases[caseId].File.Add(line);
        }
    }
}
