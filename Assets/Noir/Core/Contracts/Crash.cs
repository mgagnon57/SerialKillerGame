namespace Noir.Core.Contracts
{
    /// <summary>
    /// What kind of case the response machine is running. A response case has always been a
    /// person hit until now; this is the second flavor through the same machine, not a second
    /// machine — see docs/superpowers/specs/2026-08-18-car-collisions-design.md. Response
    /// CARRIES this as data, decided once by the planner; it never computes which kind a case
    /// is from anything of its own.
    /// </summary>
    public enum CaseKind : byte { PersonDown = 0, Collision = 1 }

    /// <summary>
    /// How a collision case ends, decided in Core as a pure function of the at-fault driver's
    /// own day (pub-within-3-hours, the fault coin the planner already stamped) — never rolled
    /// by Response itself, which only carries the verdict as data and emits the order it implies.
    /// </summary>
    public enum CrashVerdict : byte { LetGo = 0, Ticket = 1, Dui = 2 }
}
