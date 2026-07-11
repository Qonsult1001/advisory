# How-to guides — Advisory console

Each guide gets one real goal done. They assume you've signed in to the console (`<CONSOLE_URL>`) and
know the basics — if you're brand new, do *Tutorial — Gate your first package* first. Guides branch
where the real world forks ("if X, then…").

> `<CONSOLE_URL>` (curation console), `<PROXY_URL>` (the package firewall developers point pip/npm at,
> e.g. `:8090`), and `<NEXUS_URL>` (the internal repository) are the addresses your administrator
> configured for this install.

**Guides in this document**

1. [Set your firewall policy](#1-set-your-firewall-policy)
2. [Turn on intelligence feeds](#2-turn-on-intelligence-feeds)
3. [Gate a package end-to-end](#3-gate-a-package-end-to-end)
4. [Approve or revoke a package](#4-approve-or-revoke-a-package)
5. [Grant an exception for a blocked package](#5-grant-an-exception-for-a-blocked-package)
6. [Install packages through the firewall (developer, from your own PC)](#6-install-packages-through-the-firewall-developer-from-your-own-pc)
7. [Read the audit ledger](#7-read-the-audit-ledger)
8. [Export a report](#8-export-a-report)
9. [Provision a new ecosystem](#9-provision-a-new-ecosystem)

---

## 1. Set your firewall policy

**Goal:** decide what the firewall allows or blocks, and save it.
**Start:** signed in. **End:** a new signed policy version is live.

There are three ways to set policy — pick **one**.

### Option A — start from a profile (fastest)
1. Left nav → **Curation → Policy controls**.
2. Under **Choose a starting point**, click one card:
   - **Strict** — blocks aggressively (sensitive/regulated code).
   - **Recommended** ⭐ — best for most teams.
   - **Permissive** — lightest touch.
3. The card you pick shows **✓ ACTIVE**, and every control below updates.
4. Click **Commit & sign policy** (top-right).

**You should see:** the policy version in the top bar increments (e.g. *Policy v… → v…+1*) and the
SHA-256 changes. Your choice is now the live policy.

### Option B — describe it in plain English (AI)
1. Left nav → **Curation → Policy controls**.
2. In the **✦ Build rules with AI** box, type what you want, e.g.
   `block critical CVEs and known-exploited vulns, and block GPL licences`.
   *(Or click a **Try one** chip to fill it.)*
3. Click **Apply to controls**.

**You should see:** a green confirmation listing what changed (e.g. *"✓ Applied: Block CVSS ≥ 9 ·
Known-exploited → block on · Prohibited licence + GPL"*), each with a **see it ↓** link that jumps to
that control.

- **If it says "already covers this":** your current policy is already at least that strict — nothing
  to change. That's a success, not an error.
4. Click **Commit & sign policy**.

### Option C — step through each control (Guided setup)
1. Left nav → **Curation → Policy controls** → click **✦ Guided setup** (top-right).
2. For each step, read the question and the **⭐ Recommended** value (with its source). Click **Use
   recommended** or set your own, then **Next**.
3. At the last step click **Finish — review & commit**.
4. Click **Commit & sign policy**.

### Fine-tuning individual controls
On the Policy controls page, expand **Advanced settings** to see every control with a plain-English
explanation and a **⭐ Recommended** line. Adjust any (CVSS threshold, EPSS, minimum package age,
prohibited licences, etc.), then **Commit & sign policy**.

> **Nothing takes effect until you commit.** Until then, the firewall keeps running your previous
> signed policy.

---

## 2. Turn on intelligence feeds

**Goal:** make sure the gate has the threat data it needs.
**Start:** signed in. **End:** the right feeds are enabled and any gaps resolved.

1. Left nav → **Curation → Intelligence sources**.
2. Under **Choose which feeds to use**, click **Recommended** ⭐ (all free feeds: OSV, malicious
   packages, CISA KEV, EPSS, extension scanner).

**You should see:** the chosen feeds flash and their **Enabled** toggles turn on.

3. **Resolve any warnings** at the top of the page:
   - **"…that rule can't fire" (coupling warning):** a policy control needs a feed that's off. Click
     **Turn it on**.
   - **"needs key" warning:** a feed (e.g. Artifactory, Socket) is on but has no credential. Click
     **Add key** to enter one, or **Turn it off** if you don't use it.
4. Click **Commit & sign policy**.

**You should see:** the warnings clear and the feed table shows **● Ready** for each enabled feed.

> To describe it instead: use the **✦ Set up sources with AI** box (e.g. *"enable all free feeds and
> exploited-in-the-wild intelligence"*) → **Apply to feeds** → commit.

---

## 3. Gate a package end-to-end

**Goal:** run any package through the firewall and see the verdict.
**Start:** the package's ecosystem is provisioned (if not, see guide 9 first).
**End:** the package is Promoted or Held, with a recorded decision.

1. Left nav → **Catalog**.
2. Choose the **ecosystem** (e.g. npm) and search the package name (e.g. `lodash`). Click it.
3. On the overview, click **Send to Intake queue**.

**You should see:** *"✓ Sent to Intake queue."*

4. Left nav → **Pipeline → Quarantine**. Wait up to ~30 seconds (the gate cycle).

**You should see one of:**
- **Promoted** — allowed; it moved to the approved repo.
- **Held / Blocked** — stopped; the row shows the reason (triggered rules) and a violation count like
  `2C/0H`.
- **Promoting…** / **Pending** — mid-cycle; wait a few seconds and it resolves.

**Branches:**
- **If it never appears in Quarantine:** check **Pipeline → Intake queue**. If **Dead-lettered** went
  up, the package name/version probably doesn't exist upstream — re-check the spelling in the Catalog.
- **If enqueue failed with "ecosystem-not-provisioned":** provision that ecosystem first (guide 9).

5. (Optional) Left nav → **Pipeline → Decision ledger**, click the package's row to read **why**.

---

## 4. Approve or revoke a package

**Goal:** override the gate — either force a package in, or pull an approved one out.
**Start:** signed in.

### To manually promote a held package
1. Left nav → **Pipeline → Quarantine**.
2. Find the **Held / Blocked** package. Use its **Promote** action to push it to approved (operator
   override).

**You should see:** its status change to **Promoted**.

> Prefer a **time-bound exception** (guide 5) over a manual promote when you want the override recorded
> with a ticket and an expiry.

### To revoke an already-approved package
1. Left nav → **Pipeline → Approved packages**.
2. Find the package and click **Revoke**.
3. Confirm.

**You should see:** the package leaves the approved list; in **Quarantine** it now shows **Revoked**
(*"Approval revoked by operator — held, will not be re-promoted"*). Developers can no longer pull it.

---

## 5. Grant an exception for a blocked package

**Goal:** allow a specific package the gate would block, with an audit trail and an expiry.
**Start:** you know the package is being blocked and you have a ticket reference.
**End:** the exception is signed into policy and the package is allowed.

1. Left nav → **Pipeline → Approved exceptions**.
2. Fill the form:
   - **package** — as `name==version`, e.g. `pyyaml==5.3.1`.
   - **Ticket** — your reference, e.g. `SEC-1234`.
   - **Approver** — who approved it.
   - **Expiry** — pick a date with the date picker (leave blank for +90 days).
3. Click **Add exception**.

**You should see:** the exception appears in the list. On the next gate cycle the package is allowed
(it clears any prior revoke and gets promoted).

**Branches:**
- **If you get a date error:** the expiry must be a valid future date — use the date picker, don't type
  free text.
- **To remove an exception:** click its **Revoke** action. This deletes the exception **and** pulls the
  package back out of approved (a revoke wins over an exception).
- **Expired exceptions** are removed automatically (swept hourly) and the lapse is audited.

---

## 6. Install packages through the firewall (developer, from your own PC)

**Goal:** you're a developer on your laptop (call yourself *user ABC*). You keep running the **normal**
`pip install` / `npm install` you already use — nothing about your commands changes. A **one-time
registry setting** silently redirects them through the Advisory proxy, so every package you get is one
the firewall has vetted.
**Start:** your administrator has given you the firewall proxy address — call it `<PROXY_URL>` (for
example `http://advisory.mycompany.local:8090`). You have `pip` / `npm` installed.
**End:** plain `pip install <anything>` / `npm install <anything>` are transparently served through the
firewall — same commands, safe source, **works first try** (the firewall vets each package inline as you
pull it; you don't have to run the command twice).

> **The idea:** you don't type a new URL and you don't learn a new command. You set the redirect **once**
> (below), then use pip/npm exactly as before. In many organisations **IT sets this for you** (via a
> managed config or an environment variable) and you skip straight to *"Use it normally"*.

---

### Python — pip

**Set the redirect once:**
```
pip config set global.index-url <PROXY_URL>/pypi/simple/
```
*(If `<PROXY_URL>` is plain `http://`, also run:*
`pip config set global.trusted-host <host part of PROXY_URL, e.g. advisory.mycompany.local>`*)*

**Use it normally — same command you always run:**
```
pip install requests
```

**You should see:** `Successfully installed requests-…` on the **first** run. A package the firewall
hasn't vetted before is fetched, scanned, and served inline (a few extra seconds the first time); after
that it's instant. You didn't type any URL — pip picked up the redirect from its config and pulled
through the firewall.

> **Prefer not to run a command?** The same setting lives in a file pip reads
> (`%APPDATA%\pip\pip.ini` on Windows, `~/.config/pip/pip.conf` on macOS/Linux) under
> `[global]` → `index-url = …`. IT can push that file to every machine so nobody sets anything by hand.

---

### Node — npm

**Set the redirect once:**
```
npm config set registry <NEXUS_URL>/repository/npm-approved/
```

**Use it normally — same command you always run:**
```
npm install lodash
```

**You should see:** lodash installs as usual. Confirm the redirect is in place with
`npm config get registry` → it prints your `<NEXUS_URL>` address.

> **Per-project instead of machine-wide:** drop a `.npmrc` file in the project with one line —
> `registry=<NEXUS_URL>/repository/npm-approved/` — and only that project is redirected. IT can commit
> this to the repo so every clone is wired automatically.

---

### Other ecosystems (same one-time-setting idea)

Set it once, then use the tool normally:

| Ecosystem | One-time setting |
|-----------|------------------|
| **NuGet (.NET)** | `dotnet nuget add source <NEXUS_URL>/repository/nuget-approved/index.json -n advisory` (then remove nuget.org from the project's sources so restores use only the proxy) |
| **Cargo (Rust)** | in `~/.cargo/config.toml`, replace the crates-io source with `<NEXUS_URL>/repository/cargo-approved/` |
| **Go** | `go env -w GOPROXY=<NEXUS_URL>/repository/go-approved/` |

(Only ecosystems your administrator has **provisioned** — see guide 9 — will answer.)

---

### Verify it's really going through the firewall

Two quick proofs:

1. **A blocked package isn't installable.** Ask your security team for a package they know is **Held /
   Blocked** and `pip install` / `npm install` it by name — it should fail with *not found*, because
   blocked packages never reach the approved repo.
2. **Check the redirect is set:** `pip config get global.index-url` / `npm config get registry` — each
   should print your `<NEXUS_URL>` address.

**Branches:**
- **`pip install` / `npm install` fails with "not found" / 404:** the package hasn't been gated yet, or
  it was **blocked**. Ask your security team to run it through the Catalog (guide 3), or request an
  exception (guide 5). This is expected behaviour, not a broken setup.
- **Install still hits the public internet:** the redirect didn't take — re-run the `config set` command,
  then confirm with `pip config get global.index-url` / `npm config get registry`.
- **"Connection refused" / can't reach the proxy:** you're not on the network that can see the server,
  or the address is wrong. Confirm `<NEXUS_URL>` with your administrator and that it opens in a browser.

> You are always redirected to the **`-approved`** repo. There is nothing to pull from `-quarantine` —
> only vetted packages are downloadable. That's the whole point: your normal `pip`/`npm` commands can
> only ever get what passed the gate.

---

### For IT — enforce the redirect for everyone (global policy)

> **Read this if you're the administrator, not an individual developer.** The `config set` commands
> above are *per-user, per-machine* — they only protect a developer who runs them. To make the firewall
> **mandatory** (so nobody can accidentally or deliberately pull straight from the public internet), IT
> must **push the redirect centrally** and, ideally, **block direct registry access at the network**.
> Do both: config makes the tools use the proxy; the network makes it the *only* option.

**Principle:** deploy the same setting to every machine via your existing management channel (Group
Policy / Intune / Jamf / a provisioning script / a base container image) instead of asking each user.
Set it **per ecosystem** — one entry each for pip, npm, NuGet, Cargo, Go. The setting is the same one
from the sections above; you're just delivering it as a **managed file or environment variable**.

**Per-ecosystem managed setting (deploy these):**

| Ecosystem | What IT pushes (machine-wide) |
|-----------|-------------------------------|
| **pip** | A global `pip.conf` / `pip.ini` with `[global]` `index-url = <PROXY_URL>/pypi/simple/` (the firewall proxy — installs work **first try**). Machine-wide locations: Linux `/etc/pip.conf`, Windows `C:\ProgramData\pip\pip.ini`. (Or set the `PIP_INDEX_URL` environment variable for all users.) |
| **npm** | A global `.npmrc` with `registry=<NEXUS_URL>/repository/npm-approved/`. Machine-wide: the file at npm's global prefix `etc/npmrc`, or set the `NPM_CONFIG_REGISTRY` environment variable for all users. *(npm uses the repository directly for now; its first-try proxy is a fast-follow.)* |
| **NuGet** | A machine-wide `NuGet.Config` with **only** the `<NEXUS_URL>/repository/nuget-approved/index.json` source (remove `nuget.org`). Location: Windows `%ProgramFiles(x86)%\NuGet\Config\`, Linux `/etc/NuGet/NuGet.Config`. |
| **Cargo** | A shared `~/.cargo/config.toml` (or `$CARGO_HOME/config.toml` baked into the image) that replaces the crates-io source with `<NEXUS_URL>/repository/cargo-approved/`. |
| **Go** | The `GOPROXY` environment variable set for all users to `<NEXUS_URL>/repository/go-approved/`, plus `GONOSUMDB`/`GOFLAGS` as your policy needs. |

**Deliver it with the channel you already run:**
- **Windows (domain):** Group Policy *Preferences → Files* to drop `pip.ini` / `.npmrc` / `NuGet.Config`
  into the ProgramData locations, and *Environment* to set `PIP_INDEX_URL` / `NPM_CONFIG_REGISTRY` /
  `GOPROXY` for all users.
- **Windows/macOS (MDM — Intune / Jamf):** ship the same config files and environment variables as a
  configuration profile.
- **Linux / servers / CI:** put the files in the machine-wide locations above (or set the env vars in
  `/etc/environment`) via your config-management tool (Ansible/Chef/Puppet) or the base image.
- **Docker / CI images:** bake the config files (or `ENV PIP_INDEX_URL=…`, `ENV NPM_CONFIG_REGISTRY=…`)
  into your **base image** so every build and container is wired with zero developer action.

**Make it enforced, not just default (recommended):**
1. **Block the public registries at the network** for developer subnets — deny egress to
   `pypi.org`/`files.pythonhosted.org`, `registry.npmjs.org`, `api.nuget.org`, `crates.io`,
   `proxy.golang.org`, etc. — and **allow only `<NEXUS_URL>`**. Now the proxy is the *only* reachable
   source, so a user who removes their local config simply can't install anything from outside.
   (See RUNBOOK section 1a for the exact host list.) **Note:** block these on the *developer* subnets
   only — the *Advisory server* still needs to reach them, because the proxy is what fetches upstream.
2. **Prefer machine-wide config over per-user** so a normal user can't override it, and set the env-var
   form where you want it to win regardless of local files.

**Verify the fleet is enforced:**
- On a managed machine, `pip config get global.index-url` / `npm config get registry` print `<NEXUS_URL>`
  **without anyone having run a command** — that proves the push worked.
- From a developer subnet, a direct `pip install --index-url https://pypi.org/simple <pkg>` should
  **fail to connect** (network block), not fall through to the internet.

> Bottom line: **config = the tools use the proxy; network block = the proxy is the only option.** Ship
> both from IT and the firewall is enforced for every user, not just the ones who set it up themselves.

---

## 7. Read the audit ledger

**Goal:** see exactly what the firewall decided, when, and why — for review or an auditor.
**Start:** signed in. **End:** you can find and explain any decision.

1. Left nav → **Pipeline → Decision ledger**.

**You should see:** a table of every gate decision — **Component**, **Decision** (ALLOW / BLOCK),
**Tree** depth, **Coverage**, **Timestamp**. It refreshes live as new decisions happen.

2. **Click any row to expand it.** You get:
   - **Why this decision** — a plain-English rationale.
   - **Triggered controls** — which policy rules fired (e.g. `SEC-VULN-01:CVSS:…`).
   - **Source coverage** — each intelligence feed's status (Ok / Empty / Skipped) and findings, plus
     any gaps.

**Branches:**
- **If a row's rationale is empty:** it was recorded before the research agent was enabled (older
  entries stay as-is; new decisions get a written rationale).
- **If the ledger shows "No entries" right after a reset:** it refreshes within seconds as the gate
  runs again.

> The ledger is append-only and hash-chained, with an immutable WORM copy retained even through a
> reset — it's the firewall's tamper-evident audit trail.

---

## 8. Export a report

**Goal:** produce a shareable report (PDF / Word / CSV) of the firewall's findings.
**Start:** the firewall has gated some packages (so there's data).
**End:** a report file on your machine.

1. Left nav → **Pipeline → Reports**.
2. Click a report type:
   - **Vulnerabilities** — every vulnerability seen (severity, CVSS, EPSS, KEV, fix version).
   - **Violations** — Block/Quarantine decisions with triggered controls.
   - **Legal · Licenses** — declared licence, prohibited matches, unknowns.
   - **Operational Risk** — EOL, version age, project health.

**You should see:** an **executive summary** (stat cards + a distribution chart) above a detail table.

3. (Optional) **Click any table row** to expand its full detail; CVE ids link out to osv.dev.
4. Click the **↓ Export ▾** button (top-right of the report) and choose:
   - **PDF (print view)** — opens a print dialog; choose *Save as PDF*.
   - **Word (.doc)** — downloads a Word document.
   - **CSV (data)** — downloads the raw data.

**You should see:** the file is generated/downloaded in your chosen format.

**Branch:**
- **If "Save as PDF" doesn't appear:** allow pop-ups for the console, then click Export → PDF again.

---

## 9. Provision a new ecosystem

**Goal:** turn on the firewall for a package world it isn't gating yet (e.g. NuGet, Cargo, RubyGems).
**Start:** signed in. **End:** the ecosystem has quarantine + approved repos and can gate packages.

1. Left nav → **Xray → Scans List** → **Repositories** tab.
2. In the **Ecosystem firewall** panel, find the ecosystem's card (e.g. **NuGet**).
3. Click **Add** on that card.

**You should see:** after a few seconds the card shows it's provisioned, and the **Repositories** table
below gains `<eco>-quarantine` (proxy) and `<eco>-approved` (hosted) rows.

4. You can now gate packages in that ecosystem (guide 3).

**Branches:**
- **If the card says "Provisioning deferred":** that ecosystem (e.g. Debian/Ubuntu apt, Docker, Conda)
  needs extra setup and isn't available to self-provision yet.
- **To remove an ecosystem:** click **Remove** on its card and confirm. This deletes both its repos and
  their quarantine history — the firewall stops gating that ecosystem.

> Provisioning is driven from the console, not from the repository UI — keep all curation here.
