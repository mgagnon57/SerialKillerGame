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
            return ground.Raycast(ray, out float enter) ? At(world, ray.GetPoint(enter)) : PlaceId.None;
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
