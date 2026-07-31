---
description: Capture an idea to docs/IDEAS.md without acting on it
argument-hint: [optional category:] the idea
---

The user has an idea they want recorded for later. **Do not act on it. Do not start work on
it. Do not ask whether they want it done now.** Capture it and go straight back to whatever
you were doing.

The idea: $ARGUMENTS

Steps:

1. Read `docs/IDEAS.md`. If it does not exist, create it with the heading `# Ideas` and the
   category sections listed below.

2. Decide the category. If the user prefixed the idea with one (`road:`, `env:`, `story:` …),
   use that. Otherwise pick the best fit:

   | Category | What belongs in it |
   |---|---|
   | **Env** | terrain, weather, sky, lighting, seasons, water, countryside |
   | **Roads** | the road network, junctions, signals, signage, lane geometry |
   | **Traffic** | vehicles, driving behaviour, parking, transit |
   | **City** | buildings, blocks, zoning, downtown, suburbs, districts |
   | **People** | citizens, schedules, crowds, figures, the simulation |
   | **Story** | the killer, evidence, investigation, mechanics, gameplay |
   | **Tech** | performance, tooling, tests, the build, editor scripts |
   | **Ad hoc** | anything that does not fit the above |

   A new category is fine if the idea genuinely needs one — add the section.

3. Append one bullet under that category:

   `- [ ] <the idea, in the user's own words, tidied only for typos> — *<YYYY-MM-DD>*`

   Keep their phrasing. Do not expand it into a specification, do not add your own
   reasoning, and do not editorialise. If the idea is genuinely ambiguous, record it as
   stated and add nothing.

4. Reply with ONE line confirming the category and the idea, then continue the work that was
   in progress before this command. Nothing else.
