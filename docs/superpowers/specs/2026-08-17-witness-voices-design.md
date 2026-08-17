# Witness voices — the street reacts, and asking has a cost

Owner request 2026-08-17, watching his first live hit-and-run response: *"like to see
thoughts coming from witness and be able to ask the question as a cop and or a civilian."*
Design settled in chat the same night: **live bubbles** as things happen ("Live bubbles as
it happens"), and for the roles, **both** access and consequence differ ("Both of the
above"): a badge gets fuller answers and an official record; a civilian gets thinner
answers and gets REMEMBERED for asking.

## What exists already, and is kept

- `Recollection.AskInEnglish` — testimony in English, deliberately vague (a standing rule:
  the vagueness is the design). The string-only seam is the Observation firewall:
  Noir.Unity gets sentences, never evidence types.
- The T key — asks the nearest citizen, full answer, any role. Becomes the aimed,
  role-aware ask below.
- The response path knows exactly who notices what, minute by minute: the discoverer
  (BodySeen), each gawker (the drift-over choreography), each canvassed witness, the
  officer's arrival. Phase 1 bubbles ride these known moments — NO new observation
  machinery, NO RNG (the response path's standing rule; variant lines key on citizen id).

## Phase 1 — host and UI only, no Core changes

1. **StreetVoices** (new, `Assets/Noir/Unity/StreetVoices.cs`): floating one-liners over
   citizens' heads. `Say(CitizenId, line, seconds)`; draws display-only IMGUI labels at
   `WorldToScreenPoint` (PlayerInteraction's measured lesson: IMGUI INPUT dies in builds,
   display does not), capped count, distance-culled, fade-out. Position follows
   `Sim.GetAgent(id).Position` so a running discoverer carries their line with them.
2. **Host hooks** in `VillageHost.RunResponse`/`Execute`: discoverer ("oh god — somebody
   get help!" / non-fatal variant), gawker arrival (three variants by citizen id), officer
   arrival ("step back, please — all of you"), canvassed witness ("…and that's everything
   I saw"). Each is one `Say` call beside an existing log line.
3. **The badge is a toggle**: `VillageHost.Badge` (bool, default false = civilian), the B
   key in VillageUI, shown in the top bar, listed on H and in `docs/CONTROLS.md`. How the
   badge is EARNED is a story decision deliberately out of scope.
4. **Aimed ask**: when the player is walking, T asks the citizen nearest the player and
   most in front of the camera (facing dot over candidates within ~6 m), falling back to
   nearest-to-player, then nearest-to-camera from the overview. Panel header names the
   role it was asked under.
5. **Civilian thinning**: with the badge OFF, the panel shows only the LAST two lines of
   the testimony plus a hedge ("…that's about all I'd tell a stranger; you'd want to ask
   around"). Thinning only ever REMOVES — the vagueness rule forbids adding precision, and
   truncation cannot. `Testimony.SawNothing` passes through untrimmed either way.

## Phase 2 — Core, tested, NOT tonight

- **Being asked is an event.** A civilian who questions a witness is REMEMBERED: the
  witness can later testify "somebody was around asking questions about her" — through the
  same event-testimony machinery hits use, behind the same firewall, with Core tests. The
  killer's own canvass becomes a sighting.
- **The badge writes the record**: a badge-on ask about an open case's victim lands in the
  case file via the same seam the county canvass uses.
- Wants its own plan; both features change testimony content and must go through the
  Core gate.

## Out of scope, named so nobody wonders

- LLM dialogue (the scoped-not-built `ILLM` port) — these are the witness layer's own
  sentences, not conversation.
- Bubbles for non-case moments (gossip, greetings) — later, if the case bubbles feel right.
- Any change to WHAT witnesses know or how precisely they say it.
