using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// THE LOOP, END TO END: a player walks about, a neighbour is asked what they saw, and what
    /// comes back is English.
    ///
    /// Everything under Noir.Core.Observation and Noir.Core.Witness has been finished, firewalled
    /// and tested for weeks - 4,852 lines - and until 2026-08-07 nothing in the game had ever
    /// asked it a question. VillageHost recorded the player's track into a PlayerTrack that
    /// nothing read. The review's word for it was "a 4,852-line island the running game never
    /// calls", and it was right.
    ///
    /// So this test is not really about the strings. It is the thing that fails the day somebody
    /// changes the pipeline, or the track, or the firewall, and quietly disconnects the
    /// investigation from the game again - which is exactly how it got disconnected the first
    /// time, without anybody noticing for weeks.
    ///
    /// IT ASSERTS THE SHAPE, NOT THE ANSWER. Whether a particular neighbour happened to see the
    /// player depends on their day, the light, the sightlines and where the player walked, and a
    /// test that demanded a sighting would be demanding a coincidence. "Nothing. I never saw
    /// anybody." is a correct and useful answer - an alibi is evidence too - so what is checked is
    /// that a real question was asked and a sayable sentence came back.
    /// </summary>
    public class WitnessPlayTests
    {
        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = 1f;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ANeighbourCanBeAskedWhatTheySaw()
        {
            var host = CityUnderTest.Host;
            Assert.That(host, Is.Not.Null);
            Assert.That(host.People, Is.Not.Null, "no population to ask");

            var player = Object.FindFirstObjectByType<Player>();
            Assert.That(player, Is.Not.Null, "no Player was created by the host");

            player.Toggle();
            Assert.That(player.Walking, Is.True, "the player did not enter the street");

            // Let them land, then run the town's clock hard so the track actually gets minutes in
            // it. RecordWhereThePlayerWas stamps ONE entry per simulated minute, so at ordinary
            // speed this test would spend a minute of real time collecting a minute of history.
            int wasSpeed = host.SpeedIndex;
            host.SpeedIndex = VillageHost.Speeds.Length - 1;      // 300x
            for (int frame = 0; frame < 300; frame++) yield return null;
            host.SpeedIndex = wasSpeed;

            var at = player.Where;
            Assert.That(at, Is.Not.Null, "the player is not standing anywhere");

            var tile = Space3D.TileAt(at.Value);
            var who = host.NearestNeighbour(tile);
            int day = host.Sim != null ? host.Sim.Clock.Day : 0;

            string[] said = host.AskWhatTheySaw(who, day);

            Assert.That(said, Is.Not.Null, "AskWhatTheySaw returned null");
            Assert.That(said.Length, Is.GreaterThan(0),
                "A witness always says SOMETHING - Recollection.AskInEnglish returns the " +
                "saw-nothing sentence rather than an empty array, because \"I saw nobody\" and " +
                "\"nobody asked me\" are the same value to a caller and completely different " +
                "answers to a player.");

            var citizen = host.People.Get(who);
            Debug.Log($"[witness] asked {citizen.FullName} about day {day}, at tile {tile}:");
            foreach (string line in said) Debug.Log("[witness]   " + line);

            foreach (string line in said)
            {
                Assert.That(line, Is.Not.Null.And.Not.Empty, "a blank line is not testimony");
                Assert.That(line.Trim(), Does.EndWith("."), $"not a sentence: {line}");
                Assert.That(line, Does.Not.Contain("\n"), "one sighting is one line");

                // Either they saw nothing, or they say WHEN - a sighting with no time on it is
                // not evidence anybody can use.
                bool sawNothing = line.Contains("never saw anybody");
                bool hasAClock = Regex.IsMatch(line, @"^\d\d:\d\d,");
                Assert.That(sawNothing || hasAClock, Is.True,
                    $"testimony with neither a time nor an alibi: {line}");
            }

            player.Toggle();
            Assert.That(player.Walking, Is.False, "the player did not come back out");
        }
    }
}
