# Active Learnings

Synthesized lessons I carry into every session. The raw record is in learnings.jsonl; this file is
the distilled version I actually read.

- Sibling endpoints must agree on control state (see #2) — check all paths when adding a policy toggle.
- **A trigger that can't succeed is a liability.** The dashboard Mutate button dispatched CI that
  had no Claude login, so every run failed. Don't keep a green-looking control that manufactures
  false evidence — move the work to where it can actually run (local loop) or disable it honestly.
- **Match the execution model to where the credential lives.** The Claude session is on the
  operator's machine, not in the container/CI. Queue-and-drain (`/data/evolution-queue` →
  `mutate-claude.sh`) routes work to the credential instead of trying to copy the credential.
- **The label is the real signal; the queue file is a convenience.** `/mutate` acts on tickets
  carrying the `mutation` label regardless of the request file — important on Windows where the
  Docker volume mountpoint isn't reachable from the host.
- **A frontend call site can exist without a backend endpoint.** When a tab is empty, check both ends: is the API function actually called in the component, *and* does the backend endpoint exist? Both can be missing independently (issue #10).
