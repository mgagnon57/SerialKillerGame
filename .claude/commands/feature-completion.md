---
description: Start a feature completion workflow - discovery, design, and multi-agent implementation
argument-hint: feature name [; context] [; effort: low|medium|high|max]
---

# Feature Completion Workflow

Starting feature: **$ARGUMENTS**

## Process

This workflow takes any game feature—whether brand new or mid-development—through three phases:

1. **Discovery** — Read what exists, ask clarifying questions, understand scope
2. **Design** — Propose approaches, write spec, get your approval
3. **Launch** — Give you a ready-to-paste multi-agent workflow prompt

## Usage

Tell me the feature name and I'll start discovery by reading the codebase and asking what's already been done.

**Examples:**

- `/feature-completion animation` — just the feature name
- `/feature-completion animation; 70% done, need to finish character blend trees` — feature + context
- `/feature-completion assets; brand new, PolyPerfect pack just landed; effort: high` — feature + context + effort level
- `/feature-completion investigation-mechanics; effort: max` — feature + high reasoning depth

## Effort Levels

Pass `effort: low|medium|high|max` to control reasoning depth:

| Level | Use When | Cost |
|-------|----------|------|
| **low** | Feature is straightforward, quick decisions needed | Cheap, fast |
| **medium** | Normal feature work, balanced depth (default) | Moderate |
| **high** | Complex feature, multiple considerations, edge cases matter | More tokens |
| **max** | Critical system, architectural decisions, comprehensive review needed | Most thorough |

The effort level applies to **both discovery questions AND agent workflows** — the prompts you paste at 10PM will inherit it.

## Next Step

Tell me the feature name and I'll start discovery.
