# How Advisory actually intercepts packages — Nexus, the proxy, and the gate

**Plain-English answer to: "what does Nexus do for me, who do we connect to, is it free,
and how do my developers install packages?"** Grounded in this repo's real config.

---

## The one-sentence version

Your developers run a **normal `npm install` / `pip install`**, but pointed at **your
own Nexus** instead of the public internet. Nexus fetches the package from the real
public registry, **Advisory's gate scans it**, and only **clean** packages are handed
to the developer. Bad ones are held. The developer's command is unchanged — the
*registry URL* is what changes.

---

## 1. What is Nexus, and is it free?

**Nexus** (`sonatype/nexus3`) is a **self-hosted artifact repository** — it runs in
*your* `docker-compose.yml` (port 8081), on *your* infrastructure. It is **not** an
online service you depend on a third party for. The free Community edition is what's
used here (`image: sonatype/nexus3:latest`).

Think of Nexus as **your private, controllable mirror** of the public package world.

| | |
|---|---|
| Who hosts it | **You** (it's a container in your stack) |
| Cost | Free (Sonatype Nexus Community) |
| Your data | Stays in your `nexus-data` volume |

---

## 2. Do we always connect to "them" (the public registries)?

**Yes — but only Nexus does, never your developers directly.** Nexus has a
**proxy/quarantine** repo per ecosystem that points at the real upstream:

| Ecosystem | Public upstream Nexus proxies (from `scripts/nexus-setup.sh`) |
|---|---|
| npm | `https://registry.npmjs.org` |
| PyPI | `https://pypi.org` |
| NuGet | `https://api.nuget.org` |
| Cargo | `https://crates.io` |
| Go | `https://proxy.golang.org` |

So the "online repo" is the **real, free public registry** (npmjs.org etc.). You're
not paying anyone for packages — Nexus just **caches and gates** them. The first time
anyone asks for `express@4.22.2`, Nexus fetches it from npmjs.org and keeps a copy;
after that it serves the local copy (faster, and survives upstream outages).

---

## 3. The two-repo model (this is the key idea)

For **each** ecosystem, `nexus-setup.sh` creates **two** repositories:

```
            developer's normal `npm install`
                       │  (registry = npm-approved)
                       ▼
        ┌─────────────────────────┐
        │   npm-approved (HOSTED)  │  ← developers ONLY see this. Vetted packages.
        └──────────▲──────────────┘
                   │ PromoteAsync()  (only if the gate says Allow)
        ┌──────────┴──────────────┐
        │  PromotionBridge (gate)  │  ← scans every new package: OSV/KEV/malware/secrets
        └──────────▲──────────────┘
                   │ polls every N seconds
        ┌──────────┴──────────────┐
        │ npm-quarantine (PROXY)   │  ← fetches from registry.npmjs.org. NOT dev-facing.
        └──────────▲──────────────┘
                   │  (Nexus pulls from the real upstream on demand)
                   ▼
            https://registry.npmjs.org   (the free public registry)
```

- **`<eco>-quarantine`** — a **proxy** to the public registry. When something is
  requested here, Nexus downloads it from the real upstream. This is the *holding
  pen* — packages land here unvetted. **Developers are NOT pointed here.**
- **`<eco>-approved`** — a **hosted** repo. The only thing developers' `npm install`
  points at. A package only appears here **after the gate approved it.**

**The `PromotionBridge`** (`src/Advisory.Api/Nexus/PromotionBridge.cs`) is the
interception engine. Real flow from the code:
1. Polls the quarantine repo every N seconds.
2. For each new component, runs `gate.EvaluateAsync(pkg)` — the same gate that scans
   for CVEs (OSV), known-exploited (KEV), malicious packages (OpenSSF), and secrets.
3. **`Allow`** → `PromoteAsync()` copies it into the **approved** repo (devs can now
   get it).
4. **`Block` / `Quarantine`** → it stays held in quarantine + an audit record is
   written. Developers never see it.

---

## 4. "I thought I could just `npm install` anything and the proxy blocks/approves it"

You're **right** — that's exactly the intended developer experience, with one setup
step: **point the developer's tool at the approved repo once.** After that, every
`npm install` flows through the gate automatically.

**How a developer is set up (one-time, per machine or in CI):**

```bash
# npm
npm config set registry http://<nexus-host>:8081/repository/npm-approved/

# pip
pip config set global.index-url http://<nexus-host>:8081/repository/pypi-approved/simple/

# NuGet
nuget source Add -Name advisory -Source http://<nexus-host>:8081/repository/nuget-approved/index.json

# Go
export GOPROXY=http://<nexus-host>:8081/repository/go-approved/
```

Then they just work normally:
```bash
npm install express        # ← unchanged command; now gated
```

**What happens under the hood when they install a NEW package:**
1. npm asks `npm-approved` for `express`.
2. If it's already vetted → served instantly.
3. If it's new, the request triggers a fetch through quarantine → upstream → the gate
   scans it → if clean, it's promoted and served; if bad, the developer gets a
   "not found / blocked" and the security team sees it held in quarantine.

So: **developers use normal commands, the proxy/gate silently allows or blocks.** The
only thing that's different from "install from the public internet" is the **registry
URL** — and that single change is what gives you the security gate.

---

## 5. Where this shows up in the Advisory UI (all live data)

Once packages flow through, the Xray **Scans List** populates from the **real Nexus
contents** + the gate's scan results:
- **Repositories** tab → the real Nexus repos (npm-approved, npm-quarantine, …).
- **Packages** tab → every real package scanned.
- **Watch Violations / Overview** → the real CVEs the gate found.

No seed data — it's all the actual packages that passed through your Nexus.

---

## 6. Practical: how to populate live data for testing

To fill the Scans List with real packages, pull through the **quarantine** repos
(the proxies that fetch from upstream). The bridge then gates + promotes them:

```bash
npm install --registry http://localhost:8081/repository/npm-quarantine/ express lodash react axios
pip install --index-url http://localhost:8081/repository/pypi-quarantine/simple/ requests flask
```

> **Why quarantine, not approved, to *seed*?** `approved` is hosted and empty until
> the bridge promotes something. Pulling through `quarantine` is what makes Nexus
> fetch the real package from upstream so the gate has something to scan. In normal
> developer use you point at **approved** (the bridge has already filled it); to
> *prime* the system you pull through **quarantine**.

### Auth note
Nexus blocks anonymous access by default (you saw the `E401`). For local testing,
anonymous pull was enabled:
```bash
curl -u admin:admin123 -X PUT http://localhost:8081/service/rest/v1/security/anonymous \
  -H "Content-Type: application/json" \
  -d '{"enabled":true,"userId":"anonymous","realmName":"NexusAuthorizingRealm"}'
```
For production, leave anonymous **off** and give developers a Nexus token instead
(`npm config set //<host>:8081/repository/npm-approved/:_auth "<base64 user:pass>"`).

---

## 7. First-time setup (only if Nexus is empty)

Nexus in this repo is already wired (11 repos exist). If you ever start fresh:
```bash
NEXUS_URL=http://localhost:8081 NEXUS_USER=admin NEXUS_PASS=admin123 ./scripts/nexus-setup.sh
```
That creates the quarantine+approved pair for every ecosystem. It's idempotent
("exists / skip" for repos already there).

---

## TL;DR answers to your exact questions

- **What does Nexus do for me?** It's your private, self-hosted package mirror that
  every install flows through, so the gate can scan packages before developers get them.
- **Do I connect to Nexus?** Developers connect to **Nexus**; Nexus connects to the
  public registries. You never expose the public internet to developers directly.
- **Is it the online repo / is it free?** Nexus is self-hosted + free. The packages
  come from the free public registries (npmjs.org, pypi.org…) — you pay no one.
- **Do we always connect to them for packages?** Yes, Nexus proxies the public
  registries (and caches, so repeat installs are local and offline-safe).
- **How do developers install internally?** Normal `npm install` etc., with their
  registry pointed at `…/npm-approved/`. The gate silently allows or blocks.
- **"Just npm install anything and the proxy blocks/approves it"?** Exactly right —
  after the one-time registry change, that's precisely how it behaves.
