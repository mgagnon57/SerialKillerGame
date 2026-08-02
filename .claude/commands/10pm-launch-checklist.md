---
description: Pre-launch checklist for 10PM terrain + parcel workflow - verify state, confirm effort level, start tracking
argument-hint: [optional: effort level override, e.g., "medium" or "high"]
---

# 10PM Launch Checklist

## Before You Start

Run this command at 10PM to verify everything is ready:

```
/10pm-launch-checklist
```

Or with effort override:

```
/10pm-launch-checklist medium
```

## What I'll Check

1. ✅ **Spec is current** — Read latest `2026-08-01-terrain-pipeline-multiagent-design.md`
2. ✅ **Parcel files unchanged** — Verify `parcel-notes.txt`, `parcels.txt`, `elevation.txt` 
3. ✅ **No recent commits** to terrain/parcel systems (nothing to pick up mid-work)
4. ✅ **Token tracking ready** — `.claude/TOKEN-TRACKING.md` loaded
5. ✅ **Launch prompts ready** — Streams 1-4 prompts staged and verified

## What You Confirm

- [ ] You're ready to start (not tired, full focus for 2-3 hours)
- [ ] You have ~500k tokens available (or your budget target)
- [ ] Effort level: **medium** (default, efficient) or **high** (if you have tokens to burn)
- [ ] You understand the token red flags (50%, 70%, 85%)

## Effort Level Guide

- **medium** (default): Fast iteration, balanced reviews, ~17-36k tokens/hour
- **high**: Thorough reviews, edge cases, ~40-60k tokens/hour
- **max**: Exhaustive (only if you're sure about token headroom)

## Launch Order

Once confirmed, I'll provide:

1. **Stream 1 Launch Prompt** (Parcel Annotation) — Copy & paste into terminal
2. After Stream 1 complete: **Stream 2 Prompt** (Sculpt/Paint Tool)
3. After Stream 2 complete: **Streams 3+4 Prompts** (Texturing & Performance) — can run in parallel

Between streams, I'll report:
- Tokens spent this stream
- Total budget remaining
- Burn rate
- Whether to continue or adjust effort

---

**Ready? Say:**
```
/10pm-launch-checklist
```

And I'll verify everything is good to go.
