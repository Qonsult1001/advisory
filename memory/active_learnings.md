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
- **Shared WebApplicationFactory fixtures accumulate state.** In-memory policy/store state persists across tests in the same class. Split read-only tests (shared fixture) from write tests (fresh factory per test) to prevent bleed. (issue #30)
- **PolicyStore writes to disk — all test factories share the same policy.json.** When write tests run in parallel (xUnit default), concurrent writes corrupt the shared file and read-only test factories load stale/corrupt state. Fix: give every factory that writes policy an isolated `Path.GetTempFileName()` path. Use a shared `IsolatedPolicyFactory` fixture for read-only test classes. (issue #33)
- **`mutate-ide.sh setup` often reports 0 tickets.** Days 8, 10, 11: always verify manually with `gh issue list -l mutation --state open`. If tickets exist, write ISSUES_TODAY.md by hand.
