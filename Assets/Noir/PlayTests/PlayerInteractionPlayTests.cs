using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// CityDoors' player-facing API: finding the nearest door, and the timed override that lets
    /// a deliberate Close beat automatic proximity for a while.
    /// </summary>
    public class PlayerInteractionPlayTests
    {
        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = 1f;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        /// <summary>
        /// THE CITY IS BUILT ONCE AND SHARED BY EVERY TEST IN A RUN (CLAUDE.md names this trap,
        /// and both historic "flaky" tests were this shape). The trailing player.Toggle() at the
        /// end of each walking test never runs when an assertion fails mid-test, which would
        /// leave Walking=true, OrbitCamera disabled and the body parked at a door - and the NEXT
        /// test's Toggle() would then Leave() instead of Enter(), deactivate the body, and die on
        /// GameObject.Find returning null, hiding the original red behind an NRE. This runs on
        /// every exit path and puts the player back on the overview camera.
        /// </summary>
        [UnityTearDown]
        public IEnumerator BackToTheOverview()
        {
            var player = Object.FindFirstObjectByType<Player>();
            if (player != null && player.Walking) player.Toggle();
            yield break;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator NearestDoorFindsTheClosestOneInRange()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            Assert.That(doors, Is.Not.Null, "no CityDoors was created by the host");
            Assert.That(doors.Count, Is.GreaterThan(0), "no doors in this town");

            var at = doors.PositionOf(0);

            int nearest = doors.NearestDoor(at, CityDoors.Reach);
            Assert.That(nearest, Is.EqualTo(0), "the door's own position did not find itself");

            int tooFar = doors.NearestDoor(at + new Vector3(10_000f, 0f, 0f), CityDoors.Reach);
            Assert.That(tooFar, Is.EqualTo(-1), "a point 10km away found a door within range");

            yield break;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ForceCloseBeatsProximityUntilItExpires()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            var at = doors.PositionOf(0);

            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");

            // A CharacterController owns its transform's position each frame through its own
            // internal collision state, not through whatever the Transform last had written to
            // it - ThirdPersonController.Update() calls CharacterController.Move() every frame
            // (for gravity alone, even with no input), and Move() recomputes from the
            // controller's own cached capsule position. Writing body.transform.position directly
            // while the controller is enabled gets silently overwritten back to roughly where it
            // already was on the very next frame - confirmed by instrumenting this test: the body
            // stayed ~342m from the door, at its original spawn point, for the whole 60-frame
            // wait. Disabling the controller around the assignment forces it to re-acquire its
            // position from the Transform when it comes back on, which is the standard way to
            // teleport one.
            var cc = body.GetComponent<CharacterController>();
            cc.enabled = false;
            body.transform.position = at;
            cc.enabled = true;

            // A SWING-COMPLETION WAIT MUST BE TIME-BASED, NOT FRAME-COUNTED. IsOpen() on the way
            // shut is an exact-equality check against _shut (it only reads false once the swing
            // has FULLY finished), unlike the way open, which is satisfied by any nonzero
            // movement - so a fixed frame count that happens to cover the open case can still be
            // too little accumulated Time.deltaTime to finish a close. Frontage.cs swings a door
            // 85 degrees at CityDoors' 150 deg/s, ~0.57s; 1.5s is comfortable margin and still
            // well inside the 5s override window used below.
            const float SwingWait = 1.5f;

            float openUntil = Time.time + SwingWait;
            while (Time.time < openUntil) yield return null;   // let it swing open
            Assert.That(doors.IsOpen(0), Is.True, "the door never opened for a standing player");

            doors.Force(0, false);
            float forcedAt = Time.time;
            float shutUntil = Time.time + SwingWait;
            while (Time.time < shutUntil) yield return null;   // let it swing shut
            Assert.That(doors.IsOpen(0), Is.False, "Force(false) did not shut the door");

            // Still well inside the override window - proximity alone would reopen it.
            while (Time.time < forcedAt + 2f) yield return null;
            Assert.That(doors.IsOpen(0), Is.False,
                        "the door reopened while the override should still be active");

            // Past the window - proximity should take control back, and the player is still
            // standing right there, so it should swing open again on its own.
            while (Time.time < forcedAt + 5.5f) yield return null;
            float reopenUntil = Time.time + SwingWait;
            while (Time.time < reopenUntil) yield return null;   // let it swing back open
            Assert.That(doors.IsOpen(0), Is.True,
                        "proximity never took control back after the override expired");

            player.Toggle();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ForceOpenMidOverrideOpensImmediately()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            var at = doors.PositionOf(0);

            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");

            // See ForceCloseBeatsProximityUntilItExpires for why the controller has to be
            // disabled around a raw teleport.
            var cc = body.GetComponent<CharacterController>();
            cc.enabled = false;
            body.transform.position = at;
            cc.enabled = true;

            const float SwingWait = 1.5f;   // see ForceCloseBeatsProximityUntilItExpires

            float openUntil = Time.time + SwingWait;
            while (Time.time < openUntil) yield return null;   // let it swing open
            Assert.That(doors.IsOpen(0), Is.True, "the door never opened for a standing player");

            doors.Force(0, false);
            float forcedAt = Time.time;
            float shutUntil = Time.time + SwingWait;
            while (Time.time < shutUntil) yield return null;   // let it swing shut
            Assert.That(doors.IsOpen(0), Is.False, "Force(false) did not shut the door");

            // WALK AWAY BEFORE FORCING IT BACK OPEN - BUT ONLY 25 m, NOT OUT OF LIVERANGE. This
            // is the point of the test: with the player standing right at the door,
            // SomebodyWithin would already agree the moment the close-override lapses, so a test
            // that force-opens from right there would pass even against the old, buggy
            // Force(true) (which only cleared the override and left reopening to ambient
            // proximity) - proximity alone would happen to produce the same result. Moving away
            // first means proximity says "shut" for as long as the player is gone, so only an
            // explicit force-open mechanism can be the reason the door opens. 25 m clears
            // Hold/Reach (2.9 m / 1.9 m) and CellSize's 3x3 neighbourhood (8 m cells) by a wide
            // margin, while staying well inside CityDoors' own LiveRange (55 m) - go further, as
            // the "no menu" case in OffersTheNearestDoorsMenuAndSwitchesVerbOnState does with
            // 1000 m, and the door drops out of Update's own "near" gate entirely and never eases
            // its swing at all, which would make this test fail for the wrong reason.
            cc.enabled = false;
            body.transform.position = at + new Vector3(25f, 0f, 0f);
            cc.enabled = true;

            // TIME-BASED, NOT FRAME-COUNTED, same reasoning as SwingWait above: long enough for
            // Rehash (every RehashFrames = 12 frames) to run at least once even on a slow frame
            // rate and record the player's new, distant position, so SomebodyWithin(_at[0], ...)
            // reliably reads false from here on rather than stale data from when the player still
            // stood there.
            float settledAt = Time.time + SwingWait;
            while (Time.time < settledAt) yield return null;
            Assert.That(doors.IsOpen(0), Is.False, "door should still be shut with nobody near it");

            // Still well inside the close override's 5s window - proximity alone could not have
            // reopened it, and now genuinely cannot (nobody is near). Force(true) here must open
            // it anyway, promptly, not by waiting out the override or the next rehash.
            Assert.That(Time.time, Is.LessThan(forcedAt + 4.5f),
                        "test took too long to reach the mid-override Force(true) - window may "
                      + "have already lapsed, which would make this assertion meaningless");
            doors.Force(0, true);

            // A small, generous, time-based wait - not a frame count and not the override window
            // itself - so this fails if Force(true) merely cleared the close-override and left
            // the door to be picked up by ambient proximity/rehash timing (which, with nobody
            // near it, would never happen at all) instead of opening it directly.
            float reopenUntil = Time.time + SwingWait + 1f;
            bool reopened = false;
            while (Time.time < reopenUntil)
            {
                if (doors.IsOpen(0)) { reopened = true; break; }
                yield return null;
            }
            Assert.That(reopened, Is.True,
                        "Force(0, true) mid-override, with nobody near the door, did not open it "
                      + "promptly - it must not depend on proximity");

            cc.enabled = false;
            body.transform.position = at;
            cc.enabled = true;
            player.Toggle();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator OffersTheNearestDoorsMenuAndSwitchesVerbOnState()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            var interaction = Object.FindFirstObjectByType<PlayerInteraction>();
            Assert.That(interaction, Is.Not.Null, "no PlayerInteraction was created by the host");

            var at = doors.PositionOf(0);
            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;

            var body = GameObject.Find("PlayerArmature");

            // See ForceCloseBeatsProximityUntilItExpires for why: a CharacterController owns its
            // transform every frame through CharacterController.Move(), so a raw teleport while
            // it is enabled is silently reverted on the next frame. Disable it around the write,
            // same as that test.
            var cc = body.GetComponent<CharacterController>();

            cc.enabled = false;
            body.transform.position = at + new Vector3(1000f, 0f, 0f);   // far from every door
            cc.enabled = true;
            yield return null;
            Assert.That(interaction.Current, Is.Null, "offered a menu with nobody near a door");

            cc.enabled = false;
            body.transform.position = at;
            cc.enabled = true;
            yield return null;
            Assert.That(interaction.Current, Is.Not.Null, "no menu offered standing at a door");
            Assert.That(interaction.Current.Verbs,
                        Is.EqualTo(doors.IsOpen(0) ? new[] { "Close" } : new[] { "Open" }));

            player.Toggle();
        }

        /// <summary>
        /// THE GATE THIS FILE WAS MISSING, and the reason it was missing an entire bug class:
        /// every test above asserts on CityDoors' ANGLE BOOKKEEPING (IsOpen), which is blind to
        /// whether anything on screen moves. On 2026-08-15 the whole suite was green while all
        /// 589 door leaves in Rossville had been destroyed at startup - CityChunker.Bake combined
        /// each leaf into a static chunk at its shut pose and DestroyImmediated it, leaving
        /// CityDoors swinging childless hinge transforms. Invisible in every log, count and test.
        /// This asks the town that is RUNNING whether each registered hinge still has a renderer
        /// to swing.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator EveryDoorHingeKeepsItsLeafThroughTheBake()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            Assert.That(doors, Is.Not.Null, "no CityDoors was created by the host");
            Assert.That(doors.Count, Is.GreaterThan(0), "no doors in this town");
            Assert.That(doors.Leafless(), Is.Zero,
                        "hinges with no door leaf renderer beneath them - CityChunker.Bake ate "
                      + "the leaves again, so every swing in town (Force and proximity alike) is "
                      + "invisible. It was 589 of 589 when this was first measured.");
            yield break;
        }

        /// <summary>
        /// The action half of the E-key seam: PerformOffered() is exactly what pressing E does,
        /// minus the literal Keyboard.current read (three lines that cannot meaningfully be
        /// tested without forging a keyboard). Standing at a door that proximity has opened, the
        /// offered verb is Close, and performing it must actually shut the door.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator PerformingTheOfferedVerbForcesTheDoor()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            var interaction = Object.FindFirstObjectByType<PlayerInteraction>();
            var at = doors.PositionOf(0);

            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");

            // See ForceCloseBeatsProximityUntilItExpires for why the controller must be
            // disabled around a raw teleport.
            var cc = body.GetComponent<CharacterController>();
            cc.enabled = false;
            body.transform.position = at;
            cc.enabled = true;

            const float SwingWait = 1.5f;   // see ForceCloseBeatsProximityUntilItExpires

            float openUntil = Time.time + SwingWait;
            while (Time.time < openUntil) yield return null;   // let proximity swing it open
            Assert.That(doors.IsOpen(0), Is.True, "the door never opened for a standing player");
            Assert.That(interaction.Current, Is.Not.Null, "no verb offered standing at the door");
            Assert.That(interaction.Current.Verbs[0], Is.EqualTo("Close"),
                        "an open door should offer Close");

            interaction.PerformOffered();                      // what pressing E does

            float shutUntil = Time.time + SwingWait;
            while (Time.time < shutUntil) yield return null;   // let it swing shut
            Assert.That(doors.IsOpen(0), Is.False,
                        "performing the offered verb (Close) did not shut the door");

            player.Toggle();
        }
    }
}
