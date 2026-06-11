# Session Plan — Day 6 (2026-06-11)

## Ticket #30 — Git Repositories Scans List: replace auto-listing with manual repo management

**Control mapping:** Data minimisation (NIST SSDF PO.2 / GDPR). The previous behaviour
auto-listed ALL private GitHub repos for the configured owner, exposing an operator's full
repo inventory to anyone with viewer access. The fix gives admins explicit control over
which repos are under observation.

**Impact:** High — private repo inventory exposed by default.
**Urgency:** High — user explicitly reported the gap.
**Score:** High × High → address this session.

### Task 1: Update tests first

Update `tests/Advisory.Tests/GitRepoTests.cs`:
- `GitRepositories_unconfigured_returns_empty_list` → now expects `configured: true, count: 0`
  (endpoint is always functional; no external config required).
- Add `GitRepositories_link_and_list` → POST a repo, GET returns it.
- Add `GitRepositories_unlink` → POST then DELETE, GET returns empty list.

### Task 2: Model — add `LinkedGitRepo` + `LinkedGitRepos` to `FirewallPolicy`

Add `LinkedGitRepo` record to `FirewallPolicy.cs`. Add `List<LinkedGitRepo> LinkedGitRepos`
property (empty default). Entries are persisted in the signed policy — changes are versioned
and auditable.

### Task 3: Backend — change GET, add POST/DELETE in `ScansController`

- `GET /api/scans/git-repositories` → return `policy.LinkedGitRepos` (always `configured:true`).
  Remove dependency on `IGitRepoClient.IsConfigured`.
- `POST /api/scans/git-repositories` (Admin) → link a new repo (FullName + Url required).
  Idempotent by FullName.
- `DELETE /api/scans/git-repositories/{*fullName}` (Admin) → unlink by FullName.

### Task 4: Frontend — replace auto-list with manual add/remove UI

- `GET /api/scans/git-repositories` response: `configured: true` always; no "GITHUB_OWNER unset" card.
- "Link Repository" button opens a small inline form (FullName + URL fields).
- Each row gains a Delete (unlink) icon that calls `DELETE /api/scans/git-repositories/{fullName}`.
- Add `linkGitRepo` / `unlinkGitRepo` helpers to the API object.

### Build verification
```
dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo
dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo
npm --prefix web run build
```
