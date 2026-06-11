# Session Plan — 2026-06-11 (Day 8)

## Ticket: #36 — Git Repositories: owner/repo validation, Edit action, make Scan work

**Control mapping:** SEC-SRC-01, NIST SSDF PO.5 (protect software and its integrity)
**Impact x Urgency:** High x High — the gate accepted malformed repo names, making SEC-SRC-01 scans
silently fail for any repo linked without an owner prefix; and there was no way to correct a repo
without unlink + re-add.

---

### Task 1 — Backend: validate owner/repo format in LinkGitRepo (smallest correct change)

**File:** `src/Advisory.Api/Controllers/Controllers.cs`

After the existing null-check in `LinkGitRepo`, validate that `req.FullName` contains exactly one
`/` with non-empty parts on each side. Return 400 with a clear message if not.

**Tests (new, in `tests/Advisory.Tests/GitRepoTests.cs`):**
- `GitRepoLink_RejectsBareName` -- POST `{ fullName: "test", url: "..." }` -> 400
- `GitRepoLink_RejectsMultiSegmentName` -- POST `{ fullName: "org/repo/extra", url: "..." }` -> 400

(Existing `GitRepoScan_returns_202_for_linked_repo` already covers that a valid `owner/repo`
link scans successfully -- no new scan test needed.)

---

### Task 2 — Frontend: inline validation + Edit action + scan error surfacing

**File:** `web/src/App.jsx`

1. **Inline validation on Link form:** derive `linkFormInvalid` from `linkForm.fullName`; show
   error label "Must be owner/repo (e.g. myorg/payments-api)" when non-empty and format is wrong;
   disable the Link/Save button.

2. **Edit action:** extend `linkForm` with an optional `origFullName` field to drive edit mode.
   On edit-pencil click: open `linkForm` pre-filled with the row's data + `origFullName` set.
   `handleLink` in edit mode: DELETE `origFullName`, then POST new data.
   Panel title changes to "Edit Repository" in edit mode.

3. **Scan error surfacing:** add `scanErrors` state (fullName -> error string). In `handleScan`,
   `.catch(...)` sets the error instead of silently swallowing it. Render the error inline in the
   Scan Status cell so the user knows why a scan failed.
