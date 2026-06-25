# Advisory — Domain Glossary

The ubiquitous language of the platform. Implementation lives in code; this file is the glossary only.

## Gate

The decision engine (`IGateEngine`) that evaluates a package and returns **Allow**, **Block**, or
**Quarantine**. The sole ground truth for "is this package safe."

## Ecosystem

A package world Advisory understands (npm, PyPI, Maven, Hugging Face, …). The `Ecosystem` enum. An
ecosystem is gated by **one** mechanism (see *Gate mechanism*); the Catalog can *research* an ecosystem
even when no gate mechanism covers it.

## Gate mechanism

*How* a given ecosystem is scanned. Three kinds:

- **OSV-CVE** — CVE lookup via OSV.dev (the 14 package ecosystems OSV covers). These are the ones that
  flow through a **Nexus proxy** firewall.
- **Specialised scanner** — a dedicated scanner instead of OSV: Hugging Face (weights/pickle scanner),
  AI Editor Extensions (deep `.vsix` code scan via the vsix-scanner sidecar), Docker (image layer scan).
- **Research-only** — no CVE source exists, so the ecosystem can be *searched* in the Catalog but is
  **not gated** and is never shown as "protected" (currently: Conda — deferred).

## Catalog

The research surface — search any of the 17 ecosystems for packages, CVEs, and project health. Distinct
from the **gate**: the Catalog *informs*; the gate *enforces*. An ecosystem can be in the Catalog without
being gated.

## Quarantine repo

A Nexus **proxy** repository (`<eco>-quarantine`) pointing at the public upstream. Packages land here
first; developers cannot read it. The physical holding area for unvetted artifacts.

## Approved repo

A Nexus **hosted** repository (`<eco>-approved`) that developers pull from. A package only appears here
after the gate **Allowed** it.

## PromotionBridge

The interception loop. **Discovers** every `<eco>-quarantine` repo that exists in Nexus, polls each,
runs the gate per component, and **promotes** Allowed packages to the matching approved repo (Block/
Quarantine are held). Driven by Nexus's actual state — no hardcoded ecosystem list.

## Provision

The act of creating an ecosystem's `quarantine`+`approved` pair in Nexus (idempotent — "already exists"
is success). Done from the dashboard UI, by the provision API, or by the one-time fresh-install seed.
**Nexus is the source of truth; provisioning writes to Nexus and the bridge/UI read it back.**

## Repo prefix

The `<eco>` segment of a repo name (`cran-quarantine` → prefix `cran`). The **single key** that maps a
Nexus repo back to an `Ecosystem` (an explicit `prefix↔Ecosystem` map). Chosen over the Nexus *format*
because formats collide (Debian and Ubuntu are both `apt`); the prefix never does. An unknown prefix is
**skipped with a warning** — never silently routed to a default ecosystem.
