using System;

namespace Noir.Core.Contracts
{
    /// <summary>
    /// All randomness in the simulation routes through this. Nothing in Core may call
    /// System.Random or any ambient source — that is what makes a seed reproduce a village.
    /// </summary>
    public interface IRng
    {
        /// <summary>Uniform in [0, 2^64).</summary>
        ulong NextUInt64();

        /// <summary>Uniform in [0, exclusiveMax). Rejection-sampled, so no modulo bias.</summary>
        int NextInt(int exclusiveMax);

        /// <summary>Uniform in [inclusiveMin, exclusiveMax).</summary>
        int NextInt(int inclusiveMin, int exclusiveMax);

        /// <summary>Uniform in [0, 1).</summary>
        float NextFloat();

        /// <summary>True with the given probability.</summary>
        bool Chance(float probability);
    }

    /// <summary>
    /// xoshiro256** — small, fast, good statistical quality, and trivially reproducible
    /// because its entire state is four ulongs we own.
    ///
    /// Substreams matter: derive one per system with <see cref="Substream"/> so that adding
    /// a new system later does not shift the numbers every other system draws. Without this,
    /// adding one call to the schedule generator would rebuild the whole village differently.
    /// </summary>
    public sealed class Xoshiro256ss : IRng
    {
        private ulong _s0, _s1, _s2, _s3;

        public Xoshiro256ss(ulong seed)
        {
            // SplitMix64 to spread a single seed across the four state words.
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            _s2 = SplitMix64(ref seed);
            _s3 = SplitMix64(ref seed);
        }

        /// <summary>
        /// A named, independent stream derived from a master seed.
        /// Use one per system: Substream(seed, "population"), Substream(seed, "schedules"), ...
        /// </summary>
        public static Xoshiro256ss Substream(ulong masterSeed, string streamName)
        {
            return new Xoshiro256ss(masterSeed ^ Keys.Of(streamName));
        }

        private static ulong SplitMix64(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextUInt64()
        {
            ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            // Rejection sampling to avoid modulo bias.
            ulong bound = (ulong)exclusiveMax;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % bound) - 1;
            ulong r;
            do { r = NextUInt64(); } while (r > limit);
            return (int)(r % bound);
        }

        public int NextInt(int inclusiveMin, int exclusiveMax)
        {
            if (exclusiveMax <= inclusiveMin) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            return inclusiveMin + NextInt(exclusiveMax - inclusiveMin);
        }

        /// <summary>Top 24 bits scaled into [0,1) — exact, no rounding surprises.</summary>
        public float NextFloat() => (NextUInt64() >> 40) * (1.0f / 16777216.0f);

        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat() < probability;
        }
    }

    /// <summary>
    /// Stable 64-bit names for things, so that randomness can be keyed on WHAT something is
    /// rather than on when it happened to be reached.
    /// </summary>
    public static class Keys
    {
        /// <summary>FNV-1a over the characters. Not a cryptographic hash and does not need to be.</summary>
        public static ulong Of(string text)
        {
            ulong h = 1469598103934665603UL; // FNV-1a offset basis
            if (text == null) return h;
            for (int i = 0; i < text.Length; i++)
            {
                h ^= text[i];
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>One key from two, order-dependent. Used to name a thing inside a named thing.</summary>
        public static ulong Combine(ulong a, ulong b) => Rolls.Avalanche(Rolls.Avalanche(a) ^ b);

        /// <summary>
        /// The same key whichever of the two is asked first.
        ///
        /// A roll about a PAIR of people must not depend on which of them the scan reached
        /// first, or the answer changes when an unrelated system changes the scan.
        /// </summary>
        public static ulong Pair(ulong a, ulong b) =>
            a <= b ? Combine(a, b) : Combine(b, a);
    }

    /// <summary>
    /// A draw with no stream position: a pure function of (seed, purpose, subject, tick, salt).
    ///
    /// This exists because a shared mutable stream consumed in agent-index order gives NO
    /// determinism guarantee worth having. Named substreams protect against adding a SYSTEM —
    /// they do nothing about the same system running for a different subset of agents, and that
    /// is the case that actually occurs: whether agent 5 crosses a doorway this tick decides
    /// what number agent 6 gets. Take the stream position away and there is nothing left for
    /// scan order to influence.
    ///
    /// Xoshiro256ss is still the right tool for GENERATION — building a village or a population
    /// is one single-threaded pure function where a stream is the natural shape, and a stream is
    /// cheaper per draw than this is. Use this wherever the caller is one agent among many.
    /// </summary>
    public static class Rolls
    {
        /// <summary>Names a kind of decision. Cache it in a static readonly field, not per call.</summary>
        public static ulong Purpose(string name) => Keys.Of(name);

        /// <summary>The SplitMix64 finaliser: spreads every input bit across all sixty-four.
        /// Public because it is also how a key becomes an index — see Simulation.SpotIn.</summary>
        public static ulong Avalanche(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>
        /// Sixty-four bits belonging to exactly this decision.
        ///
        /// <paramref name="subject"/> is who or what the decision is about — a citizen key, a
        /// pair key, a place. <paramref name="salt"/> separates two draws that would otherwise
        /// share all four other arguments, such as the length of a conversation and whether it
        /// happens at all.
        /// </summary>
        public static ulong Bits(ulong seed, ulong purpose, ulong subject, long tick, ulong salt)
        {
            ulong h = Avalanche(seed ^ 0x9E3779B97F4A7C15UL ^ purpose);
            h = Avalanche(h ^ subject);
            h = Avalanche(h ^ (ulong)tick);
            return Avalanche(h ^ salt);
        }

        /// <summary>Uniform in [0, exclusiveMax), rejection-sampled by re-salting rather than
        /// by advancing a stream — there is no stream to advance.</summary>
        public static int Int(ulong seed, ulong purpose, ulong subject, long tick, ulong salt,
                              int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

            ulong bound = (ulong)exclusiveMax;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % bound) - 1;

            // Sixteen tries, then take the modulo anyway. Each try fails with probability under
            // 2^-32 for any bound the simulation uses, so the fallback is unreachable in
            // practice — and it is here so that this returns rather than looping forever if it
            // ever stops being.
            ulong r = 0;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                r = Bits(seed, purpose, subject, tick, salt + (ulong)attempt * 0x9E3779B97F4A7C15UL);
                if (r <= limit) break;
            }
            return (int)(r % bound);
        }

        public static int Int(ulong seed, ulong purpose, ulong subject, long tick, ulong salt,
                              int inclusiveMin, int exclusiveMax)
        {
            if (exclusiveMax <= inclusiveMin) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            return inclusiveMin + Int(seed, purpose, subject, tick, salt, exclusiveMax - inclusiveMin);
        }

        /// <summary>Uniform in [0, 1), from the top 24 bits — exactly as <see cref="Xoshiro256ss.NextFloat"/>.</summary>
        public static float Unit(ulong seed, ulong purpose, ulong subject, long tick, ulong salt) =>
            (Bits(seed, purpose, subject, tick, salt) >> 40) * (1.0f / 16777216.0f);

        public static bool Chance(ulong seed, ulong purpose, ulong subject, long tick, ulong salt,
                                  float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return Unit(seed, purpose, subject, tick, salt) < probability;
        }
    }
}
