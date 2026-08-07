using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Noir.Core.World;
// UnityEngine has its own Terrain (the 3D heightmap kind), so name ours explicitly.
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// Everything Ashcombe sounds like: the bell on the hour, a bed of outdoor ambience that
    /// follows the clock, and footsteps that know what you are walking on.
    ///
    /// Atmosphere is half sound, and until this existed the village was a silent film. The
    /// bell in particular is doing two jobs — it is the single most characteristic noise a
    /// village makes, and it is a clock you can hear without looking at the HUD, which is
    /// exactly what you want when you are stood in a street watching somebody's front door.
    ///
    /// Every clip is a loose WAV read straight off disk from Content/audio/, decoded here,
    /// exactly as SurfaceTextures reads loose PNGs: no import settings, no .meta files, no
    /// Inspector work. If the folder is not there the village is silent and says so once.
    /// </summary>
    public sealed class VillageAudio : MonoBehaviour
    {
        private VillageHost _host;
        private OrbitCamera _rig;

        // ---- the bell ----
        //
        // SPEED. The simulation runs from pause to 300x, and an hourly bell has to survive
        // that. At 300x an hour goes by every twelve seconds and at 60x every minute, while a
        // twelve-strike peal takes half a minute of REAL time — so above 10x the bell would
        // either be continuous or would still be tolling midnight when one o'clock arrived.
        // Ringing only at or below 10x costs nothing, because 10x is already six real minutes
        // to the hour, and above it you are fast-forwarding rather than watching. The peal
        // itself is timed in real seconds for the same reason: a bell that struck on sim time
        // would be a machine gun at 10x.
        private const float MaxRingSpeed = 10f;
        private const float StrikeSeconds = 2.2f;
        private const float BellVolume = 0.75f;

        /// <summary>
        /// Height of the belfry above the churchyard. The church mesh has no tower on it yet,
        /// but sound does not need one to be built — putting the source up here rather than at
        /// ground level is what stops the cottages along Church Lane from occluding it in the
        /// listener's head as you walk past them.
        /// </summary>
        private const float BelfryHeight = 11f;

        private AudioSource _bell;
        private AudioClip _bellClip;
        private int _lastHour = -1;
        private int _strikesLeft;
        private float _nextStrike;

        // ---- ambience ----
        //
        // Three beds playing at once with the level driven by the hour, rather than one bed
        // being swapped for another: at half past four you want the night still underneath the
        // chorus, not cut away from it.
        //
        // The crossfade runs on REAL time (unscaledDeltaTime) and not on sim time. Driving it
        // from the clock means that at 300x the whole day's lighting-up and dying-down happens
        // in five minutes of wall clock, and the beds strobe. Slewing towards the target level
        // over a couple of real seconds means fast-forward sounds like a long dissolve, which
        // is the only thing it could honestly sound like.
        private const float FadeSeconds = 2.5f;
        private const float AmbienceVolume = 0.75f;

        /// <summary>
        /// How far the outdoor beds drop when you are stood inside a building. Not to zero:
        /// these are single-glazed 1970s cottages and you can hear the rooks through them.
        /// </summary>
        private const float IndoorDuck = 0.3f;

        /// <summary>
        /// ALL THREE AMBIENCE BEDS ARE OFF. Not the dawn one - all of them.
        ///
        /// This started as BirdsEnabled, disabling bed 1 (ambience_dawn) alone, on the reasoning
        /// that the dawn chorus is where birds live. That reasoning came from the comment a few
        /// lines down rather than from the audio, and it was wrong: the three files are three
        /// different recordings of the same length, and the one actually audible when the game
        /// opens is ambience_NIGHT - the clock starts at 06:00, night fades out until 06:30, so
        /// at the moment you press Play it is up at about a fifth volume. It has birds in it too.
        /// The dawn bed was silenced correctly and the player kept hearing birds anyway, three
        /// times, while being told it was fixed.
        ///
        /// So this is no longer a guess about which file contains what. Nothing loops until
        /// somebody has actually listened to the three files and decided what each is for. The
        /// bell and the footsteps are untouched - they are event sounds, they only fire when
        /// something happens, and neither has ever been the complaint.
        /// </summary>
        private const bool AmbienceEnabled = false;

        private static readonly string[] BedNames = { "ambience_night", "ambience_dawn", "ambience_day" };
        private readonly AudioSource[] _beds = new AudioSource[BedNames.Length];
        private readonly float[] _level = new float[BedNames.Length];

        // ---- footsteps ----

        private AudioSource _steps;
        private Vector3 _lastCameraPosition;
        private float _strideAccumulator;
        private bool _alternate;

        private const float MinStride = 1.25f;   // metres between footfalls at a walk
        private const float MaxStride = 2.0f;    // ...and at a jog, because a stride lengthens
        private const float StepVolume = 0.55f;

        public static VillageAudio Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var audio = go.AddComponent<VillageAudio>();
            audio.Build(host);
            return audio;
        }

        /// <summary>
        /// SILENT IN BATCH MODE, ALWAYS. A headless run - MapAudit, the PlayMode suite, a
        /// CityShot render - still starts a real player with a real audio device, and it played
        /// the village's ambience out of the machine's speakers while somebody was working. It
        /// is somebody else's headphones and there is never a reason for an automated run to
        /// make a sound, so nothing here is built at all rather than being built and muted.
        /// </summary>
        private void Build(VillageHost host)
        {
            _host = host;

            if (Application.isBatchMode) return;

            EnsureListener();
            BuildBell(host.World);
            BuildBeds();
            BuildSteps();

            if (host.Sim != null) _lastHour = host.Sim.Clock.HourOfDay;
            WavClips.ReportOnce();
        }

        /// <summary>
        /// A scene bootstrapped from empty has a camera but no ears. Without this every source
        /// in the village plays into nothing, which is indistinguishable from the files being
        /// missing and takes far longer to work out.
        ///
        /// EXCEPT IN BATCH MODE, WHERE THERE IS NOBODY TO LISTEN AND SOMEBODY TO DISTURB. The
        /// PlayMode suite bootstraps this same host, so `-runTests` played the village's ambience,
        /// its bell and its traffic straight into the owner's headphones while he was doing
        /// something else. That was written down as a rule - "headless runs must be silent" - and
        /// a rule in a document does not mute anything. Giving the scene no ears is the whole fix:
        /// every source still plays, still moves, still gets tested, and is heard by nothing.
        /// </summary>
        private static void EnsureListener()
        {
            if (Application.isBatchMode)
            {
                // Belt and braces: not adding ears is not enough on its own, because anything
                // else in the scene - a prefab, a test's own camera - may bring a listener with
                // it. Zeroing the global volume is the one switch nothing downstream can undo by
                // accident.
                AudioListener.volume = 0f;
                return;
            }

            if (FindFirstObjectByType<AudioListener>() != null) return;
            var cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
        }

        private void BuildBell(WorldModel world)
        {
            _bellClip = WavClips.Load("bell");

            var position = new Vector3(0f, BelfryHeight, 0f);
            foreach (var id in world.PlacesOfKind(PlaceKind.Church))
            {
                var church = world.GetPlace(id);
                if (church == null) continue;
                position = Space3D.ToWorld(church.Bounds.Centre) + Vector3.up * BelfryHeight;
                break;
            }

            var go = new GameObject("St Anne's bell");
            go.transform.SetParent(transform, false);
            go.transform.position = position;

            _bell = go.AddComponent<AudioSource>();
            _bell.clip = null;
            _bell.playOnAwake = false;
            _bell.loop = false;
            _bell.dopplerLevel = 0f;      // walking about should not bend the pitch of a bell

            // Rung from where it actually hangs, so it is loud in the churchyard and thin from
            // the far end of Mill Lane. That is most of what makes it read as a real bell in a
            // real place rather than a sound effect fired at your head.
            _bell.spatialBlend = 1f;
            _bell.rolloffMode = AudioRolloffMode.Logarithmic;
            _bell.minDistance = 18f;
            _bell.maxDistance = 600f;
        }

        private void BuildBeds()
        {
            for (int i = 0; i < BedNames.Length; i++)
            {
                // EVERY bed is off, not just the dawn one - see AmbienceEnabled above for why
                // silencing bed 1 alone did not stop the birds. Not loaded at all, so there is
                // no source anywhere for any of them to leak from.
                if (!AmbienceEnabled) continue;

                var go = new GameObject(BedNames[i]);
                go.transform.SetParent(transform, false);

                var source = go.AddComponent<AudioSource>();
                source.clip = WavClips.Load(BedNames[i]);
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;   // weather is not in one place
                source.volume = 0f;
                if (source.clip != null) source.Play();
                _beds[i] = source;
            }
        }

        private static readonly string[] Surfaces = { "grass", "road", "path", "floor", "churchyard" };

        private void BuildSteps()
        {
            var go = new GameObject("Footsteps");
            go.transform.SetParent(transform, false);
            _steps = go.AddComponent<AudioSource>();
            _steps.playOnAwake = false;
            _steps.loop = false;
            _steps.spatialBlend = 0f;       // they are your own feet, and they are not over there

            // All ten up front rather than on demand. They are twenty kilobytes each, and the
            // alternative is reading a file off the disk on the frame you first step onto grass,
            // which is precisely the frame you would notice a hitch.
            foreach (var surface in Surfaces)
            {
                WavClips.Load("step_" + surface + "_a");
                WavClips.Load("step_" + surface + "_b");
            }
        }

        private void Update()
        {
            if (_host == null || _host.Sim == null) return;
            if (Application.isBatchMode) return;      // Build made no sources - see there
            if (_rig == null) _rig = FindFirstObjectByType<OrbitCamera>();

            UpdateAmbience();
            UpdateBell();
            UpdateFootsteps();
        }

        // ------------------------------- ambience -------------------------------

        private void UpdateAmbience()
        {
            float hour = _host.Sim.Clock.MinuteOfDay / 60f;

            // Night comes up from half past six, well before the evening chorus has finished,
            // so that dusk is two things overlapping rather than a hole between them. Its fall
            // is written as 28:00 to 30:30 — see Window on why the numbers run past midnight.
            // GATED, WHERE THEY NEVER WERE. Only the dawn bed was ever conditional, so night
            // and day went on playing at whatever the clock asked for - and night is the one
            // audible at 06:00 when the game opens.
            float night = AmbienceEnabled ? Window(hour, 18.5f, 21.0f, 28.0f, 30.5f) : 0f;
            float day = AmbienceEnabled ? Window(hour, 6.5f, 8.5f, 18.0f, 20.5f) : 0f;

            // The chorus is mostly over by seven — birds sing at first light to hold a
            // territory and spend the rest of the morning eating. The evening chorus is the
            // same birds again, quieter and shorter, which is why it reuses the dawn bed
            // rather than earning a file of its own.
            float dawn = AmbienceEnabled
                ? Mathf.Max(Window(hour, 3.8f, 5.2f, 7.0f, 9.0f),
                           0.55f * Window(hour, 18.0f, 19.2f, 20.2f, 21.6f))
                : 0f;

            float indoors = Indoors() ? IndoorDuck : 1f;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FadeSeconds);

            SetBed(0, night * indoors, k);
            SetBed(1, dawn * indoors, k);
            SetBed(2, day * indoors, k);
        }

        private void SetBed(int index, float target, float k)
        {
            _level[index] = Mathf.Lerp(_level[index], target, k);
            if (_beds[index] != null) _beds[index].volume = _level[index] * AmbienceVolume;
        }

        private bool Indoors()
        {
            var cam = Camera.main;
            if (cam == null || _rig == null || _rig.Mode != OrbitCamera.ViewMode.Street) return false;
            return _host.World.Grid.TerrainAt(Space3D.TileAt(cam.transform.position)) == Terrain.Floor;
        }

        /// <summary>
        /// A window over the day with smooth edges, in hours.
        ///
        /// Fall times may run past 24 — night is written as 18:30 to 30:30 — because a window
        /// that crosses midnight has no sane expression otherwise. Testing the hour a day
        /// either side is what makes that work, and costs two extra evaluations of a cubic.
        /// </summary>
        private static float Window(float hour, float rise0, float rise1, float fall0, float fall1)
        {
            float best = 0f;
            for (int k = -1; k <= 1; k++)
            {
                float h = hour + k * 24f;
                float v = Mathf.Min(Step(h, rise0, rise1), 1f - Step(h, fall0, fall1));
                if (v > best) best = v;
            }
            return Mathf.Clamp01(best);
        }

        private static float Step(float x, float a, float b)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // --------------------------------- bell ---------------------------------

        private void UpdateBell()
        {
            if (_bell == null || _bellClip == null) return;

            // A skip is a jump, not time passing, so nothing rings on the way through. The hour
            // is deliberately NOT resynced while skipping: when the skip lands on the hour the
            // check below sees the change and rings it, which is a rather good confirmation
            // that you have arrived where you asked to be.
            if (_host.Skipping) { _strikesLeft = 0; return; }

            if (_strikesLeft > 0)
            {
                _nextStrike -= Time.unscaledDeltaTime;
                if (_nextStrike <= 0f)
                {
                    _bell.PlayOneShot(_bellClip, BellVolume);
                    _strikesLeft--;
                    _nextStrike = StrikeSeconds;
                }
            }

            int hour = _host.Sim.Clock.HourOfDay;
            if (hour == _lastHour) return;

            // Recorded even when it is too fast to ring, so that dropping back down to 10x
            // does not immediately fire off a peal for an hour that passed some time ago.
            _lastHour = hour;

            float speed = VillageHost.Speeds[Mathf.Clamp(_host.SpeedIndex, 0, VillageHost.Speeds.Length - 1)];
            if (speed > MaxRingSpeed) return;

            // Church clocks strike the twelve-hour hour, and midnight and noon are twelve.
            _strikesLeft = hour % 12;
            if (_strikesLeft == 0) _strikesLeft = 12;
            _nextStrike = 0f;
        }

        // ------------------------------- footsteps -------------------------------

        private void UpdateFootsteps()
        {
            var cam = Camera.main;
            if (_steps == null || cam == null || _rig == null) return;

            var position = cam.transform.position;
            var moved = position - _lastCameraPosition;
            moved.y = 0f;
            _lastCameraPosition = position;

            // Only at eye level. Footsteps under an overview camera would be the sound of a
            // map being dragged about, which is nobody's idea of atmosphere.
            if (_rig.Mode != OrbitCamera.ViewMode.Street) { _strideAccumulator = MinStride; return; }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            float distance = moved.magnitude;

            // Switching view mode or being dropped somewhere else moves the camera hundreds of
            // metres in one frame. Without this it arrives as a burst of footsteps.
            if (distance > 2f) return;

            float speed = distance / dt;
            if (speed < 0.4f)
            {
                // Left one stride short rather than at zero, so the first pace after standing
                // still lands as you start moving instead of a metre and a half later.
                _strideAccumulator = MinStride;
                return;
            }

            _strideAccumulator += distance;
            float stride = Mathf.Lerp(MinStride, MaxStride, Mathf.InverseLerp(4f, 9f, speed));
            if (_strideAccumulator < stride) return;
            _strideAccumulator -= stride;

            var clip = WavClips.Load(StepName(position));
            if (clip == null) return;

            _alternate = !_alternate;
            _steps.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
            _steps.PlayOneShot(clip, StepVolume * Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(4f, 9f, speed)));
        }

        private string StepName(Vector3 position)
        {
            string surface;
            switch (_host.World.Grid.TerrainAt(Space3D.TileAt(position)))
            {
                case Terrain.Road: surface = "road"; break;
                case Terrain.Path: surface = "path"; break;
                case Terrain.Floor: surface = "floor"; break;
                case Terrain.Churchyard: surface = "churchyard"; break;
                // Grass covers field, wood and anything off the edge of the map. Ploughed
                // earth and undergrowth are both closer to grass than to tarmac, and a fifth
                // and sixth surface is more files than the difference is worth.
                default: surface = "grass"; break;
            }
            return "step_" + surface + (_alternate ? "_a" : "_b");
        }
    }

    /// <summary>
    /// Loads 16-bit PCM WAV files from Content/audio/ as AudioClips.
    ///
    /// The decoder is written out by hand for the same reason the writer in Noir.Sim is: it is
    /// forty lines, and the alternative is UnityWebRequestMultimedia, which is asynchronous,
    /// needs a coroutine, and would drag a file:// URL through the networking stack to read a
    /// file that is sitting on the disk next to village.txt. Drop a WAV in, press Play, hear
    /// it. Delete it and that sound goes away and nothing else does.
    /// </summary>
    public static class WavClips
    {
        private static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        private static bool _reported;
        private static int _loaded;

        public static string Directory => Path.Combine(ContentLoader.Root, "audio");

        public static AudioClip Load(string name)
        {
            if (_cache.TryGetValue(name, out var cached)) return cached;

            AudioClip clip = null;
            string path = Path.Combine(Directory, name + ".wav");

            if (File.Exists(path))
            {
                try { clip = Decode(name, File.ReadAllBytes(path)); }
                catch (Exception ex) { Debug.LogWarning($"Could not read {path}: {ex.Message}"); }
            }

            if (clip != null) _loaded++;
            _cache[name] = clip;
            return clip;
        }

        /// <summary>
        /// Walks the RIFF chunks rather than assuming a 44-byte header, because a writer is
        /// entitled to put a LIST or a fact chunk in front of the data and a decoder that
        /// assumes otherwise fails on a file that is perfectly valid.
        /// </summary>
        private static AudioClip Decode(string name, byte[] b)
        {
            if (b.Length < 44 || Tag(b, 0) != "RIFF" || Tag(b, 8) != "WAVE")
                throw new Exception("not a RIFF/WAVE file");

            int channels = 0, rate = 0, bits = 0, format = 0;
            int dataAt = -1, dataLength = 0;

            int p = 12;
            while (p + 8 <= b.Length)
            {
                string id = Tag(b, p);
                int length = Int32(b, p + 4);
                int body = p + 8;
                if (length < 0 || body + length > b.Length) length = b.Length - body;

                if (id == "fmt " && length >= 16)
                {
                    format = Int16(b, body);
                    channels = Int16(b, body + 2);
                    rate = Int32(b, body + 4);
                    bits = Int16(b, body + 14);
                }
                else if (id == "data")
                {
                    dataAt = body;
                    dataLength = length;
                }

                p = body + length + (length & 1);   // chunks are word-aligned
            }

            if (format != 1 || bits != 16) throw new Exception($"expected 16-bit PCM, got format {format} / {bits} bit");
            if (channels < 1 || rate < 1 || dataAt < 0) throw new Exception("missing fmt or data chunk");

            int count = dataLength / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s = (short)(b[dataAt + i * 2] | (b[dataAt + i * 2 + 1] << 8));
                samples[i] = s / 32767f;
            }

            var clip = AudioClip.Create(name, count / channels, channels, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static string Tag(byte[] b, int at) =>
            new string(new[] { (char)b[at], (char)b[at + 1], (char)b[at + 2], (char)b[at + 3] });

        private static int Int16(byte[] b, int at) => b[at] | (b[at + 1] << 8);

        private static int Int32(byte[] b, int at) =>
            b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

        public static void ReportOnce()
        {
            if (_reported) return;
            _reported = true;

            if (!System.IO.Directory.Exists(Directory))
            {
                Debug.Log($"No audio at {Directory} - the village is silent. "
                        + "Run `dotnet run --project tools/Noir.Sim -- audio` to generate it.");
                return;
            }

            Debug.Log($"Audio: {_loaded} clips loaded from Content/audio/.");
        }
    }
}
