using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The selection colour, and nothing else any more.
    ///
    /// THIS USED TO BE "every colour in the game, in one place" — thirteen terrain and person
    /// colours plus a For(Terrain) lookup. The 3D renderer replaced all of it: ground, walls and
    /// terrain now come from Materials3D.ForTerrain and Materials3D.Wall, and people are coloured
    /// by AgentMeshView. An audit found that of fifteen members, exactly one was ever read.
    ///
    /// The rest are gone rather than kept "in case", because a colour table that looks like the
    /// authority on the game's palette and is consulted by nothing is worse than no table at all
    /// — the next person to change a colour changes it here and sees no difference. Materials3D
    /// is the authority. Git has the old values if a 2D view ever wants them back.
    ///
    /// The reasoning that outlived the colours is worth keeping: the village is deliberately
    /// muted and close in value, because at this camera height it reads by SHAPE — the line of a
    /// street, the block of the mill — and saturated colour destroys that. The only things
    /// allowed to be bright are people, because people are what you are watching. Which is
    /// exactly why the one surviving colour is the one that marks the person you picked.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Selected = new Color32(0xFF, 0x6B, 0x4A, 0xFF);
    }
}
