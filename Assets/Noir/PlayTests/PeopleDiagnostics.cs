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

            // ---- and does the SKELETON actually move? ----
            //
            // THE STRONGEST FINDING IN THE AUDIT, AND IT HAS BEEN INVISIBLE FOR MONTHS. The line
            // above printed "1 of 40 advanced" in one recorded run, "0 of 40" in the next and
            // "40 of 0" in the third - and ALL THREE PASSED, because nothing asserts on it. A clip
            // whose normalizedTime advances is not the same claim as a person who moves: a figure
            // can sit in its bind pose with the state machine ticking happily behind it.
            //
            // So this asks the bones. A hip that has not moved a millimetre in a second, while its
            // animator says it is playing, is a T-posing figure.
            //
            // MEASURED IN LOCAL SPACE. Two world positions a kilometre out carry about 1.2e-4 m of
            // float error, which is only eight times under a millimetre threshold - close enough
            // to turn precision into a verdict. Local space has no such offset.
            //
            // AlwaysAnimate is FORCED for the measurement, or the answer is about the camera
            // rather than the rig - and restored in try/finally rather than a [TearDown], which a
            // yield break can skip straight past.
            // Through the view's own hierarchy, which excludes the deactivated away-figures BY
            // CONSTRUCTION. Walking `_figures` by index picks them up instead, and about four in
            // twenty-four are out of town at any hour - they would count as failures for no fault.
            var peopleRoot = Object.FindFirstObjectByType<AgentMeshView>();
            Assert.That(peopleRoot, Is.Not.Null, "no AgentMeshView - are the people drawn?");
            var rigged = peopleRoot.GetComponentsInChildren<Animator>();
            var hips = new List<(Animator a, Transform bone, Vector3 was)>();
            var modesWere = new List<(Animator a, AnimatorCullingMode mode)>();

            try
            {
                foreach (var a in rigged)
                {
                    if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                    if (!a.isHuman) continue;

                    var hip = a.GetBoneTransform(HumanBodyBones.Hips);
                    if (hip == null) continue;

                    modesWere.Add((a, a.cullingMode));
                    a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    hips.Add((a, hip, hip.localPosition));
                    if (hips.Count >= 24) break;
                }

                for (int frame = 0; frame < 60; frame++) yield return null;

                int still = 0;
                float worst = 0f;
                foreach (var (a, bone, was) in hips)
                {
                    float shift = Vector3.Distance(bone.localPosition, was);
                    if (shift > worst) worst = shift;
                    if (shift < 0.001f) still++;
                }

                Debug.Log($"[body] SKELETONS: of {hips.Count} rigged figures watched for a second, "
                        + $"{hips.Count - still} moved a hip bone and {still} did not. "
                        + $"Largest shift {worst * 1000f:0.0} mm. "
                        + "A figure whose animator is playing and whose hips never move is T-posing.");

                // RATCHETED FROM W4'S MEASUREMENT, WHICH WAS 24 OF 24 MOVING.
                //
                // The bar is a quarter rather than zero because a still figure is not automatically
                // a fault - a clip can genuinely hold a pose for a beat, and one unlucky sample
                // should not turn the six-minute gate red. What this has to catch is the failure
                // the log spent months unable to show: a town where the animators tick and NOTHING
                // MOVES. That reads as all of them, not a quarter.
                Assert.That(still, Is.LessThanOrEqualTo(Mathf.Max(1, hips.Count / 4)),
                    $"{still} of {hips.Count} rigged figures did not move a hip bone in a whole "
                  + $"second while their animator was playing. Largest shift {worst * 1000f:0.0} mm. "
                  + "That is a T-posing town, and the clip-advance line above cannot see it.");
            }
            finally
            {
                foreach (var (a, mode) in modesWere) if (a != null) a.cullingMode = mode;
            }

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

                // DOT-7, RATCHETED LIVE. W4 measured this at zero every hour. A person whose
                // wanted clip has no state in the controller is FROZEN by Drive - deliberately,
                // since freezing is honest where treadmilling was a lie - but frozen is still a
                // person not animating, and the whole dotted-clip fault was exactly this going
                // uncounted. Above the Moving check, because its victims stand at doors.
                Assert.That(census.Stateless, Is.EqualTo(0),
                    $"{census.Stateless} people want a clip the controller has no state for, so "
                  + "they are frozen. Either a row names a clip nobody downloaded, or the "
                  + "controller is stale - rebuild with Noir/Build The Townsfolk Animator.");

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
            // THE FLOOR IS 0.35, AND HERE IS WHERE THAT NUMBER COMES FROM.
            //
            // It was 0.50 against a measured 0.54, which is four hundredths of clearance - a gate
            // that fires on its own without anybody changing anything, and this run measured 0.54,
            // 0.62 and 0.72 on different hours. The floor was not describing a fault; it was
            // describing this town.
            //
            // The clip is authored at 1.5 m/s. Rossville's adults walk 1.19-1.51 m/s BEFORE
            // terrain slows them, so the average villager's honest ceiling is about
            // 1.35 / 1.5 = 0.90x and the slow end sits near 1.19 / 1.5 = 0.79x - and then terrain,
            // door pauses and the sampling of people about to stop pull the hourly mean well under
            // that. A floor of 0.35 is comfortably below anything the town produces and still
            // catches the two failures that matter, which are the ones the band was always for:
            // the match not running at all (pinned at exactly 1.00x, caught by the assert below)
            // and the match giving up at the ceiling (pinned at 2.00x, caught by the 1.4 above).
            //
            // NOT FIXED BY RE-AUTHORING THE 1.5. That figure is a FACT ABOUT THE CLIP measured off
            // its own root motion, not a dial - lowering it to flatter the ratio would put the
            // skate straight back in for everybody. Owner's decision, 2026-08-08.
            float mean = hours > 0 ? rates / hours : 0f;
            Assert.That(mean, Is.GreaterThan(0.35f).And.LessThan(1.4f),
                $"the walk averaged {mean:0.00}x over {hours} hours "
              + $"(span {slowest:0.00}-{fastest:0.00}) - the feet will skate");
            Assert.That(mean, Is.Not.EqualTo(1f).Within(0.001f),
                "every walk played at exactly 1.00x, so the rate match is not running at all");
        }
    }
}
