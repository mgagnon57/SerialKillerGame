using UnityEngine;
// UnityEngine also has a Terrain (the 3D heightmap kind), so name ours explicitly.
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// Every colour in the game, in one place.
    ///
    /// Deliberately muted and close in value: a village at this camera height reads by SHAPE —
    /// the line of a street, the block of the mill — and saturated colour destroys that. The
    /// only things allowed to be bright are people, because people are what you are watching.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Grass = new Color32(0x4C, 0x5A, 0x3E, 0xFF);
        public static readonly Color Field = new Color32(0x6E, 0x66, 0x45, 0xFF);
        public static readonly Color Wood = new Color32(0x2E, 0x3F, 0x2F, 0xFF);
        public static readonly Color Water = new Color32(0x2B, 0x45, 0x55, 0xFF);
        public static readonly Color Road = new Color32(0x4A, 0x46, 0x42, 0xFF);
        public static readonly Color Path = new Color32(0x5C, 0x55, 0x49, 0xFF);
        public static readonly Color Wall = new Color32(0x24, 0x21, 0x1F, 0xFF);
        public static readonly Color Floor = new Color32(0x6B, 0x5F, 0x53, 0xFF);
        public static readonly Color Churchyard = new Color32(0x51, 0x59, 0x47, 0xFF);

        public static readonly Color Adult = new Color32(0xE8, 0xE2, 0xD4, 0xFF);
        public static readonly Color Child = new Color32(0xF2, 0xC9, 0x7A, 0xFF);
        public static readonly Color Elder = new Color32(0xB9, 0xB2, 0xC4, 0xFF);
        public static readonly Color Selected = new Color32(0xFF, 0x6B, 0x4A, 0xFF);

        public static Color For(Terrain t)
        {
            switch (t)
            {
                case Terrain.Field: return Field;
                case Terrain.Wood: return Wood;
                case Terrain.Water: return Water;
                case Terrain.Road: return Road;
                case Terrain.Path: return Path;
                case Terrain.Wall: return Wall;
                case Terrain.Floor: return Floor;
                case Terrain.Churchyard: return Churchyard;
                default: return Grass;
            }
        }
    }
}
