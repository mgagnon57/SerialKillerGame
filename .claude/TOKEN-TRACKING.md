# Token Tracking & Budget Safeguards

**Session Start:** 2026-08-01 22:00 (10PM)  
**Budget:** 20X reset token allocation  
**Goal:** Complete terrain + parcel workflow without runout

## Tracking Checkpoints

> **I CANNOT SEE MY OWN TOKEN USAGE.** Corrected 2026-08-01, 10PM. The original version of
> this file asked me to report "Tokens spent this stream: X,XXX" at every gate. I have no
> way to observe that number, so every figure I printed would have been invented — and an
> invented number in a budget guard is worse than no number, because you would have steered
> by it. The 50/70/85% red flags below can only be called by YOU, from your own usage
> display.

After each stream completes, I report only what is actually observable:

```
STREAM N COMPLETE
├─ Wall-clock elapsed: XX min
├─ Agents spawned: N
├─ Files changed: N   (+ the list)
├─ Tests run / result: Preflight, PlayTests, Core
└─ What is left in this stream: ...
```

Then I stop and ask. You read your real usage and reply `continue`, `stop`, or
`adjust-effort medium|high`.

## Red Flags (Stop if any trigger)

| Flag | Condition | Action |
|------|-----------|--------|
| 🟡 **Caution** | >50% budget spent after Stream 1 | Reduce effort level for Streams 2-4 |
| 🔴 **Warning** | >70% budget spent after Stream 2 | Skip Stream 3, go straight to verification |
| 🛑 **Stop** | >85% budget spent | Pause, commit work, resume next reset |

## Token-Saving Strategies

If burn rate is high:

1. **Lower effort level** — Switch from `high` to `medium` (halves expensive model reasoning)
2. **Reduce review depth** — Fast model: "implement only", skip Expensive review (risky, use only if time-critical)
3. **Batch testing** — Test Streams 1+2 together, then review both together (saves context switches)
4. **Skip non-critical streams** — If running low, deprioritize Stream 4 (performance) over Streams 1-3

## Lessons Learned

**Why you ran out this week:**
- Running agents in background + foreground simultaneously
- Multiple parallel reviews without batching
- High effort level on every task
- Not tracking mid-session

**This session approach:**
- ✅ Track at every checkpoint
- ✅ Stop/pause if approaching 85%
- ✅ Report burn rate so you can adjust
- ✅ Default to `medium` effort (can always re-run with `high` next reset if needed)

## During Session

**I will message after each stream:**
```
Stream [N] complete. [XX] min elapsed, [N] agents, [N] files, tests [result].
Continue to Stream [N+1]? (yes/no/adjust-effort)
```
No token figure, because I cannot measure one. You supply that half.

**You decide:** 
- `yes` → continue as planned
- `no` → stop, commit, resume next reset
- `adjust-effort medium` → lower reasoning depth for remaining streams

## Post-Session Report

After all streams or when paused:
```
Session Summary
├─ Started with: XXXk
├─ Spent: XXXk
├─ Remaining: XXXk
├─ Streams completed: 1/2/3/4
└─ Recommendations for next reset
```

---

**The key:** You control it. If tokens are running low, you say `stop` and I save progress. No runout, no wasted budget.
