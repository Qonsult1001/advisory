# Session Plan — Day 4

## Ticket: #10 — Git Repositories inside Xray Scan List

**Problem:** The "Git Repositories" tab in Xray > Scans List is empty. The backend
only queries Nexus for package repositories; there is no integration that lists Git repos
from a connected GitHub org/owner. The frontend tab falls through to a generic placeholder.

**Impact:** Medium — operators cannot see which source-code repos are under observation,
which is a gap in the supply-chain visibility the Scans List is meant to provide.

**Urgency:** Medium — the tab exists and is visible to users but shows nothing; this is
a confusing experience that implies the feature is broken.

**Score:** Impact x Urgency = Medium-Medium → address this session.

### Task 1: Add GitHub repository client (`IGitRepoClient`)

Create `src/Advisory.Api/Integrations/GitHubRepoClient.cs`:
- Interface `IGitRepoClient` with `bool IsConfigured` and `ListRepositoriesAsync()`
- Configured via `GITHUB_OWNER` (org or user) and optional `GITHUB_TOKEN`
- Calls GitHub REST API `GET /orgs/{owner}/repos` (falls back to `/users/{owner}/repos`)
- Returns a list of `GitRepo` records (name, url, defaultBranch, visibility, language, lastPushed)
- Follows the same pattern as `INexusClient`: unconfigured = empty list, no crash

### Task 2: Add `GET /api/scans/git-repositories` endpoint

Extend `ScansController` in Controllers.cs:
- Inject `IGitRepoClient`
- New `[HttpGet("git-repositories")]` action
- Returns `{ configured, count, repositories }` matching the Nexus endpoint shape
- When unconfigured, returns `{ configured: false, repositories: [] }`

### Task 3: Wire frontend "Git Repositories" tab

In `web/src/App.jsx`:
- Add `api.getGitRepos()` call to `GET /api/scans/git-repositories`
- When `sub === "git"`, fetch and render git repos in a table with appropriate columns
  (name, visibility, language, default branch, last pushed)
- Show "not configured" card when `configured: false`

### Task 4: Write test

Add `GitRepoTests.cs`:
- Test that `GET /api/scans/git-repositories` returns 200 with `configured: false` and
  empty repositories (no GITHUB_OWNER set in test env) — mirrors the Nexus unconfigured pattern
- This confirms the endpoint exists, the DI wiring works, and the unconfigured state is safe

### Build verification
```
dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo
dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo
npm --prefix web run build
```
