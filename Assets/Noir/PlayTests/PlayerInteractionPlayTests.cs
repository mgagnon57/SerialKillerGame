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
    }
}
