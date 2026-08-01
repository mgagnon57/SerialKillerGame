using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Which building the mouse is pointing at.
    ///
    /// ASKS THE MAP, NOT THE GEOMETRY. There are no colliders in this city and there should not
    /// be: CityChunker combines four hundred prefabs into a handful of meshes, so a physics
    /// raycast would hit "chunk 7, material 3" and have no idea which building that was. But the
    /// world already knows - WorldBuilder claims every tile of a place's footprint for it, so a
    /// point on the ground answers the question directly.
    ///
    /// THE RAY IS WALKED RATHER THAN FLATTENED. Projecting the click onto the ground plane and
    /// looking up that tile is the obvious version and it is wrong wherever the camera is low:
    /// pointing at the front of a terrace, the ray meets the ground somewhere behind it, and you
    /// select the building on the other side of the street. So this steps along the ray and
    /// takes the first place whose footprint is under the ray AND whose roof is above it, which
    /// is the same test a collider would do and needs nothing built to do it.
    ///
    /// Heights come from CityBuildings, which measured them when it put the models up. A place
    /// with no recorded height - open ground, a park, the railway corridor - is flat, so it is
    /// found when the ray reaches the ground, which is exactly right for a park.
    /// </summary>
    public static class PlacePicker
    {
        /// <summary>Fine enough not to step over a 6m townhouse seen edge-on.</summary>
        private const float Step = 0.6f;

        /// <summary>Past the far corner of a 240m city seen from the top of the orbit.</summary>
        private const float Range = 900f;

        public static PlaceId Pick(WorldModel world, Ray ray)
        {
            if (world == null) return PlaceId.None;

            // THE BUILDING YOU ARE STANDING IN DOES NOT COUNT. At street level the camera is
            // regularly inside something, and without this every click from in there selects
            // whatever you are standing in rather than what you pointed at - because the ray
            // starts inside its walls and hits them before it has gone anywhere.
            //
            // Tested against its HEIGHT and not just its footprint, so that standing on a road
            // under the elevated railway - whose lot is the street's own corridor - still leaves
            // the railway clickable. It is only "inside" if it is over your head.
            var standingIn = Inside(world, ray.origin);

            for (float t = 0f; t < Range; t += Step)
            {
                var at = ray.GetPoint(t);

                // Below the ground and still nothing: there is nothing further to hit.
                if (at.y < -0.5f) break;

                var id = At(world, at);
                if (!id.IsValid || id.Value == standingIn.Value) continue;

                // Under this building's roof, or standing on flat ground that belongs to it.
                if (at.y <= TopOf(id)) return id;
            }

            // Nothing was struck on the way down, so fall back to where the ray meets the
            // ground. This is what catches everything the city renderer never built a model
            // for - Ashcombe's own buildings, and any place that is simply a patch of land.
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) return PlaceId.None;

            var hit = ray.GetPoint(enter);
            var direct = At(world, hit);
            if (direct.IsValid) return direct;

            // ON THE PLAN, THE VISIBLE LOT IS THE COUNTY PARCEL AND NOT THIS. A house's real
            // frontage is 26m by 51m; the tile footprint WorldBuilder actually claimed for it -
            // the synthetic block layout's own house, 11m by 7m - sits somewhere inside that
            // real lot but rarely fills it. With the footprint rectangle no longer drawn (see
            // CityOutlines' showFootprints flag), a click anywhere in the visible property that
            // is not directly over that small claimed patch used to find nothing and open
            // nothing, which reads as "clicking does not work" rather than "you missed by three
            // metres". So a direct miss now widens into the nearest owned tile close by, which
            // is what the plan's own visible boundary implies should be clickable.
            return NearestOwned(world, hit);
        }

        /// <summary>
        /// How far off a direct hit is still "this property" rather than the empty street
        /// beside it. Has to clear a real lot's own half-diagonal - median parcel 26m x 51m, so
        /// corner to centre is ~29m - or a click near the corner of a large real lot falls
        /// through to OrbitCamera's ParcelIndex fallback and reports the lot as undeveloped even
        /// though a house sits on it. The first value here (18m) was sized off the footprint's
        /// own dimensions and never checked against the real lot it was meant to cover.
        /// </summary>
        private const float NearbyRadius = 35f;

        private static PlaceId NearestOwned(WorldModel world, Vector3 hit)
        {
            int cx = Mathf.FloorToInt(hit.x), cy = Mathf.FloorToInt(-hit.z);
            int r = Mathf.CeilToInt(NearbyRadius);

            PlaceId best = PlaceId.None;
            float bestDist = float.MaxValue;

            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;

                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > NearbyRadius || dist >= bestDist) continue;

                var id = world.Grid.PlaceAt(x, y);
                if (!id.IsValid) continue;

                bestDist = dist;
                best = id;
            }
            return best;
        }

        /// <summary>How high this place reaches. Open ground is a whisker above nothing.</summary>
        private static float TopOf(PlaceId id) =>
            CityBuildings.TryHeight(id, out float measured) ? measured : 0.4f;

        /// <summary>The place a point is inside the walls AND under the roof of, if any.</summary>
        private static PlaceId Inside(WorldModel world, Vector3 at)
        {
            var id = At(world, at);
            return id.IsValid && at.y <= TopOf(id) ? id : PlaceId.None;
        }

        /// <summary>The place owning the tile under a world point. Village y runs into -z.</summary>
        private static PlaceId At(WorldModel world, Vector3 at)
        {
            int vx = Mathf.FloorToInt(at.x);
            int vy = Mathf.FloorToInt(-at.z);
            if (vx < 0 || vy < 0 || vx >= world.Width || vy >= world.Height) return PlaceId.None;
            return world.Grid.PlaceAt(vx, vy);
        }
    }
}
