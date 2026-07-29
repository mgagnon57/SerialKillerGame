namespace Noir.Core.Sim
{
    /// <summary>
    /// How a path query ended.
    ///
    /// The two failures are different problems and want different answers. Nothing will ever
    /// make <see cref="NoRouteExists"/> succeed — the destination is behind a river or inside a
    /// sealed room, and asking again is wasted work. <see cref="GaveUp"/> means the search ran
    /// out of budget, which is a property of this moment rather than of the map, so the same
    /// question is worth asking again shortly. A caller that cannot tell them apart has to treat
    /// every failure as permanent, and that is how somebody ends up standing in a lane all day.
    /// </summary>
    public enum PathOutcome
    {
        Found,
        NoRouteExists,
        GaveUp,
    }
}
