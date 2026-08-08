using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// WHY IS NOBODY MOVING THEIR LEGS.
    ///
    /// A person gliding across the ground is an Animator that is not playing, and there are five
    /// separate reasons that can happen - no controller, no avatar, an avatar that is not
    /// humanoid, a state the controller has never heard of, or the animator being culled - and
    /// every one of them looks identical on screen: the figure slides about in its bind pose.
    ///
    /// So this asks each of the five directly rather than guessing, which is the only way to tell
    /// a bug in the wiring from a missing tool.
    /// </summary>
    public class PeopleDiagnostics
    {
        private int _speedWas = -1;

        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = 1f;
            yield return CityUnderTest.WaitUntilBuilt();
            _speedWas = CityUnderTest.Host.SpeedIndex;
        }

        /// <summary>
        /// Put the clock back the way it was found.
        ///
        /// The city is built once and shared by every test in the run, so the speed this one
        /// needs - 1x, the only speed a playback rate means anything at - is left behind for
        /// whoever runs next. It cost an hour: the next test along waits a few frames and asks
        /// whether the clock moved, and at 1x in batchmode a few frames are a fraction of a
        /// game second, so it read a stopped simulation and blamed the traffic.
        /// </summary>
        [TearDown]
        public void PutTheClockBack()
        {
            if (_speedWas >= 0 && CityUnderTest.Host != null)
                CityUnderTest.Host.SpeedIndex = _speedWas;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator WhyAreThePeopleNotAnimating()
        {
            // A few seconds of real time so somebody is actually walking somewhere.
            for (int frame = 0; frame < 180; frame++) yield return null;

            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            Debug.Log($"[body] {animators.Length} Animators in the scene.");

            int noController = 0, noAvatar = 0, notHuman = 0, disabled = 0, culled = 0, ok = 0;
            var states = new Dictionary<string, int>();
            var modes = new Dictionary<string, int>();
            int shown = 0;

            foreach (var a in animators)
            {
                if (a == null) continue;

                string mode = a.cullingMode.ToString();
                modes[mode] = modes.TryGetValue(mode, out int m) ? m + 1 : 1;

                if (!a.enabled) { disabled++; continue; }
                if (a.runtimeAnimatorController == null) { noController++; continue; }
                if (a.avatar == null) { noAvatar++; continue; }
                if (!a.avatar.isHuman) { notHuman++; continue; }

                // The one that catches culling: an animator that is not being updated reports a
                // normalisedTime that never advances.
                var info = a.GetCurrentAnimatorStateInfo(0);
                if (!a.isActiveAndEnabled) { culled++; continue; }

                ok++;

                if (shown++ < 6)
                {
                    Debug.Log($"[body] {a.gameObject.name}: state hash {info.shortNameHash}, "
                            + $"t={info.normalizedTime:0.00}, speed={a.speed}, "
                            + $"culling={a.cullingMode}, "
                            + $"hasWalking={a.HasState(0, Animator.StringToHash("Walking"))}, "
                            + $"clips={a.runtimeAnimatorController.animationClips.Length}");
                }
            }

            Debug.Log($"[body] controller missing {noController}, avatar missing {noAvatar}, "
                    + $"not humanoid {notHuman}, disabled {disabled}, culled {culled}, "
                    + $"apparently fine {ok}.");

            foreach (var m in modes) Debug.Log($"[body] culling {m.Key}: {m.Value}");

            // Does the same animator's clock actually advance between two moments? That is the
            // difference between "configured correctly" and "playing".
            var sample = new List<(Animator a, float t)>();
            foreach (var a in animators)
            {
                if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                sample.Add((a, a.GetCurrentAnimatorStateInfo(0).normalizedTime));
                if (sample.Count >= 40) break;
            }

            for (int frame = 0; frame < 60; frame++) yield return null;

            int moved = 0;
            foreach (var (a, was) in sample)
            {
                if (a == null) continue;
                if (!Mathf.Approximately(a.GetCurrentAnimatorStateInfo(0).normalizedTime, was)) moved++;
            }

            Debug.Log($"[body] of {sample.Count} animators watched over a second, "
                    + $"{moved} advanced their clip and {sample.Count - moved} did not.");

            // ---- and do they play the right thing while walking? ----
            //
            // The sim opens at six in the morning, when the honest answer for most of Rossville is
            // that they are asleep behind a wall - so a state count taken now says nothing. What
            // matters is narrower and can be asked at any hour: of the people the simulation says
            // are ON THE MOVE, how many are in the state they should be? A person walking while
            // their animator sits in an idle IS the gliding.
            var view = Object.FindFirstObjectByType<AgentMeshView>();
            Assert.That(view, Is.Not.Null, "no AgentMeshView - are the people drawn?");

            var host = CityUnderTest.Host;

            // 1x, because that is the speed the playback rate can be checked at. Faster and the
            // clamp is doing the work and the number says nothing about whether the match is right.
            host.SpeedIndex = 3;

            int sampled = 0, wrong = 0, hours = 0;
            float slowest = float.MaxValue, fastest = 0f, rates = 0f;

            // DRIVEN, NOT WAITED FOR. Batchmode frames are quick, so a frame is worth a fraction
            // of a game second and no amount of yielding gets Rossville out of bed - eight rounds
            // of ninety frames reached six minutes past six, which is a town where the only honest
            // answer is that everybody is asleep. Ticking the simulation directly walks the clock
            // to the hours where people are actually out.
            for (int hour = 7; hour <= 18; hour += 2)
            {
                // 20 ticks a second of game time, so an hour is 72,000 of them.
                int want = hour * 60;
                int guard = 0;
                while (host.Sim.Clock.MinuteOfDay < want && guard++ < 400000) host.Sim.Tick();

                // Long enough for a quarter-second crossfade to finish. Sampling mid-fade counts a
                // person who is correctly on their way into the walk as being in the wrong state,
                // which is a false alarm rather than a finding.
                for (int frame = 0; frame < 40; frame++) yield return null;

                var census = view.Report();
                Debug.Log($"[body] {host.Sim.Clock.MinuteOfDay / 60:00}:"
                        + $"{host.Sim.Clock.MinuteOfDay % 60:00}  {census}");

                if (census.Moving == 0) continue;
                sampled += census.Moving;
                wrong += census.Wrong;
                rates += census.Rate;
                hours++;
                slowest = Mathf.Min(slowest, census.Slowest);
                fastest = Mathf.Max(fastest, census.Fastest);
            }

            Assert.That(sampled, Is.GreaterThan(0),
                "nobody walked all day, so this proves nothing about walking");

            // THE GLIDING TEST. Somebody the simulation is moving whose animator sits in an idle
            // is a figure sliding across the ground on stiff legs, which is the whole complaint.
            //
            // A FRACTION, NOT A COUNT. This was `wrong == 0`, which held while a sample was
            // twenty people and became a coin toss at five hundred: the census reads the
            // simulation's idea of who is moving and the animator's state in the same pass, and
            // somebody who starts walking between those two reads is counted wrong for one
            // frame. One in 519 is that. The fault this exists to catch - the whole town sliding
            // about because no animator is playing - is not one in five hundred, it is all of
            // them, so a couple of percent is the honest line and still catches it by a mile.
            float astray = sampled > 0 ? wrong / (float)sampled : 0f;
            Assert.That(astray, Is.LessThan(0.02f),
                $"{wrong} of {sampled} people were walking with the wrong clip playing");

            // AND THE SKATING TEST, which is the subtler half. The right clip played at the wrong
            // rate still slides - the feet plant and are then dragged along the ground.
            //
            // Gated on the AVERAGE over everybody walking, at each hour, because the extremes are
            // not evidence: one person whose last simulation step happened to fall on zero reads
            // as 0.00x for that frame and is a person about to stop, not a fault. The band is wide
            // on purpose - what it has to catch is the match not running at all, which pins the
            // rate at exactly 1.00x, or giving up at the ceiling, which pins it at 2.00x.
            float mean = hours > 0 ? rates / hours : 0f;
            Assert.That(mean, Is.GreaterThan(0.5f).And.LessThan(1.4f),
                $"the walk averaged {mean:0.00}x over {hours} hours "
              + $"(span {slowest:0.00}-{fastest:0.00}) - the feet will skate");
            Assert.That(mean, Is.Not.EqualTo(1f).Within(0.001f),
                "every walk played at exactly 1.00x, so the rate match is not running at all");
        }
    }
}
