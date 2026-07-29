using System;
using System.IO;

namespace Noir.Sim
{
    /// <summary>
    /// Minimal RIFF/WAVE encoder — 16-bit PCM, mono or stereo, and nothing else.
    ///
    /// Hand-rolled for the same reason PngWriter is: WAVE is a header and three chunks, and an
    /// audio library that also decodes MP3 and resamples FLAC is several megabytes of build for
    /// forty lines of work. Sixteen-bit PCM is also the one format that needs no decoder at the
    /// far end, which is what lets Unity read these as loose files with no import settings and
    /// no .meta files.
    /// </summary>
    public static class WavWriter
    {
        /// <summary>
        /// Write interleaved samples in -1..1.
        ///
        /// Out-of-range samples are clipped rather than allowed to wrap. A wrapped sample
        /// inverts the waveform for one frame, which is not "a bit loud" — it is a crack.
        /// </summary>
        public static void Write(string path, int sampleRate, int channels, double[] interleaved)
        {
            if (channels < 1 || channels > 2) throw new ArgumentException("mono or stereo only");
            if (interleaved.Length % channels != 0)
                throw new ArgumentException("sample buffer is not a whole number of frames");

            int dataBytes = interleaved.Length * 2;

            using var file = File.Create(path);
            using var w = new BinaryWriter(file);

            Tag(w, "RIFF");
            w.Write(36 + dataBytes);          // everything after this field
            Tag(w, "WAVE");

            Tag(w, "fmt ");
            w.Write(16);                      // PCM fmt chunk length
            w.Write((short)1);                // format: uncompressed PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);   // byte rate
            w.Write((short)(channels * 2));       // block align
            w.Write((short)16);                   // bits per sample

            Tag(w, "data");
            w.Write(dataBytes);

            var bytes = new byte[dataBytes];
            for (int i = 0; i < interleaved.Length; i++)
            {
                short s = ToPcm16(interleaved[i]);
                bytes[i * 2] = (byte)s;
                bytes[i * 2 + 1] = (byte)(s >> 8);
            }
            w.Write(bytes);
        }

        /// <summary>
        /// 32767 rather than 32768, so that +1.0 and -1.0 are both representable. Scaling by
        /// 32768 makes exactly +1.0 overflow into the most negative sample there is.
        /// </summary>
        private static short ToPcm16(double v)
        {
            if (v > 1.0) v = 1.0;
            else if (v < -1.0) v = -1.0;
            return (short)Math.Round(v * 32767.0, MidpointRounding.AwayFromZero);
        }

        private static void Tag(BinaryWriter w, string fourcc)
        {
            for (int i = 0; i < 4; i++) w.Write((byte)fourcc[i]);
        }
    }
}
