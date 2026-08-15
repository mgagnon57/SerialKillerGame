namespace Noir.Core.Contracts
{
    /// <summary>
    /// What can be said about a car from across a road, in the same spirit as the witness
    /// layer's person bands: TONE, NEVER COLOUR — a witness who says "blue" is guessing —
    /// and a shape wide enough to hold half the fleet. Lives in Contracts because the Unity
    /// side captures it (from the prefab, at creation) and the evidence side reports it, and
    /// neither assembly may reference the other.
    /// </summary>
    public enum CarTone : byte { Unnoticed = 0, Dark, Mid, Light }

    /// <summary>Car, pickup, van. Never a make, never a model, never a plate.</summary>
    public enum CarShape : byte { Unnoticed = 0, Car, Pickup, Van }
}
