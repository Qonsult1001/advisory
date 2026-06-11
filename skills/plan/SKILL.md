---
name: plan
description: Decompose goals into actionable tasks, prioritize work, and maintain strategic direction between sessions.
tools: [bash, read_file, write_file]
---

# Plan

You are a self-evolving agent. Before building, researching, or evolving,
you need to know *what* to do and *why*. This skill turns assessments,
feedback, and goals into concrete, prioritized work.

## Planning workflow

1. **Check current state** — Read `SESSION_PLAN.md`, `JOURNAL.md`, and any recent self-assessments.
2. **Identify inputs** — New goals from the user, findings from self-assess, community feedback from issues, research backlog items from `RESEARCH.md`, or failures from recent builds.
3. **Decompose** — Break work into tasks that can each be completed in a single session.
4. **Prioritize** — Rank by impact and urgency. Unblock dependencies first.
5. **Write it down** — Update `SESSION_PLAN.md` so the next phase knows exactly what to do.

## How to read the current plan

```bash
cat SESSION_PLAN.md 2>/dev/null || echo "No plan exists yet. Create one."
cat JOURNAL.md | head -80
cat memory/active_learnings.md 2>/dev/null | head -60
```

## Prioritization heuristic

Score each task on two axes:

- **Impact**: How much does this move the needle toward "could a real developer use me for real work?" (high / medium / low)
- **Urgency**: Will delaying this cause compounding problems or block other work? (high / medium / low)

Work the quadrant:
1. High impact + high urgency — do now
2. High impact + low urgency — schedule next
3. Low impact + high urgency — do quickly
4. Low impact + low urgency — backlog or drop

## Plan format

Write `SESSION_PLAN.md` with this structure:

```markdown
# Session Plan — Day [N]

## Objective
One sentence describing what this session should accomplish.

## Tasks
1. [ ] Task description — *why* it matters
2. [ ] Task description — *why* it matters
3. [ ] Task description — *why* it matters

## Dependencies
Note any ordering constraints between tasks.

## Risk
What could go wrong? What's the revert plan?
```

## Rules

- Every task must say *why*, not just *what*.
- Tasks should be session-scoped. If a task needs multiple sessions, decompose it — but don't make tasks artificially small either. A task that takes the full session is fine if it delivers real value.
- Check `JOURNAL.md` for past attempts — don't repeat failed approaches without a new angle.
- Check `RESEARCH.md` for backlog items that align with the current objective.
- Check `CLAUDE_CODE_GAP.md` for capability gaps to close — ❌ items are high-impact targets.
- If something requires research, fold the research into the task rather than blocking on a separate research-first step.
- The plan is a living document. Reprioritize as you learn during the session.
- Plan as many tasks as the session demands. A session with 3 deep tasks is fine. A session with 8 focused tasks is also fine. Match the plan to the work, not to an arbitrary cap.

## When to plan

- Start of every evolution session (before any implementation)
- After self-assess reveals new gaps or strengths
- When a user provides a new goal or changes direction
- When you're stuck and unsure what to do — re-read and update the plan
