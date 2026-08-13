using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// Photographs the layer switches actually working, one combination at a time.
    ///
    /// The panel is the one part of this project that cannot be checked by assertion: every
    /// question about it - is the house outline visible over the massing, does the massing come up
    /// at all, does turning a layer on do anything without a restart - is a question about what is
    /// ON SCREEN. So this drives the switches and takes a picture of each answer, and the pictures
    /// are the evidence.
    /// </summary>
    public class LayerProof
    {
        private const int Width = 1400, Height = 900;
        private static readonly string Dir =
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "snapshots", "layers");

        /// <summary>Over the Benton Avenue blocks: houses, the grade school lots, a crossroads.</summary>
        private static readonly Vector3 Over = new Vector3(800f, 0f, -1080f);

        /// <summary>Chicago x Attica - the commercial crossroads, and where the sidewalk rulings
        /// are. A shot of the town's own centre is worth having anyway.</summary>
        private static readonly Vector3 Downtown = new Vector3(660f, 0f, -1230f);

        private readonly struct Shot
        {
            public readonly string Name;
            public readonly Layers.Kind[] On;
            public readonly Vector3 Where;

            public Shot(string name, params Layers.Kind[] on)
            {
                Name = name; On = on; Where = Over;
            }

            public Shot(string name, Vector3 where, params Layers.Kind[] on)
            {
                Name = name; On = on; Where = where;
            }
        }

        private static readonly Shot[] Sheet =
        {
            new Shot("01-parcel-lines",            Layers.Kind.Plan),
            new Shot("02-parcel-lines-and-houses", Layers.Kind.Plan, Layers.Kind.Footprints),
            new Shot("03-house-outlines-alone",    Layers.Kind.Footprints),
            new Shot("04-massing-alone",           Layers.Kind.Massing),
            new Shot("05-massing-and-outlines",    Layers.Kind.Massing, Layers.Kind.Footprints),
            new Shot("06-massing-plan-outlines",   Layers.Kind.Massing, Layers.Kind.Plan,
                                                   Layers.Kind.Footprints),
            new Shot("07-streets-and-massing",     Layers.Kind.Massing, Layers.Kind.Streets,
                                                   Layers.Kind.Alleys),

            // The view the road widths are judged in: the corridor and its right of way against
            // the lot lines they have to sit between, with nothing standing on top of either.
            new Shot("08-road-widths",             Layers.Kind.Streets, Layers.Kind.Alleys,
                                                   Layers.Kind.Plan),
            new Shot("09-road-widths-and-houses",  Layers.Kind.Streets, Layers.Kind.Alleys,
                                                   Layers.Kind.Plan, Layers.Kind.Footprints),

            // THE OWNER'S OWN SIDEWALK RULINGS, ON THE GROUND THEY WERE MADE FOR. The browser map
            // is the source; this is the picture that says the game heard it.
            new Shot("10-downtown-sidewalks", Downtown,
                     Layers.Kind.Streets, Layers.Kind.Alleys, Layers.Kind.Plan),
        };

        // THE SWITCH THAT DECIDES WHICH TOWN A HEADLESS RUN BUILDS HAS MOVED, and it was never
        // this file's to own. It read NOIR_BUILT_TOWN and raised the built town, from inside a
        // `Category("Diagnostic")` test file that the standing gate excludes - so the decision
        // governing every run in this assembly lived in the one file most likely to be deleted or
        // skipped. See CityUnderTest, which already owns the other half of the same decision.
        //
        /// <summary>What the layer switches were before this walked all over them.</summary>
        private System.Collections.Generic.Dictionary<Layers.Kind, bool> _wasLayers;

        /// <summary>
        /// PUT THE SWITCHES BACK, whatever happened. A `[TearDown]` and not a line at the end of
        /// the test, because the interesting failures here are the ones that throw halfway - and
        /// those are exactly the runs that would otherwise leave the panel in whatever state the
        /// twelfth frame wanted.
        /// </summary>
        [TearDown]
        public void PutTheLayersBack()
        {
            if (_wasLayers != null) Layers.Restore(_wasLayers);
            _wasLayers = null;
        }

        [UnityTest, Category("Diagnostic"), Timeout(1800000)]
        public IEnumerator PhotographEveryLayerCombination()
        {
            // SILENT. A batch run is not a person sitting at the machine, and this suite has put
            // the whole town's audio through somebody's headphones before now.
            AudioListener.volume = 0f;
            AudioListener.pause = true;

            yield return CityUnderTest.WaitUntilBuilt();
            Directory.CreateDirectory(Dir);

            // NO SkipToHour. The town opens at six, in the dark, and skipping to noon means
            // simulating six hours - which on a town that has just gained 315 buildings does not
            // finish inside this test's patience. That is worth knowing on its own, and it is not
            // what these pictures are about: the subject is which geometry is on screen, so the
            // rig is stood down and a plain light put in instead. Deterministic, and instant.
            for (int settle = 0; settle < 10; settle++) yield return null;

            // ---- what has anything behind it at all ----
            // SNAPSHOT FIRST, RESTORE IN THE TEARDOWN. This walks every layer combination in the
            // sheet and puts nothing back, so whatever combination the last frame happened to end
            // on used to stick - and `Layers.Set` writes PlayerPrefs whenever there is a person at
            // the keyboard. That is verbatim the mechanism behind the 2026-08-07 incident, where a
            // run ended with People off, `VillageHost` never built `AgentMeshView`, and
            // `WhyAreThePeopleNotAnimating` was written down in this project's own notes as a
            // FLAKY TEST for weeks.
            //
            // `Layers.IsOn` gives batch mode the compiled default now, so this cannot bite a
            // headless run - but the whole point of a proof sheet is that somebody runs it from
            // the editor to LOOK at it, and that is the case with a person in it.
            _wasLayers = Layers.Snapshot();

            // THE SHEET CANNOT PROVE A LAYER THE RUN DID NOT BUILD, and for three of its twelve
            // combinations it never could.
            //
            // `VillageHost.ShowBuildings` is false in batch mode unless `NOIR_BUILT_TOWN=1` says
            // otherwise, and CityStreets, CityParking, CitySigns and CityGreenery all sit behind
            // it - so `07-streets-and-massing` came out BYTE-IDENTICAL to `04-massing-alone`,
            // `08-road-widths` to `01-parcel-lines`, and `09-road-widths-and-houses` to
            // `02-parcel-lines-and-houses`. Three pairs, every one of them a street frame.
            //
            // It passed anyway, every time, because the assertion at the bottom counted FILES.
            // `ShotLog.Stamp` hashes them now and that is how this was found - on the first
            // targeted run after the hashing landed.
            //
            // Refusing rather than skipping: a proof sheet that quietly photographs three-quarters
            // of itself is the failure this whole file exists to prevent.
            Assert.That(VillageHost.ShowBuildings, Is.True,
                "LayerProof needs the BUILT town: CityStreets, CityParking, CitySigns and "
              + "CityGreenery are all behind VillageHost.ShowBuildings, which is false in batch "
              + "mode unless NOIR_BUILT_TOWN=1. Without it three of the twelve frames come out "
              + "byte-identical to another - every one of them a street frame - and the sheet "
              + "proves nothing about the layers it was made for. Set NOIR_BUILT_TOWN=1 and run "
              + "it again.");

            var wired = new StringBuilder("[layerproof] wiring:\n");
            foreach (var kind in Layers.All)
                wired.Append("    ").Append(Layers.IsWired(kind) ? "LIVE " : "DEAD ")
                     .Append(Layers.Label(kind)).Append('\n');
            Debug.Log(wired.ToString());

            // The fog is tuned for standing in the street and eats a block seen from 200 m up,
            // and the rig writes it every frame, so it has to be stood down rather than overridden.
            var weather = Object.FindFirstObjectByType<SunRig>();
            if (weather != null) weather.enabled = false;
            bool wasFog = RenderSettings.fog;
            RenderSettings.fog = false;

            var lightGo = new GameObject("LayerProofSun");
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);

            var camGo = new GameObject("LayerProofCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 4000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.08f);

            var rotation = Quaternion.Euler(52f, 20f, 0f);
            camGo.transform.rotation = rotation;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            foreach (var frame in Sheet)
            {
                camGo.transform.position =
                    frame.Where + Vector3.up * 2f - rotation * Vector3.forward * 210f;

                foreach (var kind in Layers.All) Layers.Set(kind, false);
                foreach (var kind in frame.On) Layers.Set(kind, true);

                // A LAZY LAYER IS BUILT BY THAT Set, so the frame after is the first one that can
                // show it. Several, to let the bake and any instantiate settle.
                for (int settle = 0; settle < 4; settle++) yield return null;

                RenderSettings.fog = false;
                Capture(cam, rt, shot, frame.Name);
            }

            RenderSettings.fog = wasFog;

            Assert.That(Directory.GetFiles(Dir, "*.png").Length, Is.GreaterThanOrEqualTo(Sheet.Length),
                        "every combination should have left a picture behind");

            // "A PICTURE" IS NOT "A DIFFERENT PICTURE". This walks every layer combination and
            // then checked only that the files existed, so a combination that changes nothing
            // visible - or a camera looking somewhere none of the layers reach - passes exactly
            // like one that works. Two identical frames in a LAYER proof mean the layer did not
            // draw, which is the entire subject of the test.
            ShotLog.Stamp("layers-proof", Dir, Directory.GetFiles(Dir, "*.png"));
            Debug.Log($"[layerproof] {Sheet.Length} frames in {Dir}");
        }

        private static void Capture(Camera cam, RenderTexture rt, Texture2D shot, string name)
        {
            var wasTarget = cam.targetTexture;
            var wasActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();

            cam.targetTexture = wasTarget;
            RenderTexture.active = wasActive;

            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), shot.EncodeToPNG());
            Debug.Log($"[layerproof] {name}");
        }
    }
}
