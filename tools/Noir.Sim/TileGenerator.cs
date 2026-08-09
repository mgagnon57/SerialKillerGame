using System;
using System.IO;

namespace Noir.Sim
{
    /// <summary>
    /// Generates the isometric tileset as real PNG files in Content/tiles/.
    ///
    /// These are a starting point, not a destination: they are proper 64x32 diamonds with
    /// transparent corners, so they prove the import pipeline works end to end AND give a set
    /// of files that can be opened in any pixel editor and painted over one at a time. Replace
    /// grass.png with something hand-drawn and only the grass changes.
    ///
    /// Deterministic: re-running produces byte-identical files, so the village never shifts
    /// underneath you.
    /// </summary>
    public static class TileGenerator
    {
        public const int TileW = 64;
        public const int TileH = 32;

        public static void GenerateAll(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Generating {TileW}x{TileH} isometric tiles into {outputDir}");
            Console.WriteLine();

            // Grass gets variants: the single cheapest fix for a field reading as tiled.
            for (int v = 0; v < 4; v++)
                Save(outputDir, v == 0 ? "grass" : $"grass_{v}", Grass(101 + v * 37));

            Save(outputDir, "field", Field());
            Save(outputDir, "wood", Wood());
            Save(outputDir, "water", Water());
            Save(outputDir, "road", Road());
            Save(outputDir, "path", FootPath());
            Save(outputDir, "floor", Floor());
            Save(outputDir, "churchyard", Churchyard());

            Console.WriteLine();
            Console.WriteLine("Done. Press Play in Unity and it will pick them up.");
        }

        // ---------- tiling surface textures for the 3D renderer ----------

        private const int SurfaceSize = 256;

        /// <summary>
        /// Seamless tiling surfaces for the 3D materials - grass, asphalt, ploughed earth and
        /// so on, at 256x256.
        ///
        /// Seamless comes for free from the hash noise: sampling at (x mod 256, y mod 256)
        /// means the right edge already agrees with the left. No blending, no mirroring, no
        /// visible seam every few metres.
        ///
        /// These are honest placeholders. A bought texture set replaces the files and nothing
        /// else - the materials read whatever is in Content/textures/.
        /// </summary>
        public static void GenerateSurfaces(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Generating {SurfaceSize}x{SurfaceSize} tiling surfaces into {outputDir}");
            Console.WriteLine();

            // These are ALBEDO values, and they carry the colour on their own - the material
            // tints them with white rather than with a hue.
            //
            // The previous set was half this bright, because they were meant to be multiplied
            // by a coloured material. Multiplying two mid-tones does not tint, it darkens:
            // grass at 37% times a base at 56% landed on 20%, and the whole village looked
            // like it was under a tarpaulin. Textures carry colour; materials carry white.
            // That is also how any bought PBR set expects to be used.
            // Grain is the strength of DIRECTIONAL streaking, and it wants to be near zero on
            // anything that grows. Grass at 0.35 was fine on a lawn, but the same texture is
            // tiled across half a kilometre of open country beyond the map, where the streaks
            // stretched into bands running to the horizon like a combed carpet. Furrows and
            // floorboards want direction; a field does not.
            SaveSurface(outputDir, "grass", 118, 146, 86, 24, 0.12, 1201);
            SaveSurface(outputDir, "field", 168, 150, 100, 18, 0.55, 1301);
            SaveSurface(outputDir, "wood", 76, 104, 72, 28, 0.18, 1401);
            SaveSurface(outputDir, "water", 62, 106, 136, 12, 0.15, 1501);
            SaveSurface(outputDir, "road", 120, 116, 110, 22, 0.10, 1601);
            SaveSurface(outputDir, "path", 148, 136, 116, 28, 0.20, 1701);
            SaveSurface(outputDir, "floor", 160, 138, 112, 14, 0.60, 1801);
            SaveSurface(outputDir, "churchyard", 128, 140, 106, 22, 0.15, 1901);
            // Grain well down from 0.70. Strong directional grain on a wall is not stone, it
            // is a smear: three metres of vertical streak on every elevation in the village.
            SaveSurface(outputDir, "wall", 192, 178, 160, 16, 0.18, 2001);
            SaveSurface(outputDir, "brick", 146, 104, 84, 20, 0.30, 2501);

            // Four roof coverings, not one. A village where every roof is the same colour
            // reads as an estate put up in a single year by a single contractor - which is
            // exactly how it looked. Each is a real texture rather than a tint, because
            // materials carry white and textures carry the colour.
            //
            // Roofs keep some grain, because courses of shingle genuinely do run in lines -
            // but at 0.80 the lines were louder than the roof and every elevation came out
            // scribbled.
            //
            // THREE-TAB ASPHALT SHINGLE, IN THE OWNER'S OWN MIX, settled 2026-08-09. These were
            // slate (96,100,110 - blue-grey), clay tile (158,92,70 - TERRACOTTA), worn tile and
            // THATCH (168,142,92 - straw). Ashcombe was an English village; this is Rossville,
            // Vermilion County, Illinois, and it is 1991. Straw and terracotta on six hundred
            // roofs is the "weird shit ... left over textures" he reported.
            //
            // THESE ARE THE FALLBACK, NOT THE ROOF. The editor binds the pack's real shingle
            // sets - albedo, normal and AO - through SurfaceTextures.ApplyPack, and all the
            // shingle detail is in the normal map. A shipped player has no AssetDatabase and
            // gets these instead, so their COLOURS are matched to what the pack path produces:
            // a build should look like a slightly softer version of the editor, never like a
            // different town. Grain is up from 0.40 because a shingle course reads harder than
            // a slate one, and it is the only shingle a player will get.
            SaveSurface(outputDir, "roof_shingle_grey", 64, 64, 64, 14, 0.55, 2101);
            SaveSurface(outputDir, "roof_shingle_charcoal", 44, 43, 43, 12, 0.55, 2201);
            SaveSurface(outputDir, "roof_shingle_brown", 105, 70, 42, 20, 0.50, 2301);
            SaveSurface(outputDir, "roof_shingle_black", 40, 27, 16, 12, 0.50, 2401);

            Console.WriteLine();
            Console.WriteLine("Done. Unity picks these up on Play.");
        }

        /// <summary>
        /// <paramref name="grain"/> controls directional streaking: 0 is pure speckle, 1 is
        /// strongly lined. Ploughed fields and floorboards want lines; grass does not.
        /// </summary>
        private static void SaveSurface(string dir, string name, int r0, int g0, int b0,
                                        int noise, double grain, int salt)
        {
            var buf = new byte[SurfaceSize * SurfaceSize * 4];

            for (int y = 0; y < SurfaceSize; y++)
            for (int x = 0; x < SurfaceSize; x++)
            {
                // Four octaves of interpolated noise, plus a little per-pixel speckle.
                //
                // The previous version held one hash value across each 16-pixel block, which
                // is not an octave of noise - it is a checkerboard. Magnified onto a four-metre
                // tile it gave the roads and greens hard half-metre squares, and the whole
                // village read as Minecraft from street level. Interpolating between lattice
                // points is the entire difference between noise and a grid.
                double coarse = Lattice(x, y, 64, 64, salt) - 0.5;
                double mid = Lattice(x, y, 16, 16, salt + 7) - 0.5;
                double fine = Lattice(x, y, 4, 4, salt + 11) - 0.5;
                double speck = Noise(x, y, salt + 17) - 0.5;

                // Furrows and floorboards want direction: a lattice that is short across and
                // long along draws streaks rather than blobs.
                double line = grain * (Lattice(x, y, 4, 64, salt + 23) - 0.5) * 1.6;

                double n = coarse * 0.42 + mid * 0.30 + fine * 0.18 + speck * 0.10 + line;
                int d = (int)(n * noise * 2.0);

                int i = (y * SurfaceSize + x) * 4;
                buf[i] = Clamp(r0 + d);
                buf[i + 1] = Clamp(g0 + d);
                buf[i + 2] = Clamp(b0 + d);
                buf[i + 3] = 255;
            }

            string path = Path.Combine(dir, name + ".png");
            PngWriter.Write(path, SurfaceSize, SurfaceSize, buf);
            Console.WriteLine($"  {name + ".png",-18} {SurfaceSize}x{SurfaceSize}");
        }

        private static void Save(string dir, string name, byte[] rgba)
        {
            string path = Path.Combine(dir, name + ".png");
            PngWriter.Write(path, TileW, TileH, rgba);
            Console.WriteLine($"  {name + ".png",-18} {TileW}x{TileH}");
        }

        // ---------- the diamond ----------

        /// <summary>Half-width of the diamond at row r, for a 2:1 tile.</summary>
        private static int HalfWidth(int r)
        {
            int k = r < TileH / 2 ? r : TileH - 1 - r;
            return k * 2 + 2;
        }

        private static bool Inside(int x, int r) =>
            r >= 0 && r < TileH && Math.Abs(x - TileW / 2) < HalfWidth(r);

        private static byte[] NewCanvas() => new byte[TileW * TileH * 4];   // all zero = transparent

        private static void Put(byte[] buf, int x, int r, int red, int green, int blue)
        {
            if (!Inside(x, r)) return;
            int i = (r * TileW + x) * 4;
            buf[i] = Clamp(red);
            buf[i + 1] = Clamp(green);
            buf[i + 2] = Clamp(blue);
            buf[i + 3] = 255;
        }

        private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        /// <summary>Flat fill of the diamond with per-pixel noise.</summary>
        private static byte[] Base(int r0, int g0, int b0, double noise, int salt)
        {
            var buf = NewCanvas();
            for (int r = 0; r < TileH; r++)
            for (int x = 0; x < TileW; x++)
            {
                if (!Inside(x, r)) continue;
                double n = Noise(x, r, salt) - 0.5;
                int d = (int)(n * noise);
                Put(buf, x, r, r0 + d, g0 + d, b0 + d);
            }
            return buf;
        }

        /// <summary>
        /// A line running along the tile's isometric axis, so furrows and floorboards follow
        /// the projection instead of cutting across it.
        /// </summary>
        private static void IsoLine(byte[] buf, int startRow, int red, int green, int blue)
        {
            for (int x = 0; x < TileW; x++)
            {
                int r = startRow + (x - TileW / 2) / 2;
                Put(buf, x, r, red, green, blue);
            }
        }

        // ---------- materials ----------

        private static byte[] Grass(int salt)
        {
            var buf = Base(76, 90, 62, 26, salt);
            for (int i = 0; i < 16; i++)
            {
                int bx = 6 + (int)(Hash(i, 1, salt + 200) % (uint)(TileW - 12));
                int by = 4 + (int)(Hash(i, 2, salt + 300) % (uint)(TileH - 8));
                Put(buf, bx, by, 58, 74, 46);
                Put(buf, bx, by + 1, 63, 80, 50);
            }
            return buf;
        }

        private static byte[] Field()
        {
            var buf = Base(110, 102, 69, 18, 211);
            for (int r = 0; r < TileH; r += 4) IsoLine(buf, r, 95, 88, 59);
            return buf;
        }

        private static byte[] Wood()
        {
            var buf = Base(46, 63, 47, 34, 307);
            for (int i = 0; i < 3; i++)
            {
                int cx = 14 + (int)(Hash(i, 7, 401) % (uint)(TileW - 28));
                int cy = 8 + (int)(Hash(i, 7, 409) % (uint)(TileH - 16));
                int rad = 5 + (int)(Hash(i, 7, 419) % 3);
                for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 > rad * rad) continue;
                    double edge = d2 / (double)(rad * rad);
                    int lift = (int)(30 - edge * 44);
                    Put(buf, cx + dx, cy + dy, 46 + lift, 63 + lift, 47 + lift);
                }
            }
            return buf;
        }

        private static byte[] Water()
        {
            var buf = Base(43, 69, 85, 10, 401);
            for (int r = 2; r < TileH; r += 6) IsoLine(buf, r, 57, 85, 101);
            return buf;
        }

        private static byte[] Road()
        {
            var buf = Base(74, 70, 66, 22, 503);
            for (int i = 0; i < 30; i++)
            {
                int bx = (int)(Hash(i, 3, 601) % (uint)TileW);
                int by = (int)(Hash(i, 3, 607) % (uint)TileH);
                Put(buf, bx, by, 89, 85, 80);
            }
            return buf;
        }

        private static byte[] FootPath()
        {
            var buf = Base(92, 85, 73, 30, 701);
            for (int i = 0; i < 20; i++)
            {
                int bx = (int)(Hash(i, 5, 801) % (uint)TileW);
                int by = (int)(Hash(i, 5, 809) % (uint)TileH);
                Put(buf, bx, by, 119, 111, 97);
            }
            return buf;
        }

        private static byte[] Floor()
        {
            var buf = Base(107, 95, 83, 12, 907);
            for (int r = 0; r < TileH; r += 5) IsoLine(buf, r, 84, 74, 64);
            return buf;
        }

        private static byte[] Churchyard()
        {
            var buf = Base(81, 89, 71, 22, 1009);
            for (int y = 11; y < 18; y++)
            for (int x = 30; x < 35; x++)
                Put(buf, x, y, 138, 136, 128);
            return buf;
        }

        // ---------- deterministic noise ----------

        private static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 0x85EBCA6B;
                h ^= h >> 16;
                return h;
            }
        }

        private static double Noise(int x, int y, int salt) => (Hash(x, y, salt) & 0xFFFF) / 65536.0;

        /// <summary>
        /// Value noise: hashes on a lattice of the given period, smoothly interpolated between.
        ///
        /// Seamlessness is preserved because the period divides the texture size and the far
        /// lattice point wraps to zero, so the right edge is interpolating towards the same
        /// value the left edge starts from.
        /// </summary>
        private static double Lattice(int x, int y, int periodX, int periodY, int salt)
        {
            int x0 = x / periodX * periodX, y0 = y / periodY * periodY;
            int x1 = (x0 + periodX) % SurfaceSize, y1 = (y0 + periodY) % SurfaceSize;

            double fx = Smooth((x - x0) / (double)periodX);
            double fy = Smooth((y - y0) / (double)periodY);

            double top = Lerp(Noise(x0, y0, salt), Noise(x1, y0, salt), fx);
            double bottom = Lerp(Noise(x0, y1, salt), Noise(x1, y1, salt), fx);
            return Lerp(top, bottom, fy);
        }

        /// <summary>Smoothstep. Linear interpolation alone leaves visible lattice creases.</summary>
        private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
