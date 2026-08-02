using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// A tick in the Noir menu for Materials3D.ShowGroundColour.
    ///
    /// Plan mode dims the ground to near-black so the lot lines read, which also hides
    /// everything GroundZoning works out about a tile from real county zoning and real USGS
    /// slope. This turns that dimming off without turning the whole city on, so ground work can
    /// be looked at on its own.
    ///
    /// A MENU ITEM RATHER THAN AN EDIT, because the alternative people actually reach for is
    /// commenting out the dimming and putting it back afterwards - which leaves an uncommitted
    /// change in the tree that has to be remembered, and which regenerated the committed plan
    /// snapshot in full colour by accident once already.
    /// </summary>
    public static class GroundColourToggle
    {
        private const string Path = "Noir/Show Ground Colour (plan mode)";

        [MenuItem(Path, priority = 20)]
        private static void Toggle()
        {
            bool now = !Materials3D.ShowGroundColour;
            Materials3D.ShowGroundColour = now;

            Debug.Log(now
                ? "[ground] Colour ON - press Play, or Noir > Render Plan, to see zoning and slope. "
                + "The plan's own dimming is off until you turn this back."
                : "[ground] Colour OFF - back to the dimmed survey plan.");
        }

        [MenuItem(Path, validate = true)]
        private static bool ToggleTick()
        {
            Menu.SetChecked(Path, Materials3D.ShowGroundColour);
            return true;
        }
    }
}
