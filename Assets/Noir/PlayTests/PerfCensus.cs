using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// WHAT IS ACTUALLY EATING THE FRAME, MEASURED INSIDE ONE RUN.
    ///
    /// WHY THIS EXISTS, AND IT IS A CONFESSION. The frame rate was chased for a whole session by
    /// comparing how long the PlayMode suite took BETWEEN runs - 368 s, 688 s, 728 s, 708 s - and
    /// two changes were made and reverted on the strength of it. Then the control run came back at
    /// 708 s with code functionally identical to the 368 s one. **Suite duration does not repeat on
    /// this machine, so every conclusion drawn from comparing runs was worthless**, including the
    /// confident ones.
    ///
    /// The fix is to stop comparing runs. Everything here is measured inside a SINGLE run, seconds
    /// apart, by switching one layer off and straight back on. Thermal drift, background load and
    /// whatever else moved between runs are common to every sample and cancel.
    ///
    /// AND THE BASELINE IS MEASURED TWICE, at the start and at the end. If the two disagree, the
    /// instrument drifted DURING the run and every number between them is suspect - which is the
    /// thing nobody checked last time. It is reported either way.
    ///
    /// VSYNC OFF FOR THE DURATION. With it on, every frame reads 16.7 ms and the whole census says
    /// nothing at all. Restored afterwards, along with every layer switch - `Layers.Set` writes
    /// PlayerPrefs, and a test that leaves them changed has already broken this suite once.
    /// </summary>
    public class PerfCensus
    {
        /// <summary>
        /// Frames thrown away after a change before believing anything.
        ///
        /// GENEROUS BECAUSE THE TOGGLE ITSELF SHOWS UP IN THE MEASUREMENT. At 45 frames the layer
        /// with the most renderers to switch read SLOWER with itself turned off - `Driveways OFF`
        /// once, `People OFF` (1,385 renderers) another time, both by about 3-5 ms, which is not a
        /// thing that can be true. Flipping `enabled` on a thousand-odd renderers dirties Unity's
        /// culling and batching for longer than 45 frames at 200 fps, which is a fifth of a second.
        ///
        /// So the "saves" column is only as trustworthy as this number, and a NEGATIVE saving is
        /// the instrument talking rather than the town. The baseline pair either side of the run
        /// is the measurement to lean on - it brackets everything and needs no toggle at all.
        /// </summary>
        private const int Settle = 150;

        /// <summary>Frames in one sample. At ~30 fps this is about four seconds.</summary>
        private const int Sample = 120;

        /// <summary>The layers worth asking about. Cheap ones are not worth four seconds each.</summary>
        private static readonly Layers.Kind[] Suspects =
        {
            Layers.Kind.People,
            Layers.Kind.Traffic,
            Layers.Kind.Driveways,
            Layers.Kind.Trees,
            Layers.Kind.Buildings,
            Layers.Kind.Districts,
            Layers.Kind.Houses,
            Layers.Kind.Streets,
            Layers.Kind.Lamps,
            Layers.Kind.Powerlines,
            Layers.Kind.Farm,
            Layers.Kind.Massing,
        };

        private sealed class Reading
        {
            public string What;
            public float Median, P90;
            public int Renderers;
        }

        [UnityTest, Category("Diagnostic"), Timeout(1800000)]
        public IEnumerator WhatIsEatingTheFrame()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            int vsync = QualitySettings.vSyncCount;
            int target = Application.targetFrameRate;
            float scale = Time.timeScale;

            var was = new Dictionary<Layers.Kind, bool>();
            foreach (var kind in Suspects) was[kind] = Layers.IsOn(kind);

            var readings = new List<Reading>();
            Reading first = null, last = null;

            // yield inside try/finally is legal in a C# iterator; a catch clause would not be.
            try
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.timeScale = 1f;

                yield return Skip(Settle * 3);            // let the town settle before believing anything

                yield return Measure("baseline, everything on", null, r => first = r);

                foreach (var kind in Suspects)
                {
                    if (!Layers.IsWired(kind) || !Layers.IsOn(kind)) continue;

                    int renderers = Renderers(kind);
                    Layers.Set(kind, false);
                    yield return Skip(Settle);

                    Reading got = null;
                    yield return Measure($"{Layers.Label(kind)} OFF", renderers, r => got = r);
                    readings.Add(got);

                    Layers.Set(kind, true);
                    yield return Skip(Settle);
                }

                yield return Measure("baseline again", null, r => last = r);
            }
            finally
            {
                foreach (var pair in was) Layers.Set(pair.Key, pair.Value);
                QualitySettings.vSyncCount = vsync;
                Application.targetFrameRate = target;
                Time.timeScale = scale;
            }

            Report(first, last, readings);

            Assert.Pass("a census, not a gate - read the log");
        }

        private static IEnumerator Skip(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>Median and p90 of the next <see cref="Sample"/> frames, in milliseconds.</summary>
        private static IEnumerator Measure(string what, int? renderers, System.Action<Reading> done)
        {
            var ms = new List<float>(Sample);
            for (int i = 0; i < Sample; i++)
            {
                yield return null;
                ms.Add(Time.unscaledDeltaTime * 1000f);
            }
            ms.Sort();

            done(new Reading
            {
                What = what,
                Median = ms[ms.Count / 2],
                P90 = ms[Mathf.Min(ms.Count - 1, ms.Count * 9 / 10)],
                Renderers = renderers ?? -1,
            });
        }

        private static int Renderers(Layers.Kind kind)
        {
            int n = 0;
            foreach (var root in Layers.RootsOf(kind))
                if (root != null) n += root.GetComponentsInChildren<Renderer>(true).Length;
            return n;
        }

        private static void Report(Reading first, Reading last, List<Reading> readings)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("  ---- WHAT IS EATING THE FRAME ----");
            text.AppendLine($"  baseline start   median {first.Median,6:0.0} ms   p90 {first.P90,6:0.0} ms"
                          + $"   ({1000f / Mathf.Max(first.Median, 0.01f):0} fps)");
            text.AppendLine($"  baseline end     median {last.Median,6:0.0} ms   p90 {last.P90,6:0.0} ms");

            float drift = last.Median - first.Median;
            text.AppendLine($"  DRIFT during the run: {drift:+0.0;-0.0;0.0} ms."
                          + (Mathf.Abs(drift) > first.Median * 0.15f
                             ? "  ** MORE THAN 15% - treat every number below as soft. **"
                             : "  Under 15%, so the numbers below hold."));
            text.AppendLine();

            float baseline = (first.Median + last.Median) * 0.5f;
            readings.Sort((a, b) => (baseline - b.Median).CompareTo(baseline - a.Median));

            text.AppendLine("  layer switched off          median        saves    renderers");
            foreach (var r in readings)
                text.AppendLine($"  {r.What,-26}{r.Median,6:0.0} ms   {baseline - r.Median,6:0.0} ms"
                              + $"   {r.Renderers,9}");

            Debug.Log(text.ToString());
        }
    }
}
