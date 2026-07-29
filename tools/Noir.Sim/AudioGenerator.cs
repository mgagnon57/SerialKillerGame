using System;
using System.IO;

namespace Noir.Sim
{
    /// <summary>
    /// Every sound in Ashcombe, synthesised into Content/audio/ as 16-bit PCM.
    ///
    /// Nothing here is recorded and nothing is bought. That is not thrift for its own sake: a
    /// generated bell can be retuned by editing one number, a bed can be made twelve seconds
    /// longer without a licence, and the whole soundscape survives in a text file instead of in
    /// a folder nobody can reproduce. The trade is that it will never be as good as a real
    /// recording of a real bell — but silence is very much worse than a synthesised bell, and
    /// silence is what the village had.
    ///
    /// Deterministic, like TileGenerator: fixed seeds, no System.Random, nothing that reads a
    /// clock. Re-running produces byte-identical files, so regenerating audio never quietly
    /// changes what the village sounds like.
    /// </summary>
    public static class AudioGenerator
    {
        /// <summary>Full rate for anything with a transient in it — the bell and the footsteps.</summary>
        public const int Rate = 44100;

        /// <summary>
        /// The ambience beds run at half rate. Nothing in wind or birdsong lives above 11 kHz,
        /// and the beds are by far the longest files: half the rate is half the megabytes for
        /// a difference nobody will hear through a laptop speaker.
        /// </summary>
        public const int BedRate = 22050;

        /// <summary>
        /// Loop length of an ambience bed. Long enough that the ear does not lock onto the
        /// repeat, short enough that three of them fit in three megabytes.
        /// </summary>
        public const double BedSeconds = 12.0;

        public static void GenerateAll(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Generating audio into {outputDir}");
            Console.WriteLine();
            Console.WriteLine("  file                      ch    rate    secs     peak      rms       dc    seam");

            _bytes = 0;
            _files = 0;

            Save(outputDir, "bell", Rate, 1, Bell(), looping: false);

            Save(outputDir, "ambience_dawn", BedRate, 2, DawnBed(), looping: true);
            Save(outputDir, "ambience_day", BedRate, 2, DayBed(), looping: true);
            Save(outputDir, "ambience_night", BedRate, 2, NightBed(), looping: true);

            // Two takes of each surface, alternated at play time and pitched a few per cent
            // either way. Two is enough because the ear notices a repeated step, not a
            // repeated pair of steps.
            Save(outputDir, "step_grass_a", Rate, 1, StepGrass(3101), looping: false);
            Save(outputDir, "step_grass_b", Rate, 1, StepGrass(3102), looping: false);
            Save(outputDir, "step_road_a", Rate, 1, StepRoad(3201), looping: false);
            Save(outputDir, "step_road_b", Rate, 1, StepRoad(3202), looping: false);
            Save(outputDir, "step_path_a", Rate, 1, StepPath(3301), looping: false);
            Save(outputDir, "step_path_b", Rate, 1, StepPath(3302), looping: false);
            Save(outputDir, "step_floor_a", Rate, 1, StepFloor(3401, creak: false), looping: false);
            Save(outputDir, "step_floor_b", Rate, 1, StepFloor(3402, creak: true), looping: false);
            Save(outputDir, "step_churchyard_a", Rate, 1, StepChurchyard(3501), looping: false);
            Save(outputDir, "step_churchyard_b", Rate, 1, StepChurchyard(3502), looping: false);

            Console.WriteLine();
            Console.WriteLine($"  {_files} files, {_bytes / 1024.0 / 1024.0:0.00} MB.");
            Console.WriteLine("  Unity picks these up on Play.");
        }

        private static long _bytes;
        private static int _files;

        // ================================ the bell ================================

        /// <summary>
        /// A struck bell is a decaying sum of INHARMONIC partials, and the inharmonicity is the
        /// whole point: the tierce sits a minor third above the prime rather than a major one,
        /// which is why a bell sounds solemn where a harmonic series sounds like an organ. The
        /// named partials below are the ones a bellfounder actually tunes.
        ///
        /// Ratios are against the prime. The pitch you would name the bell is the prime, but
        /// the partial that carries it to the far side of the fields is the nominal, an octave
        /// up — which is also why a bell heard across a valley sounds higher than it is.
        /// </summary>
        private static readonly double[,] BellPartials =
        {
            // ratio   amplitude   decay (s)
            { 0.500,   0.30,       9.0 },   // hum
            { 1.000,   0.50,       6.5 },   // prime
            { 1.183,   0.42,       4.2 },   // tierce — the minor third
            { 1.506,   0.22,       3.0 },   // quint
            { 2.000,   0.55,       2.4 },   // nominal
            { 2.510,   0.13,       1.5 },   // deciem
            { 3.010,   0.10,       1.1 },   // undecim
            { 4.020,   0.07,       0.80 },  // duodecim
            { 5.330,   0.05,       0.55 },
            { 6.700,   0.035,      0.40 },
        };

        /// <summary>D4. A parish tenor bell, not a cathedral one and not a handbell.</summary>
        private const double BellPrime = 293.665;

        /// <summary>
        /// Eight seconds, of which the last two are a taper. The hum partial of a real tenor
        /// bell is still going after twenty seconds and no file here is going to be twenty
        /// seconds long, so the tail has to be faded — but it has to be faded over long enough
        /// that it reads as the bell dying away rather than as somebody turning it down. The
        /// bell loses about 2 dB a second, so anything under a second of fade is audible as a cut.
        /// </summary>
        private const double BellSeconds = 8.0;
        private const double BellFade = 2.0;

        private static double[] Bell()
        {
            int n = (int)(Rate * BellSeconds);
            var buf = new double[n];
            var rng = new Rng(4408);

            for (int p = 0; p < BellPartials.GetLength(0); p++)
            {
                double f = BellPrime * BellPartials[p, 0];
                double amp = BellPartials[p, 1];
                double decay = BellPartials[p, 2];

                // Every partial of a real bell is really two, a fraction of a hertz apart,
                // because no casting is perfectly round. Those pairs beating against each other
                // are the slow warble in the tail. One sine per partial gives a church ORGAN.
                double detune = rng.Range(0.5, 2.2);
                double phaseA = rng.Range(0.0, 2.0 * Math.PI);
                double phaseB = rng.Range(0.0, 2.0 * Math.PI);

                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)Rate;
                    double env = Math.Exp(-t / decay);
                    if (env < 1e-6) break;
                    buf[i] += amp * env * 0.5 *
                              (Math.Sin(2.0 * Math.PI * f * t + phaseA) +
                               Math.Sin(2.0 * Math.PI * (f + detune) * t + phaseB));
                }
            }

            // The strike itself: a fraction of a second of broadband clatter as the clapper
            // hits. Without it the note simply appears out of nothing, which sounds like a
            // synthesiser rather than a hundredweight of bronze being struck.
            Burst(buf, Rate, 0.0, 0.30, Biquad.BandPass(2100, 0.55, Rate), 0.055, 0.55, rng);
            Burst(buf, Rate, 0.0, 0.10, Biquad.BandPass(5200, 0.90, Rate), 0.014, 0.30, rng);

            Attack(buf, Rate, 0.004);
            Fade(buf, Rate, BellFade);
            return Normalise(buf, 0.85);
        }

        // ============================ the ambience beds ============================
        //
        // Three beds, mixed by the hour at play time rather than switched: at half past four in
        // the morning you want the night still under the chorus, not cut away from it.
        //
        // Everything in a bed has to survive being looped. Wind is filtered CIRCULARLY so the
        // filter state at the seam is the state the loop starts with; gusts are built from sines
        // whose periods divide the loop exactly; and a bird that starts a tenth of a second
        // before the end wraps round and finishes at the beginning. Any one of those left alone
        // puts an audible tick every twelve seconds, which is the single loudest way to tell
        // somebody they are listening to a loop.

        private static double[] DawnBed()
        {
            int n = (int)(BedRate * BedSeconds);
            var l = new double[n];
            var r = new double[n];
            var rng = new Rng(5101);

            // The air is stillest at first light, so the wind sits well under the birds.
            Wind(l, 0.038, 260, 0.55, rng);
            Wind(r, 0.038, 260, 0.55, rng);

            // The dawn chorus is not a few birds, it is a wall. Robins and wrens start it,
            // blackbirds carry the melody, and everything else piles in.
            for (int i = 0; i < 11; i++) Robin(l, r, rng, 0.055, 0.105);
            for (int i = 0; i < 7; i++) Blackbird(l, r, rng, 0.060, 0.115);
            for (int i = 0; i < 8; i++) GreatTit(l, r, rng, 0.040, 0.080);
            for (int i = 0; i < 7; i++) Sparrow(l, r, rng, 0.035, 0.070);
            for (int i = 0; i < 2; i++) Pigeon(l, r, rng, 0.070, 0.100);

            return Finish(l, r, DawnRms);
        }

        private static double[] DayBed()
        {
            int n = (int)(BedRate * BedSeconds);
            var l = new double[n];
            var r = new double[n];
            var rng = new Rng(5201);

            // More air moving by mid-morning, and much less singing: birds sing to hold a
            // territory at dawn and dusk, and spend the middle of the day eating.
            Wind(l, 0.072, 380, 0.70, rng);
            Wind(r, 0.072, 380, 0.70, rng);

            for (int i = 0; i < 4; i++) Sparrow(l, r, rng, 0.030, 0.065);
            for (int i = 0; i < 2; i++) Blackbird(l, r, rng, 0.030, 0.060);
            for (int i = 0; i < 2; i++) Pigeon(l, r, rng, 0.055, 0.085);
            for (int i = 0; i < 2; i++) Rook(l, r, rng, 0.030, 0.055);

            return Finish(l, r, DayRms);
        }

        private static double[] NightBed()
        {
            int n = (int)(BedRate * BedSeconds);
            var l = new double[n];
            var r = new double[n];
            var rng = new Rng(5301);

            // Three in the morning is the quietest this village ever gets. The bed is mostly
            // low wind: loud enough that the speakers are not obviously off, quiet enough that
            // a door closing two streets away would be the loudest thing you heard.
            Wind(l, 0.055, 190, 0.80, rng);
            Wind(r, 0.055, 190, 0.80, rng);

            for (int i = 0; i < 2; i++) Owl(l, r, rng, 0.050, 0.075);
            Dog(l, r, rng, 0.022, 0.034);

            return Finish(l, r, NightRms);
        }

        /// <summary>
        /// Where each bed sits in the finished mix, as RMS rather than peak — see NormaliseRms.
        /// Night is a genuinely quieter FILE rather than a loud one turned down, so that the
        /// Unity side needs one volume curve for all three and the balance between them cannot
        /// drift away from what was auditioned here.
        /// </summary>
        private const double DawnRms = 0.079;    // about -22 dBFS
        private const double DayRms = 0.063;     //       -24
        private const double NightRms = 0.032;   //       -30

        /// <summary>
        /// Take the DC out of each channel, interleave, and set the bed's level.
        ///
        /// Both channels are scaled by the same factor, computed across the pair: normalising
        /// them separately would quietly recentre the stereo image on whichever side happened
        /// to get the louder blackbird.
        /// </summary>
        private static double[] Finish(double[] l, double[] r, double rms)
        {
            RemoveDc(l);
            RemoveDc(r);
            var mix = Interleave(l, r);
            NormaliseRms(mix, rms);
            return mix;
        }

        // ---- wind ----

        /// <summary>
        /// Wind is filtered noise with slow gusts on it, and both halves have to loop.
        ///
        /// <paramref name="cutoff"/> is what makes it wind rather than hiss: 190 Hz is the low
        /// moan of a still night, 380 Hz is daylight moving through hedges.
        /// </summary>
        private static void Wind(double[] ch, double rms, double cutoff, double gust, Rng rng)
        {
            for (int i = 0; i < ch.Length; i++) ch[i] = rng.Bipolar();
            LowPassLoop(ch, cutoff, BedRate);
            NormaliseRms(ch, rms);

            // Three slow sines, at one, two and three cycles per loop. Integer cycle counts are
            // the entire reason the gust pattern wraps as cleanly as the noise under it.
            double p1 = rng.Range(0, 2 * Math.PI), p2 = rng.Range(0, 2 * Math.PI), p3 = rng.Range(0, 2 * Math.PI);
            for (int i = 0; i < ch.Length; i++)
            {
                double u = i / (double)ch.Length;
                double g = (Math.Sin(2 * Math.PI * u + p1)
                          + Math.Sin(4 * Math.PI * u + p2)
                          + Math.Sin(6 * Math.PI * u + p3)) / 3.0;
                ch[i] *= 1.0 - gust * (0.5 - 0.5 * g);
            }
        }

        // ---- birds ----
        //
        // Every one of these is the same trick: a short sine sweep with a raised-cosine
        // envelope, repeated a few times with jitter. That is a crude model of a syrinx and it
        // would not fool a birdwatcher, but pitch contour is most of what tells two garden
        // birds apart at thirty metres, and pitch contour is exactly what it gets right.

        private static void Robin(double[] l, double[] r, Rng rng, double near, double far) =>
            Phrase(l, r, rng, rng.NextInt(4, 8), 4300, 6100, 0.055, 0.030, 0.12, near, far);

        private static void Blackbird(double[] l, double[] r, Rng rng, double near, double far) =>
            Phrase(l, r, rng, rng.NextInt(3, 6), 2000, 2900, 0.150, 0.090, 0.22, near, far);

        private static void GreatTit(double[] l, double[] r, Rng rng, double near, double far) =>
            Phrase(l, r, rng, rng.NextInt(4, 7), 4300, 3200, 0.110, 0.070, 0.30, near, far);

        private static void Sparrow(double[] l, double[] r, Rng rng, double near, double far) =>
            Phrase(l, r, rng, rng.NextInt(2, 4), 3500, 3200, 0.055, 0.100, 0.30, near, far);

        /// <summary>A wood pigeon: five low notes, the second one leaned on. The sound of an English afternoon.</summary>
        private static void Pigeon(double[] l, double[] r, Rng rng, double near, double far) =>
            Phrase(l, r, rng, 5, 560, 505, 0.280, 0.110, 0.35, near, far);

        /// <summary>An owl. Two soft notes a long way apart, which is why the note length is nearly half a second.</summary>
        private static void Owl(double[] l, double[] r, Rng rng, double near, double far) =>
            Phrase(l, r, rng, 2, 415, 398, 0.420, 0.480, 0.14, near, far);

        private static void Phrase(double[] l, double[] r, Rng rng, int notes,
                                   double f0, double f1, double noteSec, double gapSec,
                                   double harmonic, double near, double far)
        {
            double t = rng.Range(0.0, BedSeconds);
            double pan = rng.Range(-0.85, 0.85);
            double gain = rng.Range(near, far);

            for (int k = 0; k < notes; k++)
            {
                double dur = noteSec * rng.Range(0.75, 1.30);
                Note(l, r, t, dur,
                     f0 * rng.Range(0.94, 1.06), f1 * rng.Range(0.90, 1.10),
                     gain * rng.Range(0.70, 1.00), pan, harmonic);
                t += dur + gapSec * rng.Range(0.50, 1.60);
            }
        }

        /// <summary>
        /// One note, written into the loop with wraparound. The envelope is a full raised
        /// cosine, so the note starts and ends at exactly zero wherever it happens to land.
        /// </summary>
        private static void Note(double[] l, double[] r, double startSec, double durSec,
                                 double fStart, double fEnd, double gain, double pan, double harmonic)
        {
            int n = l.Length;
            int len = (int)(durSec * BedRate);
            if (len < 4) return;

            int at = (int)(startSec * BedRate);
            double gl = gain * Math.Cos((pan + 1.0) * Math.PI * 0.25);
            double gr = gain * Math.Sin((pan + 1.0) * Math.PI * 0.25);

            double phase = 0.0;
            for (int i = 0; i < len; i++)
            {
                double u = i / (double)len;
                phase += 2.0 * Math.PI * (fStart + (fEnd - fStart) * u) / BedRate;
                double env = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * u);
                double s = env * (Math.Sin(phase) + harmonic * Math.Sin(phase * 2.0));

                int j = (at + i) % n;
                l[j] += gl * s;
                r[j] += gr * s;
            }
        }

        /// <summary>
        /// A rook, and a dog, share a shape: harsh, because they are a stack of harmonics with
        /// noise smeared across them rather than a tone.
        /// </summary>
        private static void Harsh(double[] l, double[] r, Rng rng, int calls, double f,
                                  double callSec, double gapSec, double roughness,
                                  double near, double far)
        {
            int n = l.Length;
            double t = rng.Range(0.0, BedSeconds);
            double pan = rng.Range(-0.8, 0.8);
            double gain = rng.Range(near, far);
            double gl = Math.Cos((pan + 1.0) * Math.PI * 0.25);
            double gr = Math.Sin((pan + 1.0) * Math.PI * 0.25);

            for (int c = 0; c < calls; c++)
            {
                double dur = callSec * rng.Range(0.8, 1.25);
                double f0 = f * rng.Range(0.9, 1.12);
                int len = (int)(dur * BedRate);
                int at = (int)(t * BedRate);

                double phase = 0.0, grit = 0.0;
                for (int i = 0; i < len; i++)
                {
                    double u = i / (double)len;
                    phase += 2.0 * Math.PI * f0 * (1.0 - 0.15 * u) / BedRate;

                    double s = 0.0;
                    for (int k = 1; k <= 5; k++) s += Math.Sin(phase * k) / k;

                    // Slow-moving noise on the amplitude, not added noise: that is the
                    // difference between a rough voice and a voice with hiss behind it.
                    grit += 0.05 * (rng.Bipolar() - grit);
                    double env = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * u);
                    s *= env * (1.0 - roughness + roughness * (0.5 + 0.5 * grit)) * gain * 0.45;

                    int j = (at + i) % n;
                    l[j] += gl * s;
                    r[j] += gr * s;
                }
                t += dur + gapSec * rng.Range(0.6, 1.5);
            }
        }

        private static void Rook(double[] l, double[] r, Rng rng, double near, double far) =>
            Harsh(l, r, rng, rng.NextInt(2, 5), 620, 0.20, 0.22, 0.80, near, far);

        /// <summary>A dog two fields away at two in the morning — quiet, and all the more carrying for it.</summary>
        private static void Dog(double[] l, double[] r, Rng rng, double near, double far) =>
            Harsh(l, r, rng, rng.NextInt(2, 4), 330, 0.13, 0.42, 0.55, near, far);

        // =============================== footsteps ===============================
        //
        // A footstep is an impact and a surface, and it is the surface that carries the
        // information: you know you have walked off the road onto grass without looking down.
        // Each of these is filtered noise with an envelope, plus whatever the surface adds —
        // grit for gravel, a resonance for floorboards.

        /// <summary>Grass has no impact worth the name. It is all swish, and it is the quietest thing underfoot in the village.</summary>
        private static double[] StepGrass(int seed)
        {
            var rng = new Rng((ulong)seed);
            var buf = new double[(int)(Rate * 0.26)];
            Burst(buf, Rate, 0.000, 0.16, Biquad.LowPass(rng.Range(1300, 1750), 0.70, Rate), 0.045, 1.00, rng);
            Burst(buf, Rate, 0.028, 0.13, Biquad.BandPass(rng.Range(2300, 3000), 0.60, Rate), 0.032, 0.40, rng);
            Fade(buf, Rate, 0.010);
            return Normalise(buf, 0.40);
        }

        /// <summary>Tarmac: dry, short, and with a hard edge on the heel that no other surface has.</summary>
        private static double[] StepRoad(int seed)
        {
            var rng = new Rng((ulong)seed);
            var buf = new double[(int)(Rate * 0.22)];
            Burst(buf, Rate, 0.000, 0.10, Biquad.BandPass(rng.Range(700, 880), 0.90, Rate), 0.022, 1.00, rng);
            Burst(buf, Rate, 0.000, 0.05, Biquad.BandPass(rng.Range(3100, 3800), 1.10, Rate), 0.008, 0.45, rng);
            Tone(buf, Rate, 0.000, rng.Range(140, 160), rng.Range(140, 160), 0.030, 0.25);
            Fade(buf, Rate, 0.008);
            return Normalise(buf, 0.70);
        }

        /// <summary>Packed earth with grit on it: one soft thump and a scatter of individual stones.</summary>
        private static double[] StepPath(int seed)
        {
            var rng = new Rng((ulong)seed);
            var buf = new double[(int)(Rate * 0.24)];
            Burst(buf, Rate, 0.000, 0.10, Biquad.BandPass(rng.Range(270, 340), 0.80, Rate), 0.030, 1.00, rng);
            Grains(buf, rng, count: 11, spread: 0.075, lo: 1800, hi: 4800, gain: 0.34);
            Fade(buf, Rate, 0.008);
            return Normalise(buf, 0.66);
        }

        /// <summary>
        /// The churchyard is loose gravel over grass: more stones than the footpath, brighter,
        /// and they go on rattling for a moment after the foot has stopped moving.
        /// </summary>
        private static double[] StepChurchyard(int seed)
        {
            var rng = new Rng((ulong)seed);
            var buf = new double[(int)(Rate * 0.28)];
            Burst(buf, Rate, 0.000, 0.08, Biquad.BandPass(rng.Range(240, 300), 0.90, Rate), 0.024, 0.60, rng);
            Grains(buf, rng, count: 22, spread: 0.115, lo: 2500, hi: 7000, gain: 0.42);
            Fade(buf, Rate, 0.010);
            return Normalise(buf, 0.60);
        }

        /// <summary>
        /// Floorboards. The resonance is what makes it read as indoors — a suspended floor
        /// rings at its own pitch, which is why a house sounds different from a pavement even
        /// through the same shoe.
        /// </summary>
        private static double[] StepFloor(int seed, bool creak)
        {
            var rng = new Rng((ulong)seed);
            var buf = new double[(int)(Rate * 0.30)];
            double f = rng.Range(110, 128);
            Tone(buf, Rate, 0.000, f, f, 0.075, 1.00);
            Tone(buf, Rate, 0.000, f * 1.58, f * 1.58, 0.045, 0.45);
            Burst(buf, Rate, 0.000, 0.06, Biquad.BandPass(rng.Range(1150, 1450), 0.80, Rate), 0.014, 0.55, rng);
            Burst(buf, Rate, 0.001, 0.03, Biquad.BandPass(rng.Range(3800, 4600), 1.30, Rate), 0.006, 0.20, rng);

            // Only one of the two takes creaks, so a board gives about every other pace rather
            // than under every single footfall, which would be a rotten floor rather than a house.
            if (creak) Tone(buf, Rate, 0.012, 640, 585, 0.060, 0.13, flutterHz: 17.0);

            Fade(buf, Rate, 0.010);
            return Normalise(buf, 0.75);
        }

        /// <summary>Individual stones, each one a few milliseconds of narrow-band noise.</summary>
        private static void Grains(double[] buf, Rng rng, int count, double spread,
                                   double lo, double hi, double gain)
        {
            for (int g = 0; g < count; g++)
                Burst(buf, Rate,
                      rng.Range(0.0, spread), 0.012,
                      Biquad.BandPass(rng.Range(lo, hi), 2.2, Rate),
                      0.0035, gain * rng.Range(0.30, 1.00), rng);
        }

        // ============================= signal plumbing =============================

        /// <summary>A filtered noise burst with an exponential tail, mixed in at an offset.</summary>
        private static void Burst(double[] buf, int rate, double startSec, double lenSec,
                                  Biquad filter, double decaySec, double gain, Rng rng)
        {
            int at = (int)(startSec * rate);
            int len = (int)(lenSec * rate);
            for (int i = 0; i < len; i++)
            {
                int j = at + i;
                if (j >= buf.Length) break;
                double t = i / (double)rate;
                // A raw noise burst begins with a step, which reads as a fault rather than as
                // a heel. One millisecond of ramp is enough to remove it and far too short to
                // soften the attack.
                double env = Math.Min(1.0, t / 0.0012) * Math.Exp(-t / decaySec);
                buf[j] += gain * env * filter.Process(rng.Bipolar());
            }
        }

        /// <summary>
        /// A decaying sine, optionally sliding in pitch and fluttering in level — which between
        /// them are a floor resonance, a bell partial, and a creaking board.
        /// </summary>
        private static void Tone(double[] buf, int rate, double startSec, double f0, double f1,
                                 double decaySec, double gain, double flutterHz = 0.0)
        {
            int at = (int)(startSec * rate);
            double phase = 0.0;
            for (int i = 0; at + i < buf.Length; i++)
            {
                double t = i / (double)rate;
                double env = Math.Exp(-t / decaySec) * Math.Min(1.0, t / 0.0015);
                if (env < 1e-5 && t > 0.005) break;

                double u = Math.Min(1.0, t / (decaySec * 3.0));
                phase += 2.0 * Math.PI * (f0 + (f1 - f0) * u) / rate;

                double flutter = flutterHz > 0.0
                    ? 0.65 + 0.35 * Math.Sin(2.0 * Math.PI * flutterHz * t)
                    : 1.0;
                buf[at + i] += gain * env * flutter * Math.Sin(phase);
            }
        }

        /// <summary>
        /// A low-pass run CIRCULARLY: it filters as though the buffer repeated for ever, so the
        /// filter state at the seam is the state the loop starts from. A straight pass leaves
        /// the first few hundred samples ramping up out of silence, which is a tick every time
        /// the bed wraps — the single loudest way to tell somebody they are hearing a loop.
        ///
        /// Each pole gets TWO passes over the signal: one to settle the state, whose output is
        /// thrown away, and one that writes. The settled state is what an infinitely repeated
        /// input would have produced, which is exactly what a loop is. Running the second pass
        /// over the first pass's output instead — the obvious shortcut — just re-filters the
        /// start-up transient and leaves the seam where it was.
        ///
        /// Two poles because 12 dB/octave is what wind sounds like and 6 is not.
        /// </summary>
        private static void LowPassLoop(double[] x, double cutoffHz, int rate, int poles = 2)
        {
            double a = 1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / rate);
            if (a > 1.0) a = 1.0;
            for (int p = 0; p < poles; p++)
            {
                double y = 0.0;
                for (int i = 0; i < x.Length; i++) y += a * (x[i] - y);
                for (int i = 0; i < x.Length; i++) { y += a * (x[i] - y); x[i] = y; }
            }
        }

        private static double[] Normalise(double[] x, double targetPeak)
        {
            double peak = 0.0;
            foreach (double v in x) { double a = Math.Abs(v); if (a > peak) peak = a; }
            if (peak <= 0.0) return x;
            double k = targetPeak / peak;
            for (int i = 0; i < x.Length; i++) x[i] *= k;
            return x;
        }

        /// <summary>
        /// Beds are matched on RMS rather than peak. Noise has a peak wherever it happens to
        /// have one; loudness is the average, and matching peaks would make the quiet bed the
        /// loud one.
        /// </summary>
        private static void NormaliseRms(double[] x, double targetRms)
        {
            double sum = 0.0;
            foreach (double v in x) sum += v * v;
            double rms = Math.Sqrt(sum / x.Length);
            if (rms <= 0.0) return;
            double k = targetRms / rms;
            for (int i = 0; i < x.Length; i++) x[i] *= k;
        }

        private static void RemoveDc(double[] x)
        {
            double sum = 0.0;
            foreach (double v in x) sum += v;
            double mean = sum / x.Length;
            for (int i = 0; i < x.Length; i++) x[i] -= mean;
        }

        private static void Attack(double[] x, int rate, double seconds)
        {
            int len = Math.Min(x.Length, (int)(seconds * rate));
            for (int i = 0; i < len; i++)
                x[i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / len);
        }

        /// <summary>Raised-cosine tail, so a one-shot ends at exactly zero and stopping it cannot click.</summary>
        private static void Fade(double[] x, int rate, double seconds)
        {
            int len = Math.Min(x.Length, (int)(seconds * rate));
            for (int i = 0; i < len; i++)
                x[x.Length - len + i] *= 0.5 + 0.5 * Math.Cos(Math.PI * i / len);
            if (x.Length > 0) x[x.Length - 1] = 0.0;
        }

        private static double[] Interleave(double[] l, double[] r)
        {
            var outp = new double[l.Length * 2];
            for (int i = 0; i < l.Length; i++) { outp[i * 2] = l[i]; outp[i * 2 + 1] = r[i]; }
            return outp;
        }

        // ============================== writing out ==============================

        private static void Save(string dir, string name, int rate, int channels,
                                 double[] interleaved, bool looping)
        {
            // A last guard rather than a mix decision: if anything came out hot enough to clip,
            // pull the whole file down and say so, because a clipped bell is unlistenable and a
            // silently clipped one is worse than an obviously quiet one.
            double peak = Peak(interleaved);
            if (peak > 0.95)
            {
                Console.WriteLine($"  {name}: peak {peak:0.000} — scaled down to 0.95");
                Normalise(interleaved, 0.95);
                peak = 0.95;
            }

            string path = Path.Combine(dir, name + ".wav");
            WavWriter.Write(path, rate, channels, interleaved);

            int frames = interleaved.Length / channels;
            long size = 44L + interleaved.Length * 2L;
            _bytes += size;
            _files++;

            string seam = looping ? $"{SeamRatio(interleaved, channels):0.00}" : "-";
            Console.WriteLine($"  {name + ".wav",-24}{channels,4}{rate,8}{frames / (double)rate,8:0.00}"
                            + $"{peak,9:0.000}{Rms(interleaved),9:0.000}{Dc(interleaved),9:0.000}{seam,8}");
        }

        private static double Peak(double[] x)
        {
            double p = 0.0;
            foreach (double v in x) { double a = Math.Abs(v); if (a > p) p = a; }
            return p;
        }

        private static double Rms(double[] x)
        {
            double s = 0.0;
            foreach (double v in x) s += v * v;
            return Math.Sqrt(s / x.Length);
        }

        private static double Dc(double[] x)
        {
            double s = 0.0;
            foreach (double v in x) s += v;
            return s / x.Length;
        }

        /// <summary>
        /// How big the jump across the loop point is, measured in average sample steps.
        ///
        /// Comparing the first and last samples on their own says nothing: two adjacent samples
        /// of any real signal differ. What matters is whether the wrap looks like every other
        /// sample boundary in the file, so this is the step across the seam divided by the mean
        /// step everywhere else. One means the seam is indistinguishable; ten means a tick.
        /// </summary>
        private static double SeamRatio(double[] interleaved, int channels)
        {
            double worst = 0.0;
            int frames = interleaved.Length / channels;
            for (int c = 0; c < channels; c++)
            {
                double sum = 0.0;
                for (int i = 1; i < frames; i++)
                    sum += Math.Abs(interleaved[i * channels + c] - interleaved[(i - 1) * channels + c]);
                double mean = sum / (frames - 1);
                double wrap = Math.Abs(interleaved[c] - interleaved[(frames - 1) * channels + c]);
                double ratio = mean > 0.0 ? wrap / mean : 0.0;
                if (ratio > worst) worst = ratio;
            }
            return worst;
        }

        // =========================== deterministic noise ===========================

        /// <summary>
        /// xorshift64, seeded through a splitmix avalanche so that neighbouring seeds do not
        /// give correlated streams. A class rather than a struct because it is threaded through
        /// every synthesis call and a copied PRNG silently repeats itself.
        /// </summary>
        private sealed class Rng
        {
            private ulong _s;

            public Rng(ulong seed)
            {
                ulong z = seed + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                _s = (z ^ (z >> 31)) | 1UL;
            }

            private ulong Bits()
            {
                _s ^= _s << 13;
                _s ^= _s >> 7;
                _s ^= _s << 17;
                return _s;
            }

            public double NextDouble() => (Bits() >> 11) * (1.0 / 9007199254740992.0);
            public double Bipolar() => NextDouble() * 2.0 - 1.0;
            public double Range(double a, double b) => a + (b - a) * NextDouble();
            public int NextInt(int lo, int hiExclusive) => lo + (int)(NextDouble() * (hiExclusive - lo));
        }

        /// <summary>
        /// A single biquad section, RBJ cookbook coefficients. Enough for every filter here,
        /// which is only ever "make this noise sound like that surface".
        /// </summary>
        private sealed class Biquad
        {
            private readonly double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
                _a1 = a1 / a0; _a2 = a2 / a0;
            }

            public static Biquad LowPass(double f, double q, int rate)
            {
                double w = 2.0 * Math.PI * f / rate;
                double alpha = Math.Sin(w) / (2.0 * q), cs = Math.Cos(w);
                return new Biquad((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
            }

            public static Biquad BandPass(double f, double q, int rate)
            {
                double w = 2.0 * Math.PI * f / rate;
                double alpha = Math.Sin(w) / (2.0 * q), cs = Math.Cos(w);
                return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cs, 1 - alpha);
            }

            public double Process(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }
    }
}
