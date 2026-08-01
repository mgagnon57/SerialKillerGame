using Noir.Core.Observation;

// ---------------------------------------------------------------------------------------------
//  THE PRODUCER SIDE OF THE FIREWALL.
//
//  Noir.Core.Observation is safe because it can see almost nothing: its asmdef names
//  Noir.Core.Contracts and stops, so a Sighting cannot hold a Citizen. This assembly is the
//  opposite. It sees the world, the population, every day plan and the player's whole track,
//  and it is safe for one reason only:
//
//      THE ONLY THING IT HANDS OUT IS A Sighting[], AND A Sighting CANNOT NAME ANYBODY.
//
//  Everything this assembly knows is narrowed through that return type. The compiler does the
//  narrowing, which is why the boundary is worth anything.
//
//  THE RULE, and it is about callers rather than about code in here:
//
//      NOTHING MAY REFERENCE Noir.Core.Witness EXCEPT THE ONE CALLER THAT ASKS IT A QUESTION.
//
//  The instant a single scope holds a Sighting[] and a DayPlan together, the narrowing is
//  decorative — whoever wrote it can just look the answer up, and will. WitnessFirewallTests
//  pins the reference list and greps for callers.
//
//  Do not add a method here that returns anything richer than a Sighting. Not "which citizen
//  was nearest", not a debug overload that returns the candidate before degradation, not a
//  bool "did anyone see him". Each is one line and each is the whole game.
// ---------------------------------------------------------------------------------------------

namespace Noir.Core.Witness
{
    public static class Recollection
    {
    }
}
