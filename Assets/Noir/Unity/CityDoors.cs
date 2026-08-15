using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// THE DOORS OF ROSSVILLE, WHICH UNTIL NOW WERE PAINT ON A HOLE.
    ///
    /// `Frontage.Doorway` drew a door as four static boxes - head, case, leaf, step - with no
    /// hinge, no state and, because `Piece` discards its collider on purpose, nothing to walk
    /// into. Six hundred front doors that never moved. This gives the leaf a pivot and swings it
    /// when somebody is at it.
    ///
    /// A HOUSE SWINGS IN AND A SHOP SWINGS OUT, and that is not decoration. American residential
    /// exterior doors open inward - the opposite of the British ones this project's earlier town
    /// was modelled on - while commercial premises open outward because the fire code says an
    /// exit must not be pushed against by a crowd. Rossville in 1991 is a village of frame houses
    /// and a downtown row of storefronts, so both rules are visible on the same street.
    ///
    /// WHAT THIS COSTS, because six hundred of anything in this project has to justify itself.
    /// Nothing runs per door per frame. A door is only considered when the camera is within
    /// <see cref="LiveRange"/>, which in a town this size is a handful at a time; everything
    /// beyond that is a shut door that is not touched. The people are bucketed into a coarse
    /// spatial hash once every <see cref="RehashFrames"/> frames rather than searched, so a live
    /// door asks its own cell and nothing else. With nobody about, the whole update is one
    /// distance test per door against the camera.
    ///
    /// WHAT IT DOES NOT DO YET, stated so nobody reads more into it than is there:
    ///
    ///  - There is no Core-side door state. Nothing can ask "was that door open when he walked
    ///    past", which for a game about noticing what changed is the version that actually
    ///    matters. That wants a `Door` in `Noir.Core.World` with open/closed and locked, driven
    ///    by the sim rather than by proximity - see docs/IDEAS.md.
    ///  - The leaf still has no collider, so an open door does not block and a shut one does not
    ///    stop you. Adding one is a separate change and it must be measured: `CityCollision`'s
    ///    header records that boxes on the frontage filled every doorway.
    /// </summary>
    public sealed class CityDoors : MonoBehaviour
    {
        /// <summary>How near the camera a door has to be before it is even considered.</summary>
        private const float LiveRange = 55f;

        /// <summary>How near a person has to be for the door to open. Public so
        /// PlayerInteraction can offer its menu at exactly this distance, rather than choosing a
        /// second, independently-picked number that can drift out of sync with it - and public
        /// rather than internal because the PlayMode tests that reference it live in the separate
        /// Noir.PlayTests assembly, which this project grants no InternalsVisibleTo access to.</summary>
        public const float Reach = 1.9f;

        /// <summary>How near a person has to stay for it to remain open - hysteresis, so a door
        /// somebody is standing beside does not chatter open and shut every frame.</summary>
        private const float Hold = 2.9f;

        /// <summary>Degrees a second. A front door takes about two thirds of a second.</summary>
        private const float Swing = 150f;

        private const int RehashFrames = 12;
        private const float CellSize = 8f;

        /// <summary>How long a manual Force beats automatic proximity, in seconds, in either
        /// direction. Long enough to step away from the door; short enough that walking back up
        /// to a shut door behaves normally again rather than staying artificially locked.
        ///
        /// A CLOSED OVERRIDE BLOCKS THE PROXIMITY SWING FOR EVERYONE, NOT JUST THE PLAYER, for
        /// its whole duration - Update's SomebodyWithin check is only reached once the override
        /// has expired, and it does not distinguish who is standing there. Deliberate: a door
        /// somebody has just shut should stay shut for a while even if an NPC happens to walk up
        /// to it next, not swing back open for the first passer-by. Not a bug.</summary>
        private const float OverrideHold = 5f;

        private readonly List<Transform> _hinges = new List<Transform>();
        private readonly List<float> _shut = new List<float>();
        private readonly List<float> _open = new List<float>();
        private readonly List<float> _angle = new List<float>();
        private readonly List<Vector3> _at = new List<Vector3>();
        private readonly List<float> _overrideUntil = new List<float>();
        private readonly List<float> _forceOpenUntil = new List<float>();

        private readonly Dictionary<long, List<Vector3>> _people = new Dictionary<long, List<Vector3>>();
        private int _sinceRehash = 999;

        private Transform _crowd;
        private Camera _eye;

        public int Count => _hinges.Count;
        public int Moving { get; private set; }

        public static CityDoors Create(Transform parent)
        {
            var go = new GameObject("CityDoors");
            go.transform.SetParent(parent, false);
            return go.AddComponent<CityDoors>();
        }

        /// <summary>
        /// Take a hinge. <paramref name="shutYaw"/> is the leaf's closed heading and
        /// <paramref name="openYaw"/> the heading it swings to - the caller decides which way,
        /// because only the frontage knows whether this is a house or a shopfront.
        /// </summary>
        public void Add(Transform hinge, float shutYaw, float openYaw)
        {
            if (hinge == null) return;
            _hinges.Add(hinge);
            _shut.Add(shutYaw);
            _open.Add(openYaw);
            _angle.Add(shutYaw);
            _at.Add(hinge.position);
            _overrideUntil.Add(0f);
            _forceOpenUntil.Add(0f);
        }

        /// <summary>The world position Update measures this door's own distance checks from.</summary>
        public Vector3 PositionOf(int index) => _at[index];

        /// <summary>Whether hinge <paramref name="index"/> is currently ajar rather than shut.</summary>
        public bool IsOpen(int index) => _angle[index] != _shut[index];

        /// <summary>
        /// The hinge index of the nearest door to <paramref name="from"/> within
        /// <paramref name="within"/> metres, or -1 if none.
        ///
        /// AN UNBOUNDED LINEAR SCAN OF EVERY DOOR IN TOWN, CALLED EVERY FRAME WHILE THE PLAYER IS
        /// WALKING - PlayerInteraction.Update asks this once a frame, not as a one-off query on
        /// approach. It ignores the LiveRange gate Update (this file's own) uses to limit its own
        /// per-frame cost, and does the whole ~589-door town instead. Cheap enough to be invisible
        /// at that count; the thing to bucket or spatially index first if the door count grows
        /// substantially.
        /// </summary>
        public int NearestDoor(Vector3 from, float within)
        {
            int best = -1;
            float bestD2 = within * within;
            for (int i = 0; i < _hinges.Count; i++)
            {
                if (_hinges[i] == null) continue;
                var d = _at[i] - from;
                float d2 = d.x * d.x + d.z * d.z;
                if (d2 > bestD2) continue;
                bestD2 = d2;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// The player's own choice, which beats proximity for a while rather than forever, in
        /// either direction.
        ///
        /// Opening clears any active close-override and sets its own open-override, so the door
        /// starts swinging open immediately rather than waiting on proximity to notice and the
        /// next Rehash to agree - both of which could otherwise sit the swing out for up to
        /// RehashFrames frames, or forever if proximity never happens to agree. Closing is the
        /// mirror: it clears any active open-override and sets the close-override, so a door shut
        /// here does not swing straight back open next frame - the player is by definition
        /// standing within Hold range to have reached this door's menu at all.
        /// </summary>
        public void Force(int index, bool open)
        {
            if (open)
            {
                _overrideUntil[index] = 0f;
                _forceOpenUntil[index] = Time.time + OverrideHold;
            }
            else
            {
                _overrideUntil[index] = Time.time + OverrideHold;
                _forceOpenUntil[index] = 0f;
            }
        }

        private static long CellOf(Vector3 p)
        {
            long cx = (long)Mathf.Floor(p.x / CellSize);
            long cz = (long)Mathf.Floor(p.z / CellSize);
            return (cx << 32) ^ (cz & 0xffffffffL);
        }

        private void Rehash()
        {
            foreach (var pair in _people) pair.Value.Clear();

            if (_crowd == null)
            {
                var view = Object.FindFirstObjectByType<AgentMeshView>();
                if (view != null) _crowd = view.transform;
            }

            if (_crowd != null)
            {
                for (int i = 0; i < _crowd.childCount; i++)
                {
                    var t = _crowd.GetChild(i);
                    if (!t.gameObject.activeSelf) continue;
                    Bucket(t.position);
                }
            }

            var host = Object.FindFirstObjectByType<VillageHost>();
            var player = host != null ? host.Player : null;
            if (player != null)
            {
                var where = player.Where;
                if (where.HasValue) Bucket(where.Value);
            }
        }

        private void Bucket(Vector3 p)
        {
            long key = CellOf(p);
            if (!_people.TryGetValue(key, out var list))
            {
                list = new List<Vector3>();
                _people[key] = list;
            }
            list.Add(p);
        }

        private bool SomebodyWithin(Vector3 at, float range)
        {
            float r2 = range * range;
            long cx = (long)Mathf.Floor(at.x / CellSize);
            long cz = (long)Mathf.Floor(at.z / CellSize);

            for (long dz = -1; dz <= 1; dz++)
            for (long dx = -1; dx <= 1; dx++)
            {
                long key = ((cx + dx) << 32) ^ ((cz + dz) & 0xffffffffL);
                if (!_people.TryGetValue(key, out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i] - at;
                    if (d.x * d.x + d.z * d.z <= r2) return true;
                }
            }
            return false;
        }

        private void Update()
        {
            if (_hinges.Count == 0) return;

            if (_eye == null) _eye = Camera.main;
            Vector3 eye = _eye != null ? _eye.transform.position : Vector3.zero;

            if (++_sinceRehash >= RehashFrames) { Rehash(); _sinceRehash = 0; }

            float live2 = LiveRange * LiveRange;
            float step = Swing * Time.deltaTime;
            int moving = 0;

            for (int i = 0; i < _hinges.Count; i++)
            {
                var hinge = _hinges[i];
                if (hinge == null) continue;

                var d = _at[i] - eye;
                bool near = d.x * d.x + d.z * d.z <= live2;

                float want;
                if (!near)
                {
                    // Out of sight, shut, and not eased there - a door nobody can see does not
                    // need to be watched closing.
                    if (_angle[i] != _shut[i]) { _angle[i] = _shut[i]; hinge.localEulerAngles = new Vector3(0f, _shut[i], 0f); }
                    continue;
                }

                bool open = _angle[i] != _shut[i];                     // already ajar?
                bool closedOverride = Time.time < _overrideUntil[i];
                bool openOverride = Time.time < _forceOpenUntil[i];
                want = closedOverride ? _shut[i]
                     : openOverride ? _open[i]
                     : SomebodyWithin(_at[i], open ? Hold : Reach) ? _open[i] : _shut[i];

                if (Mathf.Approximately(_angle[i], want)) continue;

                _angle[i] = Mathf.MoveTowardsAngle(_angle[i], want, step);
                hinge.localEulerAngles = new Vector3(0f, _angle[i], 0f);
                moving++;
            }

            Moving = moving;
        }
    }
}
