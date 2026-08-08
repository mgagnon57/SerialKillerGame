# History — session records, not instructions

Everything in this folder describes a moment. It was true when it was written and much of it is
not true now. **Nothing here is a source of truth. `CLAUDE.md` at the repo root wins.**

These were moved out of `docs/` on 2026-08-08 because they had stopped reading as history and
started reading as documentation. Between them they stated **six different Core test baselines**
(72 · 227 · 316/318 · 323/325 · 341 · 359), none matching the suite; two of them dropped
`-c Release` from a command the older ones had right, so the knowledge had actively regressed.
`docs/STATE.md` — 1,464 lines, eleven mutually contradictory baselines, and a 960×960 map that
has been 2100×2400 for months — was deleted outright rather than moved. It is in git:

```
git show 36d4067:docs/STATE.md
```

## Why keep the rest at all

The postmortems are the valuable part, and they are valuable precisely because they are dated.
`POSTMORTEM-2026-08-03-ROADS.md` is the record of measuring a road's cross-section ±14 m around a
centreline when the right-of-way was 17 m away — a window that never contained the answer — and
then generalising from that one mis-framed sample. `COST-POSTMORTEM-2026-08-01.md` is the record
of what a session costs when the Unity loop is not batched. Read them as case studies. Do not
take a command, a count or a file path out of one without checking `CLAUDE.md` first.

Anything in here that was still load-bearing on 2026-08-08 was lifted into `CLAUDE.md` before
these were archived — the traps, the preconditions, and the content-file map.

## The rule that put them here

Across one branch the documentation took 867 additions and exactly one deletion, while the C#
took 4,289 additions and 1,227 deletions. Code gets refactored; documentation only ever accretes.
Nothing here got corrected, so nothing here could be trusted.

**Do not add files to this folder as a way of keeping a session's notes.** If something learned
is still true tomorrow, it belongs in `CLAUDE.md` — replacing what was there, not appended after
it. If it is not still true tomorrow, it does not belong in the repo.
