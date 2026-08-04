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

        /// <summary>The scale these styles were built at, so a live Ctrl+= rebuilds them
        /// instead of leaving the map at the old size while the panels grow.</summary>
        private float _builtAt = -1f;

        private void Ready()
        {
            if (_street != null && Mathf.Approximately(_builtAt, VillageUI.Scale)) return;
            _builtAt = VillageUI.Scale;

            // SCALED WITH THE REST OF THE UI. These are drawn over the map rather than in a
            // panel, and they were the smallest text on screen - 13px for a street name and 11
            // for an address, pale blue on near-black. See VillageUI.Scale, which the same
            // Ctrl+= / Ctrl+- adjusts; the labels have to grow with the panels or the map stays
            // unreadable while the inspector gets better.
            _street = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(13f * VillageUI.Scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _street.normal.textColor = new Color(0.55f, 0.93f, 1.00f);

            _address = new GUIStyle(_street)
            {
                fontSize = Mathf.RoundToInt(12f * VillageUI.Scale),
                fontStyle = FontStyle.Normal,
            };
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
                if (line == null || string.IsNullOrEmpty(line.Name)) continue;

                // WALKED ALONG THE PATH, NOT THE AXIS. This used to skip anything that was not
                // straight, which quietly meant THE TWO MOST IMPORTANT ROADS ON THE MAP HAD NEVER
                // BEEN LABELLED - Chicago Street, which is Route 1 and the reason the town is
                // where it is, and Railroad Avenue. Both bend, so both were dropped by the guard
                // and nobody noticed, because a missing label looks exactly like a label that is
                // off screen.
                var path = line.Path;
                if (path == null || path.Length < 1f) continue;

                for (float d = Mathf.Min(Repeat * 0.5f, path.Length * 0.5f);
                     d < path.Length; d += Repeat)
                {
                    var p = path.PointAt(d);
                    var q = path.PointAt(Mathf.Min(path.Length, d + 10f));
                    var at = new Vector2(p.X, p.Y);
                    var ahead = new Vector2(q.X, q.Y);

                    // Five of these change name partway - see StreetAddressing.RealName for why
                    // that is resolved by position here rather than by a second RoadLine.
                    string name = Pretty(StreetAddressing.RealName(line.Name, at));
                    Draw(cam, eye, at, name, _street, StreetReach, ScreenAngle(cam, at, ahead));
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
        ///
        /// A street name is asked to flow along its own street, not sit flat across it - see
        /// ScreenAngle for how that angle is found. Rotated about the label's own centre using
        /// GUI.matrix, which is IMGUI's own way of doing this without a mesh or a font asset.
        /// </summary>
        private void Draw(Camera cam, Vector3 eye, Vector2 at, string text, GUIStyle style,
                          float reach, float screenAngle = 0f)
        {
            var world = Space3D.ToWorld(new Core.Contracts.Vec2(at.x, at.y), 0.4f);
            if ((world - eye).sqrMagnitude > reach * reach) return;

            var p = cam.WorldToScreenPoint(world);
            if (p.z <= 1f) return;                       // behind the camera

            // GUI y runs down the screen and the camera's runs up it.
            // The box grows with the text or a scaled-up street name is clipped by the
            // rectangle it is centred in.
            float w = 220f * VillageUI.Scale, h = 18f * VillageUI.Scale;
            var r = new Rect(p.x - w * 0.5f, Screen.height - p.y - h * 0.5f, w, h);
            if (r.xMax < 0f || r.yMax < 0f || r.x > Screen.width || r.y > Screen.height) return;

            var was = style.normal.textColor;
            _shadow.fontSize = style.fontSize;
            _shadow.fontStyle = style.fontStyle;

            var oldMatrix = GUI.matrix;
            if (screenAngle != 0f)
                GUIUtility.RotateAroundPivot(screenAngle, r.center);

            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _shadow);

            // Fades out over the last third of its range rather than popping off.
            float d = (world - eye).magnitude;
            style.normal.textColor = new Color(was.r, was.g, was.b,
                                               Mathf.Clamp01((reach - d) / (reach * 0.33f)));
            GUI.Label(r, text, style);
            style.normal.textColor = was;

            GUI.matrix = oldMatrix;
        }

        /// <summary>
        /// The on-screen angle to rotate a label so it reads along its own street rather than
        /// flat across it, whatever the camera's own rotation happens to be - Q and E turn the
        /// orbit, and a name that only ever read "north-south" would come out sideways the
        /// moment somebody used them. Projects a step along the street into screen space and
        /// measures the angle THERE, not in world space, which is what makes it agree with the
        /// camera at any orbit angle.
        ///
        /// Flipped 180 degrees whenever that would read the text backwards or upside down -
        /// GUI.Label always draws left-to-right, so "along the street" and "along the street
        /// reversed" are the same physical line but only one of them is legible.
        /// </summary>
        private static float ScreenAngle(Camera cam, Vector2 at, Vector2 ahead)
        {
            var wa = Space3D.ToWorld(new Core.Contracts.Vec2(at.x, at.y), 0.4f);
            var wb = Space3D.ToWorld(new Core.Contracts.Vec2(ahead.x, ahead.y), 0.4f);
            var pa = cam.WorldToScreenPoint(wa);
            var pb = cam.WorldToScreenPoint(wb);

            float dx = pb.x - pa.x;
            float dy = -(pb.y - pa.y);      // GUI y runs down the screen; the camera's runs up it
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            if (angle > 90f || angle < -90f) angle += angle > 0f ? -180f : 180f;
            return angle;
        }

        /// <summary>`chicago` becomes `CHICAGO ST`, `holmes` becomes `HOLMES AVE`.
        ///
        /// The four west-of-Route-1 names (park, perry, stufflebeam, mckibbin) and the
        /// north-of-Attica name (smith) are their OWN roads now, not the same road under a
        /// second name - see relay-rossville.py's EW/NS split, confirmed against Vermilion
        /// County's own tax address data on 2026-08-01 - so each gets its own line here rather
        /// than falling through to the generic uppercase-plus-ST guess.</summary>
        private static string Pretty(string name)
        {
            switch (name)
            {
                case "chicago":     return "CHICAGO ST  ·  ILLINOIS 1";
                case "attica":      return "ATTICA ST";
                case "holmes":      return "HOLMES AVE";
                case "park":        return "PARK";
                case "perry":       return "PERRY";
                case "stufflebeam": return "STUFFLEBEAM";
                case "mckibbin":    return "MCKIBBIN";
                case "smith":       return "SMITH";
                case "watson":      return "WATSON DR";
                case "earlcourt":   return "EARL CT";
                case "dale":        return "DALE";
                case "greenwood":   return "GREENWOOD";
            }
            if (name == "railroad") return "RAILROAD AVE";

            // AN ALLEY HAS NO NAME AND SHOULD NOT BORROW ONE - "ALLEY7 ST" is worse than nothing.
            // But it DOES need its number on it, because the number is how the owner reports one:
            // "alley 3 is wrong" is only actionable if alley 3 says so on the ground.
            if (name.StartsWith("alley")) return "ALLEY " + name.Substring("alley".Length);

            // SAY UNKNOWN RATHER THAN GUESS. These four are deliberate placeholders - the section
            // roads and crossroads were never identified against anything, and calling them
            // "SECTION RD" on the drawing states more than is known. The placeholder id is kept
            // beside it so the owner can say which one he is looking at.
            if (name.StartsWith("section") || name.StartsWith("crossroad"))
                return "UNKNOWN  ·  " + name;

            return name.ToUpperInvariant() + " ST";
        }
    }
}
