# Journal

## Day 6 — The list that told too much (#30)

Ticket #30 was blunt: "not my private shit." The Git Repositories tab was calling the GitHub API
and dumping every repo the configured owner has — public AND private — to anyone with viewer
access. That's not under-observation; that's unintentional disclosure. A compliance officer spots
that immediately: data minimisation, NIST SSDF PO.2, and frankly just common sense.

The right fix was architectural, not cosmetic. The feature was designed around "configured owner
→ auto-list all repos," which is the wrong model for a governance tool. A security gate should
show exactly what you've decided to watch, not everything that happens to exist. So I swapped it:
`LinkedGitRepos` lives in the signed policy now — add a repo explicitly, and it shows; don't add
it, and it stays invisible. Every add/remove goes through `PolicyStore.UpdateAsync`, so it's
versioned and in the audit trail, just like approving a model or granting an exception.

Three new endpoints, four test cases, a frontend with a "Link Repository" form and a ✕ per row.
The one thing I had to think through was test isolation: the shared `WebApplicationFactory` fixture
accumulates in-memory policy state across tests, so a POST in one test would bleed into the next.
Separated read-only tests (shared factory) from write tests (fresh factory per test). Clean.

The `IGitRepoClient` / `GitHubRepoClient` service is still registered in DI but no longer consumed.
I left it rather than deleting it — removing registered services is a separate change with its own
risk surface (something else could conceivably depend on it via the container), and that cleanup
wasn't in the ticket. Document and move on.

49/49 green. PR #31, not a push to main.

## Day 5 — The endpoint that nobody could phone home on (#27)

Ticket #27 asked for the smallest possible addition: `GET /api/health` returning `200 { status: "ok" }`,
anonymous, for container orchestrators and uptime monitors. No gate logic. No policy. Genuinely the
kind of gap that only surfaces when you try to deploy to a real environment and the scheduler marks
the pod unhealthy before it even starts serving traffic.

The implementation is three lines in Program.cs: a `MapGet` after `MapControllers`, `.AllowAnonymous()`,
and a `Results.Ok` with a two-field anonymous object. Tests first — two assertions, status code and
body property. 47/47 green. PR #28 open.

What this session reminded me: not every ticket is a bug in the gate. Some are simply missing
infrastructure that a deployed service is expected to provide. The compliance officer framing still
holds — the obligation here is operational availability (NIST SSDF RV.1), and the evidence is the
test passing plus the endpoint being unreachable without `.AllowAnonymous()` if I'd omitted it.
I verified both sides before shipping. No control was weakened; a missing one was added.

The wrap-up commit swept in a package-lock.json churn again (npm audit bump from a prior run).
That's cosmetic noise in the PR diff but harmless. I've noted it before and I'll note it again:
the lock-file drift is background hygiene, not a mutation concern.

## Day 4 — The tab that called an endpoint that didn't exist (#10)

Ticket #10 pointed at the "Git Repositories" tab in Xray → Scans List and said it was empty.
The screenshot showed the first sub-tab selected and nothing rendered. Straightforward enough
on the surface, but I wanted to know *why* before touching anything.

The frontend already had `api.getGitRepos()` defined, pointing at `GET /api/scans/git-repositories`.
The `ScansRepos` component had the "Git Repositories" tab in its tab list. But the component never
called `getGitRepos()` — it only called `getScans()` for Nexus repos, and the git tab fell through
to a generic placeholder. And the backend had no such endpoint at all. So the tab was empty for two
separate reasons: no API call from the frontend, and no endpoint on the backend to answer it.

The fix was layered correctly: create `IGitRepoClient` / `GitHubRepoClient` following the same
unconfigured-safe pattern as `INexusClient` (no GITHUB_OWNER → configured: false, empty list,
never a crash); add the endpoint to `ScansController`; wire the frontend tab to actually call the
API and render the table. The client also auto-derives the owner from `EVOLUTION_REPO` if
`GITHUB_OWNER` isn't set, which means operators who are already running the evolution loop get
the git list "for free".

Two new tests pin it: endpoint returns 200, and unconfigured state returns the expected shape.
45/45 green. No security control was touched.

What I noticed: the `mutate-ide.sh finish` script swept up Windows npm cache files into the
wrap-up commit because `C:\Users\Carter/` wasn't gitignored. The `.gitignore` already has a
partial entry but the Windows-style absolute path slipped through. I didn't fix it here —
that's a separate ticket, not scope creep from #10.

## Day 3 — A bug that was real but unreachable (#7)

Ticket #7 said `LuhnValid` had no upper bound — a PAN is 13–19 digits, but the checksum would
bless a 22-digit run too. True on its face. So I wrote the regression test first: a 22-digit
Luhn-valid number must not be classified as a card.

It passed before I touched the helper. That stopped me. The reason: the *card candidate* regex
already clamps to `{13,19}`, so an over-length run never reaches `LuhnValid` on the card path. The
only other caller is SA-ID validation, which is always exactly 13 digits. The missing bound was
unreachable in practice — defensiveness the callers happened to provide.

I could have shipped the failing-then-passing story and looked productive. Instead I fixed it
honestly: added `> 19` to `LuhnValid` as **defense-in-depth** (so the helper is correct regardless
of who calls it), kept the integration test that pins the 13–19 contract end-to-end, and said
plainly in the PR that the candidate regex is the primary guard and this hardens the secondary path.

Lesson: when a test for a "bug" passes on the first try, the bug isn't where the ticket thinks it
is. Find out why before claiming a fix. Evidence over assertion — including evidence against my own
ticket. 43/43 green. PR, not a push to main.

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
