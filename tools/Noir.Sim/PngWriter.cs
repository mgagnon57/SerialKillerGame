using System;
using System.IO;
using System.IO.Compression;

namespace Noir.Sim
{
    /// <summary>
    /// Minimal PNG encoder — enough to write 32-bit RGBA images, and nothing more.
    ///
    /// Writing this by hand rather than taking a dependency: PNG is a short format (signature,
    /// IHDR, IDAT, IEND, each with a CRC), and .NET already ships ZLibStream for the only hard
    /// part. Roughly seventy lines against an image library in the build.
    /// </summary>
    public static class PngWriter
    {
        /// <summary>Write RGBA pixels (row 0 = top) to a PNG file.</summary>
        public static void Write(string path, int width, int height, byte[] rgba)
        {
            if (rgba.Length != width * height * 4)
                throw new ArgumentException("pixel buffer is the wrong size");

            using var file = File.Create(path);

            // signature
            file.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

            // IHDR
            var ihdr = new byte[13];
            WriteInt32(ihdr, 0, width);
            WriteInt32(ihdr, 4, height);
            ihdr[8] = 8;    // bit depth
            ihdr[9] = 6;    // colour type: RGBA
            ihdr[10] = 0;   // deflate
            ihdr[11] = 0;   // no filtering beyond per-scanline
            ihdr[12] = 0;   // no interlace
            WriteChunk(file, "IHDR", ihdr);

            // IDAT: each scanline prefixed with its filter byte (0 = none)
            var raw = new byte[height * (1 + width * 4)];
            int o = 0;
            for (int y = 0; y < height; y++)
            {
                raw[o++] = 0;
                Buffer.BlockCopy(rgba, y * width * 4, raw, o, width * 4);
                o += width * 4;
            }

            byte[] compressed;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    z.Write(raw, 0, raw.Length);
                compressed = ms.ToArray();
            }
            WriteChunk(file, "IDAT", compressed);

            WriteChunk(file, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteInt32(len, 0, data.Length);
            s.Write(len);

            var typeBytes = new byte[4];
            for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
            s.Write(typeBytes);
            s.Write(data);

            // The CRC covers the type bytes followed by the data, and nothing else.
            uint crc = 0xFFFFFFFF;
            crc = Crc32Update(crc, typeBytes);
            crc = Crc32Update(crc, data);
            crc ^= 0xFFFFFFFF;

            var crcBytes = new byte[4];
            WriteInt32(crcBytes, 0, unchecked((int)crc));
            s.Write(crcBytes);
        }

        private static void WriteInt32(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static uint[] _table;

        private static uint[] Table
        {
            get
            {
                if (_table != null) return _table;
                _table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    _table[n] = c;
                }
                return _table;
            }
        }

        private static uint Crc32Update(uint crc, byte[] data)
        {
            var t = Table;
            foreach (byte b in data) crc = t[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
