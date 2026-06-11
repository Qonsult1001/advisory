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
