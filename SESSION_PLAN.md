# Session Plan — Day 7 (2026-06-11)

## Ticket
**#33 — Git Repo: Update with Scan capability**
Users can link a repository in the Xray → Scans → Git Repositories tab but cannot scan the
files inside it. The column shows no scan data and there is no "Scan" button.

## Impact × Urgency
- **Impact: High** — the core value proposition of the Git Repositories tab is to observe
  supply-chain risk inside source repos; a tab that only lists repos without scanning them
  fulfils no security control.
- **Urgency: Medium** — no regression (the tab was never scanning); new missing capability.
- Priority: implement this session.

## Control mapping
Control **SEC-SRC-01**: git repositories linked for observation must be scannable —
meaning the gate must be able to evaluate their declared package dependencies against the
same vuln sources it uses for Nexus artifacts. Absence of scan capability is a gap in this
control.

## Task (one task, ticket #33)

### What
Add "scan" capability to linked git repositories:
1. **`GitRepoScanService`** (new, `src/Advisory.Api/Scan/GitRepoScanService.cs`):
   - Fetches known manifest files from `raw.githubusercontent.com` for each repo
     (package.json → npm, requirements.txt → PyPI; silently skips 404s).
   - Parses declared dependencies and evaluates each via the gate engine.
   - Stores results in-memory keyed by `fullName`. Updates asynchronously.

2. **Two new endpoints** in `ScansController`:
   - `POST /api/scans/git-repositories/{*fullName}/scan` → start a scan (202 Accepted).
     Returns 404 if the repo is not linked.
   - `GET /api/scans/git-repositories/{*fullName}/scan` → retrieve stored scan result.
     Returns 404 if no scan has been run yet.

3. **Frontend** (`web/src/App.jsx`):
   - Add `api.scanGitRepo` and `api.getGitRepoScan` to the API object.
   - Add a "Scan" button per row in the Git Repositories table.
   - Show scan status (Scanning / Done / Failed), packages found, and worst severity
     inline on the row after a scan has been run.

### Test (proves the control works)
New tests in `GitRepoTests.cs`:
- `GitRepoScan_returns_404_when_repo_not_linked` — confirms the gate doesn't scan
  repos that have not been explicitly linked (prevents scope creep).
- `GitRepoScan_returns_202_for_linked_repo` — confirms start-scan is accepted.
- `GitRepoScanResult_returns_200_after_scan_started` — confirms the result endpoint
  returns a parseable response (any status) after a scan has been initiated.

### Smallest correct change
- One new service file (~100 lines).
- Two new controller actions appended to the existing `ScansController` (no new class).
- Registration in Program.cs (one line).
- Frontend additions to existing `api` object + table rows (~30 lines).
- No changes to auth, policy signing, audit hash-chain, Dockerfiles, or CI.
