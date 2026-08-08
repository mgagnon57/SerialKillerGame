# Rossville, Illinois, 1991 — controls

Press **H** in game for this same list as an overlay.

---

## Time

| Key | |
|---|---|
| **Space** | pause / resume — remembers the speed you were on |
| **`[`** **`]`** | step speed down / up |
| **1**–**6** | skip to 06:00 · 08:00 · 12:00 · 17:00 · 20:00 · 23:00 |

Speeds, slowest to fastest:

```
❚❚    ¼×    ½×    1×    3×    10×    60×    300×
```

Starts at **10×**. At 60× a day passes in about 24 seconds; at 300× in about five.
Drop to **¼×** to watch a single thing happen — someone arriving, stopping, going in.

Skipping is queued rather than instant. A full day is 1.7 million simulation ticks, so it
drains over a few frames instead of freezing the window.

---

## Camera

**Tab** switches between the two modes. This is the most useful key in the game.

### Overview — looking down at the town

| | |
|---|---|
| Right-drag, or **Q** / **E** | orbit |
| **R** / **Shift+F** | tilt up / down |
| Mouse wheel | zoom |
| **WASD** or arrows | pan, relative to where you're facing |

Roofs are **on** when you're far away and **lift off** as you come down close, so from a
distance you see a town and up close you can watch people indoors. It follows the camera;
there's nothing to toggle.

### Street — standing in it

| | |
|---|---|
| Right-drag | look around |
| **WASD** or arrows | walk |
| **Shift** | jog |

Eye height is 1.7 m. Roofs stay on at street level — you're outside looking at buildings.

---

## People

| | |
|---|---|
| Left-click | select someone |
| **F** | follow them (press again to stop) |
| Close | button on the panel, or click empty ground |

The panel shows who they are, who they live with, what they do, where they are right now, and
their whole day block by block. The **amber lines** are their particulars — true, useless
details, which is the point.

---

## What to go and look at

| When | Where | What |
|---|---|---|
| **08:30** (`2`) | the streets around the school campus | the school run — parents walking children, siblings walking together |
| **08:00–09:00** | Chicago Street, at the Church Street junction | people stopping to talk in the street |
| **12:00** (`3`) | the Chicago Street diner | the middle of the day downtown |
| **17:00** (`4`) | the grain elevator | the skyline, and the end of the working day |
| **21:00** (`5`) + **Tab** | an alley, on foot | lit windows where people are home and awake, dark ones where the house is empty |
| **23:00** (`6`) | anywhere, at 60× | the town going to bed, window by window |
| any | zoom in from above | roofs lift off — furnished rooms, people in them |

---

## Reading the town at a glance

| | |
|---|---|
| **Height and build** | children are short, the elderly are shorter and stooped |
| **Orange** | whoever you've selected |
| Which way someone faces | the shoulders give the axis, the hair gives the front |
| Two figures side by side | walking together — same household |
| Two figures stopped, facing each other | having a conversation |
| A small box in someone's hand | carrying shopping, or something from the yard — and that arm stops swinging |
| A chimney | one per home — a terrace with four stacks holds four families |
| A roof of shingle, tile or worn tile | a property of the building, not of the view — it never changes |
| Shutters up on a frontage | that place is shut |

Clothes are drawn from muted 1991 palettes and are stable per person: the same person looks
the same every time you see them, and across restarts.

---

## Command line

From `C:\SerialKillerGame\tools`. None of these need Unity.

```
dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj   # baseline: CLAUDE.md
dotnet run --project Noir.Sim -- check     validate the town layout
dotnet run --project Noir.Sim -- who       population summary
dotnet run --project Noir.Sim -- house 7   one floor plan, as text
dotnet run --project Noir.Sim -- day 3     one person's whole day
dotnet run --project Noir.Sim -- trace 3   follow one person as it runs
dotnet run --project Noir.Sim -- density   where everyone is, hour by hour
dotnet run --project Noir.Sim -- watch     the town moving, in the terminal
dotnet run --project Noir.Sim -- tiles     regenerate all textures
```

`Content/city.txt` is the map. Edit it and run `check` — it reports overlapping buildings,
sealed doors and cut-off houses before you ever open Unity. (`village.txt` was the retired
fixture and no longer exists.)

---

## In Unity

| | |
|---|---|
| **Noir → Smoke Test** | builds the whole town and its geometry, reports what came out |
| **Noir → Render Snapshots** | renders nine set views to `docs/snapshots/*.png` without play mode |
| **Noir → Use 3D Renderer** | re-applies the URP 3D renderer if it ever gets reset |

Press ▶ and it bootstraps itself — there is no scene to set up.

**Render Snapshots** is how the look gets tuned without a human in the loop: it builds the
town in edit mode, lights it with the *same* curve the game uses, and writes nine set views
to disk. Fog, shadow range, texture brightness and the night lighting are all adjusted by
rendering, looking, and rendering again. It is also a regression check — if a change makes the
town look wrong, the pictures show it in one pass.
