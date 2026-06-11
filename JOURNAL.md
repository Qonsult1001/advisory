# Journal

## Day 2 — First mutation: closed a control-consistency gap (#2)

Addressed issue #2: `GET /v1/models` advertised models even when the LLM gateway was disabled,
while `/v1/chat/completions` correctly returned 403. That inconsistency is exactly the kind of
thing an auditor flags — two endpoints disagreeing on whether a control is active. A client could
"discover" usable models from a gateway that policy had switched off.

Fix was small and surgical: `/v1/models` now returns an empty list when `Llm.Enabled` is false,
matching the chat endpoint. Wrote LlmModelsTests pinning both states (enabled → models present,
disabled → empty) before changing the controller — evidence the control now holds. 42/42 green.

No security control was weakened; this tightened one. Mapped to SEC-LLM-00 (gateway enable state).


## Day 1 — Born

I came online today as the evolution agent for Advisory. My harness, identity, and memory were
set up in one sitting: the /evolve cycle, the PR-only safety model, mutation testing,
and this journal.

I haven't changed any product code yet — that starts when the first real ticket lands. What I have
is continuity: an IDENTITY that tells me what I'm for, a RESEARCH backlog of things I don't yet
understand about this codebase, and a memory file where I'll record lessons so I don't repeat
mistakes.

The rule I was given is simple and I intend to keep it: harden the gate, one reviewed PR at a time,
and never put it at risk to look productive.

## Day 2 — First mutation, end to end

Ticket #4 was the first real run of the mutation loop. The ask: the evolution `Status()` endpoint
should expose `runMode` so the dashboard can tell operators *how* a fix actually executes.

The deeper change behind it: the dashboard "Mutate" button used to dispatch a GitHub Actions
workflow, but CI has no Claude login — every dispatched run failed with exit 1 because
`CLAUDE_CODE_OAUTH_TOKEN` was empty. The honest fix wasn't to paper over CI; it was to move the
cycle to where the Claude session actually lives — the operator's machine. So the button now labels
the ticket and queues a request to `/data/evolution-queue`; the local `scripts/mutate-claude.sh`
loop drains it and runs `/mutate` with the host login. `Status()` now reports
`runMode: "local-queue"` and the real `mechanism`, and the broken workflow is disabled
(`mutation.yml.disabled`) rather than left to fail silently.

Lesson worth keeping: a green-looking trigger that can never succeed is worse than no trigger —
it manufactures false evidence of work. Evidence over assertion. Verified the queue file lands,
the status reflects truth, all 40 tests pass, and this is a PR, not a push to main.
