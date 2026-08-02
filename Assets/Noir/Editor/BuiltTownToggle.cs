using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// A tick in the Noir menu for VillageHost.ShowBuildings, remembered between sessions.
    ///
    /// WHY IT EXISTS. Pressing Play gives the dark survey plan, which is the right default and
    /// has a written reason (the pack's two house families are Chicago brownstones and Rossville
    /// is not made of those). But EVERYTHING that dresses the town sits behind that one flag -
    /// the buildings, the streets, the greenery, the farm, the powerlines and the CSX rail bed -
    /// and the only way to raise any of it was to edit `ShowBuildings = false` in VillageHost and
    /// remember to put it back. Somebody who pressed Play to look at newly-built ground work and
    /// got a black screen had no way to find out why, which has now happened twice.
    ///
    /// A MENU ITEM RATHER THAN AN EDIT, for the same reason GroundColourToggle is one: the thing
    /// people actually reach for is changing the source and leaving an uncommitted edit in the
    /// tree, which has already regenerated a committed snapshot by accident once.
    ///
    /// It is read once, in VillageHost.Bootstrap, before any mesh or material exists - so ticking
    /// it takes effect on the NEXT press of Play, not the current one. Only one of the two towns
    /// is ever built, so this costs nothing to leave on.
    /// </summary>
    public static class BuiltTownToggle
    {
        private const string Path = "Noir/Show The Built Town (buildings, trees, the railroad)";

        [MenuItem(Path, priority = 21)]
        private static void Toggle()
        {
            bool now = PlayerPrefs.GetInt(VillageHost.BuiltTownKey, 0) != 1;
            PlayerPrefs.SetInt(VillageHost.BuiltTownKey, now ? 1 : 0);
            PlayerPrefs.Save();

            Debug.Log(now
                ? "[town] The built town is ON. Press Play: buildings, streets, greenery, the "
                + "farm, the powerlines and the CSX rail bed. Tab drops you to street level."
                : "[town] Back to the survey plan - the county's own lot lines, and nothing "
                + "standing on them.");
        }

        [MenuItem(Path, validate = true)]
        private static bool ToggleTick()
        {
            Menu.SetChecked(Path, PlayerPrefs.GetInt(VillageHost.BuiltTownKey, 0) == 1);
            return true;
        }
    }
}
