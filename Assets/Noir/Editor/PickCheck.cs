using System;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Points at every building in the city and checks that clicking it would select it.
    ///
    /// Picking has two failure modes and neither of them shows up in a compile. The tile lookup
    /// can be off - village y runs into Unity -z, and getting that backwards selects a building
    /// mirrored across the map. And the ray walk can be too coarse, stepping straight over a
    /// narrow terrace seen edge-on.
    ///
    /// Two shots at each building: straight down on it, which tests the lookup, and from the
    /// street outside its own front door at eye height, which tests the walk against a facade -
    /// the case that a flat ground projection gets wrong and the reason the walk exists.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PickCheck.Run -logFile &lt;log&gt;
    /// </summary>
    public static class PickCheck
    {
        [MenuItem("Noir/Check Building Picking")]
        public static void Run()
        {
            GameObject city = null;
            int fromAbove = 0, fromTheStreet = 0, tested = 0, missedAbove = 0, missedStreet = 0;
            int doorless = 0;

            try
            {
                var built = TownPipeline.Build();
                var world = built.World;

                // The models have to go up: heights are measured while they are built, and
                // without them every building is treated as flat ground.
                city = new GameObject("PickCheck");
                CityBuildings.Build(world, city.transform);

                foreach (var place in world.AllPlaces)
                {
                    if (!CityBuildings.Handles(place)) continue;
                    tested++;

                    var lot = place.Bounds;
                    float cx = lot.X + lot.W / 2f;
                    float cy = lot.Y + lot.H / 2f;
                    float top = CityBuildings.TryHeight(place.Id, out float t) ? t : 0f;

                    if (top <= 0.1f)
                        Debug.LogWarning($"[pick] {place.Name} has no measured height.");

                    // ---- 1. straight down ----
                    var above = new Ray(new Vector3(cx, 300f, -cy), Vector3.down);
                    var hit = PlacePicker.Pick(world, above);
                    if (hit.Value == place.Id.Value) fromAbove++;
                    else
                    {
                        missedAbove++;
                        Debug.LogError($"[pick] from above, {place.Name} at {lot} gave "
                                     + $"'{world.GetPlace(hit)?.Name ?? "nothing"}'");
                    }

                    // ---- 2. from the street, at eye height ----
                    //
                    // Stand outside the front door looking back at the middle of the facade.
                    // The door is on the lot's boundary, so the way out of the building is
                    // simply the way from its middle towards its door.
                    // A silo has no front door, so there is nowhere to stand outside it. Counted
                    // separately rather than silently, or the two totals disagree and the reader
                    // is left wondering which buildings failed.
                    var door = place.Door;
                    if (!door.IsValid) { doorless++; continue; }

                    var outward = new Vector3(door.X + 0.5f - cx, 0f, -(door.Y + 0.5f - cy));
                    if (outward.sqrMagnitude < 0.01f) continue;
                    outward.Normalize();

                    var eye = new Vector3(door.X + 0.5f, 1.6f, -(door.Y + 0.5f)) + outward * 9f;
                    var aim = new Vector3(cx, Mathf.Min(3f, top * 0.5f), -cy);

                    var street = new Ray(eye, (aim - eye).normalized);
                    var seen = PlacePicker.Pick(world, street);
                    if (seen.Value == place.Id.Value) fromTheStreet++;
                    else
                    {
                        missedStreet++;
                        Debug.LogError($"[pick] from the street, {place.Name} gave "
                                     + $"'{world.GetPlace(seen)?.Name ?? "nothing"}'");
                    }
                }

                Debug.Log($"[pick] {tested} buildings: {fromAbove} found from above, "
                        + $"{fromTheStreet} of {tested - doorless} found from the street "
                        + $"({doorless} have no door to stand outside).");
                Debug.Log(missedAbove + missedStreet == 0
                    ? "[pick] VERDICT: every building can be clicked."
                    : $"[pick] VERDICT: {missedAbove} missed from above, "
                    + $"{missedStreet} missed from the street.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[pick] FAILED: " + ex);
            }
            finally
            {
                if (city != null) UnityEngine.Object.DestroyImmediate(city);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
