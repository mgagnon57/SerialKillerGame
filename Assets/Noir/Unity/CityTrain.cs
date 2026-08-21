using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Runs the elevated train along its line and stops it at the platform.
    ///
    /// The single cheapest piece of life in the whole city: one straight line, no junctions, no
    /// traffic, nothing to collide with - and it is the thing the eye follows, because it is the
    /// biggest moving object above the rooflines.
    ///
    /// Lives on its own node so the chunker never sees it. A combined mesh cannot move.
    /// </summary>
    public sealed class CityTrain : MonoBehaviour
    {
        public Vector3 From, To, Along;

        /// <summary>Where along the run the platform is, 0..1.</summary>
        public float StopAt = 0.33f;

        /// <summary>Metres per SIM second, and how long it sits at the platform, in SIM
        /// seconds - one clock (owner, 2026-08-16), so the train obeys the dial and the pause
        /// like everything else that moves.</summary>
        private const float Speed = 11f, Dwell = 6f;

        /// <summary>
        /// The sim clock's tick. -1 until fed, and the train stands still until it is. NOBODY
        /// FEEDS IT TODAY: CityRail (this El) is built only by the editor render tools, never
        /// by VillageHost - Rossville's line is CityRailBed's freight grade, not an El. If the
        /// El ever joins a running town, its builder must push Sim.Clock.Tick here every frame
        /// the way VillageHost.Update does for CityTraffic, or the train will visibly never
        /// move - which is this comment's job to explain.
        /// </summary>
        public long TownTick = -1;
        private long _lastTick = -1;

        private float _at;
        private float _waited = -1f;
        private Vector3 _base;

        private void Start() => _base = transform.position;

        private void Update()
        {
            float length = Vector3.Distance(From, To);
            if (length < 1f) return;
            if (TownTick < 0) return;
            if (_lastTick < 0) { _lastTick = TownTick; return; }
            float dtTown = (TownTick - _lastTick) / (float)Noir.Core.Contracts.GameClock.TicksPerSecond;
            _lastTick = TownTick;
            if (dtTown <= 0f) return;

            // Sitting at the platform.
            if (_waited >= 0f)
            {
                _waited += dtTown;
                if (_waited < Dwell) return;
                _waited = -1f;
                _at += 0.02f;                     // clear of the stop before testing again
            }

            float was = _at;
            _at += Speed / length * dtTown;
            if (_at > 1f) _at -= 1f;

            // Pull in when it crosses the platform, once per lap.
            if (was < StopAt && _at >= StopAt) { _at = StopAt; _waited = 0f; }

            transform.position = _base + Vector3.Lerp(From, To, _at);
        }
    }
}
