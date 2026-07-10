# CLI user-manual skeleton

The section order for a production CLI manual. **System-agnostic** — fill each section from the real
command surface; omit a section only if the system genuinely lacks that capability (note the omission).
Lifecycle-first: a reader walks it top to bottom and ends production-ready.

```
# <Product> CLI — User Manual

## 1. Overview            — what the CLI is, the one-paragraph value, who it's for
## 2. Install & verify    — install per platform; `--version`; confirm it runs
## 3. Concepts            — the few domain terms a user must know (from CONTEXT.md); the
                            unit-of-state model (one base vs many) — the most-confused choice

## — LIFECYCLE —
## 4. Create              — make a base/workspace/db; the create command + every flag; modes/tiers
## 5. Import / ingest     — bring data in; per-source command; the create→import sequence shown whole
## 6. Align / configure   — tag, index, configure; how data is made queryable
## 7. Operate             — the day-to-day verbs, grouped by task not alphabetically
## 8. Query / retrieve    — every read command; real query + real result
## 9. Maintain            — compact/optimise, sync, snapshot/export, clean

## — PRODUCTION —
## 10. Modes & tiers       — each mode, what it enables/forbids, how to choose (if applicable)
## 11. Security            — encryption, signing, secrets handling (if applicable)
## 12. Audit & compliance  — read/verify the audit trail; retention; legal hold (if applicable)
## 13. Access control      — roles/permissions (if applicable)
## 14. Backup & restore    — back up; restore; integrity verification
## 15. Deployment          — how it's shipped/run in production; service/daemon setup
## 16. Configuration       — every config key: name · default · location · effect (a table)

## — SAFETY NET —
## 17. Troubleshooting     — symptom → cause → fix table
## 18. Exit codes & errors — every exit code and its meaning
## 19. Glossary            — domain terms (reuse the project's ubiquitous language)
## 20. Quick reference     — the whole command surface on one screen
```

## Per-command entry shape

Every command documented uses the same shape (consistency lets a reader scan):

```
### <command>
<one-line purpose, in the user's task language>

Usage:    <product> <command> [flags] <args>
Flags:    --flag       <what it does; default>   ← every flag, from the real --help
Example:  <product> <command> --real-flag value
Result:   <the actual observable: stdout excerpt / exit code / state change>
Notes:    <gotchas, enterprise-mode interactions, when NOT to use it>
```

## Rules

- **One runnable example minimum per lifecycle stage**, copy-paste-and-it-works (verified in step 6).
- **Flags come from the real `--help`**, not memory — every flag, with its default.
- **Group by task, not alphabet** in Operate/Query; alphabetical lives only in §20 quick-reference.
- **Show the create→import→align→query path as one continuous sequence** somewhere early — the reader's
  first win.
