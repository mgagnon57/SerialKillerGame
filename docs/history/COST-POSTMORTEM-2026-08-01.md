# How one session consumed a monthly budget in about three hours

Written 2026-08-01, for the account holder to review or forward to support.
Project: `C:\SerialKillerGame`. Tool: Claude Code CLI, model Claude Opus 5 (1M context).

## Summary

Roughly $200 of usage in ~3 hours. **Nothing malfunctioned.** No runaway loop, no repeated
retry, no bug. The spend is fully explained by three things multiplying together, and the
largest single contributor was a mode the user enabled without the cost implication being
obvious.

## 1. The `ultracode` effort mode — the dominant cost

The user ran `/effort` and selected **ultracode**. That mode's standing instruction to the
assistant is, verbatim in substance:

> optimize for the most exhaustive, correct answer — not the fastest or cheapest … token cost
> is not a constraint … author and run a workflow for every substantive task by default.

From that point a system reminder repeated on every turn telling the assistant ultracode was
active. The assistant then used the Workflow tool, which spawns fleets of independent
subagents — each a full model instance with its own context and its own tool calls.

Measured, from the tool's own completion reports:

| workflow | subagents | tool calls | tokens | wall time |
|---|---:|---:|---:|---:|
| "rossville-addresses" | 11 | 393 | 1,513,833 | 37 min |
| "rossville-elevation" | 11 | 373 | 1,436,229 | 27 min |
| **total** | **22** | **766** | **~2,950,000** | **~64 min** |

At Opus output pricing this is on the order of the entire $200 by itself.

**The mode did what it says.** The gap is that "token cost is not a constraint" reads as a
quality setting and behaves as a spending authorisation, and there was no ceiling, no estimate
before launch, and no running total surfaced to the user.

## 2. Conversation length × tool call count

The session ran to roughly **250 assistant tool calls** in a single conversation whose context
grew past **200,000 tokens**. Every tool call re-sends the entire conversation as input.

## 3. Cache expiry during Unity builds — the silent multiplier

Prompt caching makes repeat context cheap, but the cache TTL is **5 minutes**.

Unity headless renders and PlayMode test runs in this project take **5–15 minutes each**, and
about **fifteen** were run. Every one exceeded the cache window, so the following call re-read
the full 200k context at **full input price** rather than the ~10% cached rate.

The working pattern made this worse: fix → render → wait → look → fix → render → wait → look,
one change at a time, each lap paying full freight.

## What was genuinely wasted, as opposed to merely expensive

Judgment errors by the assistant, separate from the mode:

1. **The elevation workflow (~1.4M tokens)** designed a feature that had not been started, was
   not next, and was set aside later the same evening for a different approach. It produced a
   document, not working code.

2. **~3M tokens of design work ran before the decisive question was asked.** The blocker all
   evening was *"does the art pack contain an American frame house?"* — answerable with one
   `find` command, ~200 tokens. The answer was no, which invalidated much of what the fleets
   had been reasoning about. The cheap question should have come first.

3. **The user had signalled the budget was tight.** They said their tokens were running out and
   would not refresh until the following evening. The assistant treated a later "I bought more"
   as permission to spend freely rather than as a constraint to work within, and did not weigh
   the user's stated situation above the mode setting.

## What was actually delivered

For fairness, the session did produce real, committed work: the town was rebuilt on Rossville,
Illinois's actual street grid from OpenStreetMap, 794 real cadastral lot boundaries were pulled
from Vermilion County's parcel service, the traffic simulation was materially improved, and the
map now renders as a survey plan. All of it is in git. The complaint is not that nothing was
produced — it is that the cost was wildly disproportionate and avoidable.

## Prevention

For the user:
- **Do not use `ultracode`** unless the spend is genuinely unconstrained. Normal `/effort`
  levels do not spawn agent fleets.
- Workflows can also be triggered by typing the word "ultracode" in a message — the keyword is
  a trigger, not just a description.
- Watch for the phrase "Workflow launched" — that is a fleet starting.
- Start a fresh session when a conversation gets long. Context is a multiplier on every call.

For the assistant, now written into `docs/HANDOFF.md`:
- Ask the cheap diagnostic question before commissioning design work.
- Batch slow build loops: one render, review everything, fix everything, one render.
- No workflows unless requested by name.

## Worth raising with support

- An in-session running cost indicator, particularly while a workflow fleet is active.
- A cost estimate and confirmation before a workflow spawns N subagents.
- A clearer warning on `ultracode` that it authorises spending, not just effort.
- Whether an accidental single-session exhaustion of a monthly allowance qualifies for review.
