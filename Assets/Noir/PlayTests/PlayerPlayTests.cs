using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// The one question about a player character that a still cannot answer: does the floor hold.
    ///
    /// This city has no colliders by design - the chunker combines everything, so picking walks
    /// the world model rather than casting a ray. A CharacterController is the one thing in the
    /// project that IS physics, and the failure it has when the collision shell is wrong is not
    /// subtle or gradual: the man falls through Rossville and keeps going.
    /// </summary>
    public class PlayerPlayTests
    {
        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = 1f;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ThePlayerCanStandInTheStreet()
        {
            var player = Object.FindFirstObjectByType<Player>();
            Assert.That(player, Is.Not.Null, "no Player was created by the host");

            player.Toggle();
            Assert.That(player.Walking, Is.True,
                        "the player did not enter - is StarterAssets/PlayerArmature imported?");

            // Spawned above the floor and dropped, so this is also the test that the drop lands.
            for (int frame = 0; frame < 240; frame++) yield return null;

            var body = GameObject.Find("PlayerArmature");
            Assert.That(body, Is.Not.Null, "the armature was not instantiated");

            var at = body.transform.position;
            float y = at.y;

            // AGAINST THE GROUND UNDER THEM, NOT AGAINST ZERO. This used to read `y > -1f` and
            // `y < 5f`, which was the same flat-map assumption `Player.Standing` was making: true
            // while the whole map was one plane at nought, and meaningless once ElevationGrid gave
            // it 24m of relief and put the ground under Second Street at +4.2m. Both a man standing
            // correctly on a hill and a man falling through a valley can be at y=3.
            //
            // The floor is CityCollision.Floor - 0.06m - above the local terrain, and a
            // CharacterController sits its origin at its own base, so standing means within about
            // a metre of the height the grid reports HERE, at the point they actually came to rest.
            float ground = ElevationGrid.HeightAt(at.x, -at.z);

            Assert.That(y, Is.GreaterThan(ground - 1f),
                        $"the player fell through the world - ended at y={y:0.00} with the ground "
                      + $"at {ground:0.00}, so the collision shell did not hold");
            Assert.That(y, Is.LessThan(ground + 5f),
                        $"the player never landed - still at y={y:0.00} with the ground "
                      + $"at {ground:0.00}");

            // And they are somewhere in the town rather than off the edge of it.
            var world = CityUnderTest.World;
            Assert.That(at.x, Is.InRange(0f, (float)world.Width), "spawned outside the map in x");
            Assert.That(-at.z, Is.InRange(0f, (float)world.Height), "spawned outside the map in y");

            Debug.Log($"[player] stood at {at} after the drop.");

            player.Toggle();
            Assert.That(player.Walking, Is.False, "the player did not come back out");
        }

        /// <summary>
        /// Stepping out of third person stays over the scene: run somebody over, Tab out to
        /// watch the response — and actually be over the body rather than wherever the overview
        /// camera last sat, which was the measured complaint (owner, 2026-08-16). Asserts on
        /// OrbitCamera.Target in the ground plane; ArriveOver zeroes the pivot's y on purpose.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator LeavingThirdPersonStaysOverTheScene()
        {
            var player = Object.FindFirstObjectByType<Player>();
            var orbit = Object.FindFirstObjectByType<OrbitCamera>();
            Assert.That(orbit, Is.Not.Null, "no OrbitCamera in the scene");

            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");

            // Somewhere well away from wherever the orbit was looking — the
            // ForceCloseBeatsProximityUntilItExpires teleport pattern, controller disabled
            // around the write or CharacterController.Move silently reverts it.
            var far = orbit.Target + new Vector3(250f, 0f, -180f);
            far.x = Mathf.Clamp(far.x, 30f, CityUnderTest.World.Width - 30f);
            far.z = Mathf.Clamp(far.z, -(CityUnderTest.World.Height - 30f), -30f);
            far.y = ElevationGrid.HeightAt(far.x, -far.z) + 0.6f;

            var cc = body.GetComponent<CharacterController>();
            cc.enabled = false;
            body.transform.position = far;
            cc.enabled = true;
            yield return null;

            var stood = body.transform.position;
            player.Toggle();
            yield return null;

            float dx = orbit.Target.x - stood.x, dz = orbit.Target.z - stood.z;
            float off = Mathf.Sqrt(dx * dx + dz * dz);
            Assert.That(off, Is.LessThan(10f),
                $"the orbit landed {off:0.0}m from where the player stepped out - "
              + "Leave() did not hand the camera the scene");
        }
    }
}
