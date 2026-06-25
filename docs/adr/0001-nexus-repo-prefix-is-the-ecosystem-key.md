# ADR 0001 — The Nexus repo-name prefix is the ecosystem key (not the Nexus format)

**Status:** Accepted · **Date:** 2026-06-25

## Context

The PromotionBridge discovers `<eco>-quarantine` repos in Nexus dynamically (Nexus is the source of
truth; no hardcoded ecosystem list) and must map each discovered repo back to an `Ecosystem` so it can
scan the package against the correct OSV ecosystem. Three candidate keys exist:

1. **Repo-name prefix** — `cran-quarantine` → `cran`.
2. **Nexus format** — the repo's `format` field (`r`, `apt`, `maven2`, …).
3. **A side-stored mapping** recorded at provision time.

The decision is hard to reverse: it defines the repo-naming contract the entire firewall, the provision
API, the bootstrap seed, and the dashboard all depend on.

## Decision

**Key off the repo-name prefix, via an explicit `prefix↔Ecosystem` map** that is the single source of
truth shared by the provision API, the bridge, and the UI. The provision step enforces the
`<eco>-quarantine` / `<eco>-approved` naming so the prefix is always trustworthy. An unknown prefix is
**skipped with a logged warning** — never routed to a default ecosystem.

## Why not the Nexus format

Formats **collide**: Debian and Ubuntu are both Nexus format `apt`, but are different distros with
different upstreams and different OSV ecosystems (`Debian` vs `Ubuntu`). Keying off format cannot tell
them apart, so one would be scanned against the wrong CVE set — a silent, dangerous mis-route. The prefix
is unique per ecosystem and never collides.

This also removes a pre-existing bug: two stale maps (`EcoPrefix` and `MapEco`) each fell through to
`_ => "pypi"` / `_ => PyPI`, so any unrecognised ecosystem was silently scanned as PyPI. The explicit
prefix map with a skip-and-warn default replaces both.

## Consequences

- Provisioning **must** enforce the prefix convention; the name is the contract.
- Adding an ecosystem = creating correctly-named repos in Nexus (UI/API/seed) — no code change to the
  bridge, which simply discovers and maps by prefix.
- A repo created outside the convention (e.g. Nexus's default `maven-central`) does not match a
  `*-quarantine` prefix and is correctly ignored by the bridge.
- The `prefix↔Ecosystem` map is the one place to edit when a genuinely new ecosystem is added.
