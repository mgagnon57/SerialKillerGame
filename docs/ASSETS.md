# Assets worth buying

A shopping list, researched 2026-07-31. Nothing here has been bought and nothing here is a
commitment. Tick a box when it is in and working; strike the line out when it turns out to be
wrong for us and say why, so nobody researches it twice.

Prices are indicative and were correct when this was written.

**And a third source now: the owner makes models.** House-made props live in
`Assets/Noir/Models/` as OBJ+MTL, converted from Designer's GLB exports by
`python tools/glb-to-obj.py <file.glb> <Name>` — flat-color materials only (the tool refuses
textured GLBs rather than dropping their textures silently), node transforms baked, one mesh
with a submesh per material. First one in: **DesktopPC1991** (2026-08-16, his first model) —
a complete beige 1991 desktop, CRT, keyboard with modeled keys, mouse and cables, 10,072 tris,
6 flat materials, real-world metres, and it sits in register with the pack. Preview:
`docs/snapshots/pc-preview-front.png`. Second: **RotaryDeskPhone** (same day) — handset in
the cradle, rotary dial, a coiled cord spiralling onto the desk; 44,408 tris, most of them in
the coil and dial. That is HERO-PROP weight: fine for a handful of desks, but a phone is an
every-house object in 1991, and 500 households of 44k tris is another whole town of triangles
— the everywhere version wants a lower-segment re-export from Designer. Preview:
`docs/snapshots/phone-preview.png`. Neither is PLACED anywhere yet — a placement seam for
desk props is the follow-on.

---

## The four filters

Every suggestion below has been through these. They are also the reason several obvious-looking
assets are in the DO NOT BUY section rather than this one.

**1. URP, and Unity 6.** The project is on Unity 6000.3 with the Universal Render Pipeline.
An HDRP-only or Built-in-only asset is not a candidate whatever it does.

**2. It has to survive `CityChunker`.** The bake combines about 51,000 renderers into 5,200 across
30 materials, and it does that by destroying the originals. Anything that needs a live
MonoBehaviour on a piece of scenery does not survive it. Camera effects, audio and character
controllers are fine; per-prop scripts are not.

**3. It must not replace something we have already built and tested.** `CityTraffic`, `LaneGraph`,
`SunRig` and `CityDistrict` are ours, are covered by the PlayMode suite, and in the traffic case
took a long day to get right. Buying over the top of them trades tested code for untested code.

**4. STYLISED, NOT PHOTOREAL.** This is the one that catches people out. The whole city is UV-mapped
onto a single flat-colour swatch atlas - `Universal_A_Alb`, 4096 square and 428KB because it is
blocks of colour rather than a texture. A photoreal brick material dropped next to that does not
read as better, it reads as a mistake. What makes this world feel real is LIGHT, WEATHER, MOTION
AND SOUND, not surface detail.

---

## Free, and do these first

- [ ] **Unity Starter Assets - ThirdPerson** — free, official, URP, Unity 6.
  [Asset Store](https://assetstore.unity.com/packages/essentials/starter-assets-thirdperson-updates-in-new-charactercontroller-package-196526)

  THE CONTROLLABLE PERSON, and the reason to start here rather than with a paid controller is
  measured: **78 of our 79 characters are already imported as `animationType: 3`, a Humanoid rig.**
  Starter Assets drives any Humanoid, so it should be walking around Rossville in an afternoon at
  no cost. It brings a Cinemachine third-person camera with it.

  What it touches: a new controllable agent alongside `OrbitCamera`, which already does Tab to
  street level and WASD. What is actually missing today is a visible BODY, an animated walk and a
  camera that follows it - which is exactly what this is.

- [ ] **Mixamo animations** — free (Adobe account), [mixamo.com](https://www.mixamo.com).

  The pack ships **zero animations and zero animator controllers**. It does not need to: a Humanoid
  rig retargets, so any Mixamo clip drops onto our people. This is the whole animation problem
  solved for nothing, and it is why the paid animated-people packs are in DO NOT BUY.

  **There is no bulk download.** Adobe removed pack downloads years ago; you pick clips one at a
  time. That is fine, because the list below is about a dozen rather than hundreds.

  ### Three settings on every download

  **You do not have to upload anything.** Browse with Mixamo's own mannequin (X Bot or Y Bot) and
  download from that: Unity's Humanoid retargeting maps a clip onto our people whoever it was
  authored on, so the skeleton it was recorded against does not matter. Uploading one of ours only
  makes the PREVIEW look like our game, and our characters arrive already rigged, which Mixamo's
  auto-rigger will try to redo.

  If you do want the preview to match, the file to upload is one of the 79 under
  `Assets/polyperfect/Poly Universal Pack/Meshes/People/` - note the `SKM_` prefix, for example
  `Slavic People/SKM_Man_Slavic_Summer_Hair.fbx`.

  Per clip:

  | setting | value | why |
  |---|---|---|
  | Format | `FBX for Unity (.fbx)` | not the .dae, not the .glb |
  | Skin | **Without Skin** | we have our own characters; the skin is weight for nothing |
  | **In Place** | **ON** for anything locomotive | see below - this one is not a preference |

  **WHY IN PLACE MATTERS HERE.** `Simulation` decides where everybody is by pathfinding and
  `AgentMeshView` draws them at the position it computed. A clip carrying root motion wants to
  drive the transform ITSELF, which would be the animation and the simulation fighting over the
  same number. Same reasoning as `CityTraffic` moving a car along its lane coordinate rather than
  letting anything else push it about.

  Belt and braces: `AgentBody` sets `applyRootMotion = false`, so a clip that does carry root
  motion has it discarded rather than applied, and a bulk download made without ticking the box
  will not send anybody walking through a wall. `Noir/Check The Animations` still reports them,
  because a discarded root is a clip whose stride was authored for a travel we then throw away -
  the rate match below has nothing true to work from, and In Place is the clean fix.

  ### Matching the clip to the pace, so the feet do not skate

  A walk cycle is a stride of a particular length at a particular rate. Play it while moving the
  person at any OTHER speed and the feet plant and are then dragged along the ground - which is
  the "gliding" fault, and it survives every animator setting being correct.

  So `Content/animations.txt` carries the speed each locomotive row was animated at, and the clip
  is played at the ratio between that and the ground the person actually covers:

  ```
  moving          1.5m/s  Walking, Walking Male, Walking Female
  hurrying        3.6m/s  Running
  ```

  Omit the figure on a row that is not locomotion and it plays at its natural rate. The ratio is
  capped at 2x - above that the legs blur and no clip can honestly show the speed, which is the
  same admission the primitive figures' leg swing already makes. There is deliberately no floor:
  a slow walk played slowly is a person dawdling, which is correct.

  **If feet still skate, that number is the one to tune.** It is the clip's speed, not the
  simulation's - lowering it to match the sim would put the skate straight back in.

  ### What to download

  Named for the states in `Activity` (see `Core/People/DayPlan.cs` — there are fourteen, and this
  line said thirteen for long enough that the count is not written down here any more), and the
  mapping is
  already written down in `Assets/Noir/Unity/AgentAnimation.cs` - **the code asks for Mixamo's own
  clip names verbatim**, so there is no translation step between what you downloaded and what it
  wants. Name the Animator state exactly what Mixamo called the clip.

  | clip | covers |
  |---|---|
  | **Walking** (in place) | `Walking`, `TravellingTo` - the workhorse |
  | **Standing Idle** | the fallback for everything, and `AtWork` |
  | **Running** (in place) | **nothing — it has never played once.** See below |
  | **Talking** | `Talking` |
  | **Drinking** | `AtThePub` |
  | **Looking Around** | `Shopping` |
  | **Digging** | `OnTheAllotment` |
  | **Sitting Idle** | `AtChurch`, `AtSchool`, `AtHome`, `Visiting` |
  | a second and third **idle** | so a street is not a row of identical statues |

  `Asleep` needs nothing. They are indoors, behind a wall, in the dark, and the only thing that
  ever shows it is the window not being lit.

  RUNNING IS NOBODY, AND THAT IS NOT WHAT THIS PARAGRAPH USED TO SAY. It read "the child at play
  and nobody else… `AgentAnimation` enforces it", and the enforcement is real — but the row it
  guards **has never played a single frame and cannot**. `hurrying` is only true of somebody
  *moving* while `AtThePlayground`, which the simulation never produces: a child at the playground
  is at the playground, not travelling to it. Measured, not inferred — `Running=` appears zero
  times across all three recorded runs against censuses reporting up to 317 people on the move.

  The reasoning above is still right, which is why the row is being deleted rather than fixed: if
  running belongs in this game it belongs to a story event — somebody fleeing — not to a commute,
  and the simulation's ceiling is about 3.4 mph against a clip authored for 8 mph. Scheduled in
  `docs/ANIMATION-FIXES.md` W5. **Do not download `Running` on the strength of this table.**

  ### Into Unity

  Drop the FBXs in `Assets/Noir/Animations/`. **That is the whole procedure** — then add the clip's
  name to a row in `Content/animations.txt` and run `Noir/Build The Townsfolk Animator`.

  `AnimationImport` configures each file on the way in: Animation Type **Humanoid**, Avatar
  **Create From This Model**, and **Loop Time** on. No materials to sort out, because you
  downloaded without skin.

  **DO NOT DO IT BY HAND, AND THIS SECTION USED TO TELL YOU TO.** It listed the Rig and Animation
  tabs step by step, which is worse than redundant: `AnimationImport` only acts when
  `importSettingsMissing` is true — first import and no other time — so any setting a human applies
  by hand becomes permanent and invisible, and the automation will never correct it. Following the
  old steps could not fail loudly; it just quietly took a file out of the system's hands forever.

  It also said to leave Loop Time **off** for one-shots. The importer loops everything, and there
  are about a dozen one-shot gestures on the list — so the document and the tool disagreed, and the
  tool wins on every file either way. Whether looping a one-shot is actually wrong is a real
  question and it is open: `docs/ANIMATION-FIXES.md` decision 7 wants it measured before anybody
  touches 87 import settings, because `loopPose` does not merely mark a clip, it *warps* it.

  Mecanim retargets through the Humanoid abstraction, so a clip authored on Mixamo's skeleton plays
  correctly on our people despite them being a different height and build. **No scaling needed.**

  ### Two things to know before starting

  - **The player probably does not need Mixamo at all.** Unity Starter Assets ships its own
    walk/run/idle/jump already wired to a controller. Mixamo is for the TOWNSPEOPLE's activity
    states, which is the thing Starter Assets cannot give you.
  - **One of the 79 characters is not Humanoid, and it is `SKM_Woman_Rabbit_Easter`** —
    `animationType: 2` (Generic) where the other 78 are `3`, in
    `Poly Universal Pack/Meshes/People/Seasons People/`. It would silently refuse to retarget, so
    it would read as a broken clip rather than an import setting.
    **It is never cast, so this costs nothing today.** `AgentBody` draws from the ordinary
    townsfolk; a woman in an Easter rabbit costume is not going to be walking down Chicago Street
    in 1991 whatever her rig says. Left alone deliberately — changing an import setting on a pack
    asset that nobody uses is churn. It matters only if somebody widens the cast, and then the fix
    is one dropdown.
    *(This line used to say "whichever it is", which is a note asking the next person to redo the
    search. It took one `rg --no-ignore` — the pack is gitignored, so a plain grep finds nothing
    and reports the folder as empty.)*

  ### Where it lands

  Already wired and inert until the clips exist: `AgentAnimation.Drive` returns immediately on a
  null Animator, a missing controller, or a state the controller does not have, and
  `AgentFigure.Animator` is null for the primitive figures we draw today. So the city keeps running
  while the set is half imported, which is what makes importing one clip at a time possible at all.

---

## Atmosphere — the biggest gain per pound

- [ ] **HAZE - Volumetric Fog & Lighting for URP** — ~$40.
  [Asset Store](https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/haze-volumetric-fog-lighting-for-urp-336656)

  **TOP PICK.** Light shafts under the street lamps. We have just replaced the cylinder-and-box
  lamp posts with the pack's own `Lamp_Street_*`, each carrying a real point light, and a wet noir
  street at 22:00 with cones of light down it is the single biggest mood gain available to this
  project. It is a fullscreen camera effect, so filter 2 does not apply to it at all.

  Watch for: it composites with `PostFx`; check the two do not both grade the image.

- [ ] **COZY: Stylized Weather 3** — ~$60.
  [Asset Store](https://assetstore.unity.com/packages/tools/utilities/cozy-stylized-weather-3-271742)

  Rain, snow, seasons, and it is explicitly STYLISED - which is why it is above Enviro here.
  Rain on the asphalt at night is most of the noir look for very little work.

  Watch for: it wants to own time of day. `SunRig` already drives sun angle, fog colour and ambient
  off `Sim.Clock`, deliberately, so that how fast the day passes is a property of the game. Give
  COZY the weather and keep the clock ours.

- [ ] **Altos - Volumetric Clouds, Skybox and Weather** — ~$50.
  [Asset Store](https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/altos-volumetric-clouds-skybox-and-weather-for-unity-urp-221227)

  Volumetric clouds, which now have 270 metres of open country on every side to be seen over.
  Overlaps COZY on weather - pick one, not both.

- [ ] ~~**Enviro 3 - Sky and Weather**~~ — ~$80. Most complete of the four, and the one I would
  NOT take: it wants to replace `SunRig` wholesale, and `SunRig` is what lights the windows when
  somebody is home, glows the lamp lenses, and is wired to the simulation clock. Only revisit this
  if we ever want its whole system rather than a piece of it.
  [Asset Store](https://assetstore.unity.com/packages/tools/particles-effects/enviro-3-sky-and-weather-236601)

---

## Audio — currently there is none at all

`OVERNIGHT.md` has said "No audio at all" since the beginning. This is the cheapest large gain
after the lighting.

- [ ] **Footsteps Pack** — ~$30. 3,011 samples: concrete, grass, mud, gravel, water, metal, snow.
  [Listing](https://unityassetcollection.com/footsteps-pack-free-download/)

  NEARLY FREE TO WIRE, which is the point: the map already knows the terrain of every tile, so
  `world.Grid.TerrainAt` picks the surface and the sample follows. Every other game has to author
  that mapping by hand.

- [ ] **Ambient Sounds - Interactive Soundscapes for Unity 6** — ~$45.
  [Asset Store](https://assetstore.unity.com/packages/tools/audio/ambient-sounds-interactive-soundscapes-for-unity-6-142132)

  Global, 1D, 2D and 3D zones, which maps straight onto what the map already is: downtown, the
  suburb ring, the country frame, and the individual places inside them.

- [ ] **City Ambience Sound** — ~$20. Raw material for the above.
  [Asset Store](https://assetstore.unity.com/packages/audio/ambient/urban/city-ambience-sound-309820)

---

## People

The full survey is in `IDEAS.md` and `Assets/Noir/Editor/PeopleProbe.cs` reproduces it. The short
version: **79 rigged humanoids, of which about 22 are in register** for an ordinary town - ten
Slavic, nine Steampunk, two Farm, and about four of the film crew. No contemporary crowd, and
**no elderly figure anywhere in the pack**, so `AgeBand` can express adult and child and nothing
else whatever we buy.

- [ ] **PolyActors - Modular City People** — ~$50. Same publisher, so the style will match.
  [Publisher](https://assetstore.unity.com/publishers/19123)

  The only people pack worth considering, and NOT for variety - variety is already solved, see
  below. Buy this if the Slavic and Steampunk register bothers you once there are figures on
  screen and you want ordinary modern clothes: suits, overalls, shop coats.

**Variety is already solved and needs no purchase.** `Universal_A_Alb` is a labelled swatch grid,
and the grid is **32 x 32 cells of 128 px on a 4,096 px sheet** — measured off the file on
2026-08-09, because this paragraph, `docs/IDEAS.md` and four comments in `AgentBody.cs` all said
sixteen or sixty-four and a safety argument was built on top of it. Each ROW is a role, labelled on
the sheet: 1 skin, 2 hide, 3 hair, 9 stone, 13 tertiary, 14 secondary, 15 primary, and so on.
**Along a row, columns 0–19 are a twenty-step ramp** from near-black to white; **columns 20–29 are
one flat colour repeated ten times**; column 30 is the only non-black column in
`Universal_A_Emit.png`; column 31 is an accent. Measured, `Man_Slavic_Summer_Hair` puts 2,841
vertices on 27 distinct atlas cells across 10 roles. So a person's coat colour is a UV coordinate,
not a texture, and moving it along its own row recolours that garment and nothing else — **but only
inside columns 0–19.** Wrap past 19 and the garment lands on the flat block or on the emission key,
which is why the shift has to be bounded rather than a modulo. Four roles at twenty steps is 160,000
looks from ONE prefab, against a population of 365. Clone the mesh per citizen, seed the shades off
the citizen id so a person looks the same every run, and no two people ever match.

That also closes a gap the simulation has had since it was written: `PersonDescription.ClothingTone`
is one of six properties that are never constructed, and it can be DERIVED from the swatch we
picked - so what a witness reports is what is actually on screen.

---

## DO NOT BUY, and why

- ~~**Crowd and pedestrian systems**~~ — [Urban Traffic & Pedestrian](https://gamecontentshopper.com/asset/all-assets/urban-traffic-pedestrian-system/2025/03/18/),
  [Mobile Pedestrian System](https://www.gameassetdeals.com/asset/203706/mobile-pedestrian-system),
  [DOTS Traffic City](https://devassetlibrary.com/dots-traffic-city/).

  These simulate WANDERING. Our people do not wander - they have day plans and go to named places,
  and `Agent.At` is a `PlaceId`. A crowd system would fight the simulation rather than serve it.
  What we actually need is locomotion animation, which is free, and a NavMesh, which is built in.

- ~~**Traffic systems**~~ — `CityTraffic` and `LaneGraph` are ours, are covered by the PlayMode
  suite, and the give-way and junction-claim work landed on 2026-07-31 after four wrong theories.
  Replacing tested code with untested code is a bad trade at any price.

- ~~**Low Poly Animated People**~~ ($30) and other animated-character packs — 120 rigged characters
  with animations, but 500-1000 vertices against our ~2,800, so they will not stand next to the
  Universal people. And we do not need the animations: our rigs are Humanoid and Mixamo is free.

- ~~**Low Poly Ultimate Pack**~~ ($150) — heavy overlap with Poly Universal, which we already have.

- ~~**Photoreal environment, material and megascan packs**~~ — see filter 4. They would make this
  world look worse, not better.

---

## If you only buy three

**HAZE** for the light under the new lamps, **Footsteps Pack** because the terrain data to drive it
already exists, and **COZY 3** for weather that does not fight the art style. Comfortably under
$150 together, and every one of them drops in without authoring anything in a scene - which matters
here, because content authored in an editor window is content `MapAudit` and the PlayMode tests
cannot see.

And before any of it: **Unity Starter Assets, free.** Walk around Rossville at eye height with a
real body first. Half of what looks worth buying from a spreadsheet stops looking worth buying once
you have stood in the street.
