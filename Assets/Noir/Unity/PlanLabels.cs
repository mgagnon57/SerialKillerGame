using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The names on the plan: streets down their own centrelines, addresses on their own lots.
    ///
    /// WITHOUT THIS THE DRAWING IS ANONYMOUS. Every line in it is correct - the grid is
    /// Rossville's, the lots are the county's - and none of that is legible to somebody standing
    /// in it, because a street with no name on it is a grey corridor and 408 Holmes Ave is an
    /// eleven-metre rectangle exactly like the four hundred and sixty-five others. A survey
    /// drawing is half geometry and half labelling, and only one half had been built.
    ///
    /// DRAWN IN SCREEN SPACE, not as world text. World-space labels need a font asset, a
    /// material and a mesh per string - a thousand of them is a thousand renderers, and it is
    /// editor work to set up. Projecting a point and drawing at it costs one matrix multiply per
    /// label and no assets at all, which is the same reasoning that keeps everything else here
    /// out of the editor.
    ///
    /// CULLED BY DISTANCE, because that is what makes it readable rather than a wall of text:
    /// street names carry a long way, addresses only appear when you are close enough to be
    /// looking for one.
    /// </summary>
    public sealed class PlanLabels : MonoBehaviour
    {
        private VillageHost _host;

        /// <summary>How far street names and addresses stay legible, in metres.</summary>
        private const float StreetReach = 620f, AddressReach = 90f;

        /// <summary>Metres between repeats of a street name along its own length.</summary>
        private const float Repeat = 240f;

        private GUIStyle _street, _address, _shadow;

        public static PlanLabels Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("PlanLabels");
            go.transform.SetParent(parent, false);
            var it = go.AddComponent<PlanLabels>();
            it._host = host;
            return it;
        }

        private void Ready()
        {
            if (_street != null) return;

            _street = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _street.normal.textColor = new Color(0.55f, 0.93f, 1.00f);

            _address = new GUIStyle(_street) { fontSize = 11, fontStyle = FontStyle.Normal };
            _address.normal.textColor = new Color(1.00f, 0.97f, 0.88f);

            // Everything is drawn twice, black first and offset a pixel. Pale text on a dark
            // plan is legible until it crosses a pale line, and then it is not.
            _shadow = new GUIStyle(_street);
            _shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }

        private void OnGUI()
        {
            if (_host == null || _host.World == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            Ready();

            var eye = cam.transform.position;

            // ---- the streets ----
            foreach (var line in _host.World.Roads.Lines)
            {
                if (line == null || !line.IsStraight || string.IsNullOrEmpty(line.Name)) continue;

                string name = Pretty(line.Name);
                for (float along = line.From; along <= line.To; along += Repeat)
                {
                    var at = line.IsNorthSouth
                        ? new Vector2(line.Centre, along)
                        : new Vector2(along, line.Centre);

                    Draw(cam, eye, at, name, _street, StreetReach);
                }
            }

            // ---- the addresses ----
            //
            // Only places that are homes get one. A shop's name is its trade name and belongs on
            // the shop; a house's name IS its address, which is the whole point of authoring
            // them one at a time.
            var kinds = PlaceKindTable.Current;
            foreach (var place in _host.World.AllPlaces)
            {
                if (place == null) continue;

                var b = place.Bounds;
                var mid = new Vector2(b.X + b.W * 0.5f, b.Y + b.H * 0.5f);
                bool home = kinds.Row(place.Kind).IsHome;

                Draw(cam, eye, mid, place.Name, home ? _address : _street,
                     home ? AddressReach : AddressReach * 2.4f);
            }
        }

        /// <summary>
        /// Put one label on screen, if it is in front of the camera and close enough to read.
        /// </summary>
        private void Draw(Camera cam, Vector3 eye, Vector2 at, string text, GUIStyle style,
                          float reach)
        {
            var world = Space3D.ToWorld(new Core.Contracts.Vec2(at.x, at.y), 0.4f);
            if ((world - eye).sqrMagnitude > reach * reach) return;

            var p = cam.WorldToScreenPoint(world);
            if (p.z <= 1f) return;                       // behind the camera

            // GUI y runs down the screen and the camera's runs up it.
            var r = new Rect(p.x - 110f, Screen.height - p.y - 9f, 220f, 18f);
            if (r.xMax < 0f || r.yMax < 0f || r.x > Screen.width || r.y > Screen.height) return;

            var was = style.normal.textColor;
            _shadow.fontSize = style.fontSize;
            _shadow.fontStyle = style.fontStyle;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _shadow);

            // Fades out over the last third of its range rather than popping off.
            float d = (world - eye).magnitude;
            style.normal.textColor = new Color(was.r, was.g, was.b,
                                               Mathf.Clamp01((reach - d) / (reach * 0.33f)));
            GUI.Label(r, text, style);
            style.normal.textColor = was;
        }

        /// <summary>`chicago` becomes `CHICAGO ST`, `holmes` becomes `HOLMES AVE`.</summary>
        private static string Pretty(string name)
        {
            switch (name)
            {
                case "chicago": return "CHICAGO ST  ·  ILLINOIS 1";
                case "attica":  return "ATTICA ST";
                case "holmes":  return "HOLMES AVE";
            }
            if (name.StartsWith("section") || name.StartsWith("crossroad")) return "SECTION RD";
            return name.ToUpperInvariant() + " ST";
        }
    }
}
