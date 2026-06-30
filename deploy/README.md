# Advisory — IT rollout bundle

A self-contained Docker deployment of the Advisory software-supply-chain firewall: the **web console**
your curation team uses, the **API/gate** that allows/blocks packages, the **Nexus repository** your
developers pull approved packages from, plus the SQL queue and on-prem scanners.

This folder is **isolated** — it has its own compose project, ports, volumes, and data directory. It
does not interfere with any development copy of Advisory on the same machine.

---

## 1. Prerequisites

- **A container engine — Docker _or_ Podman** (your choice), with Compose v2:
  - Docker Desktop (Win/Mac) or Docker Engine + compose plugin (Linux) — `docker compose version`, **or**
  - Podman with the compose plugin — `podman compose version`.
  - The installer auto-detects which you have (prefers Docker if both are present). Every command in
    this README works with either — substitute `podman` for `docker` if that's your engine.
- ~6 GB free disk and ~4 GB RAM available to the engine (SQL Server + Nexus are the heavy parts).
- Outbound internet (see the network-access list below) — for image pulls/builds, the package
  registries Nexus proxies, the threat-intelligence feeds, and the AI assistant.

---

## 1a. Network access (firewall / proxy allowlist)

The stack reaches **external services**. On a restricted network, open outbound **HTTPS (443)** to the
hosts below (and DNS). Everything is optional except the container-image hosts at install time — but a
host that's blocked simply disables that capability (e.g. block the Groq host and the AI assistant goes
silent; block a registry and you can't gate that ecosystem). If you run a corporate proxy, point Docker/
Podman and the containers at it (`HTTP_PROXY`/`HTTPS_PROXY`/`NO_PROXY`).

### A. Container images (needed at install/build time)
| Host | Why |
|------|-----|
| `registry-1.docker.io`, `auth.docker.io`, `production.cloudflare.docker.com` | Pull base images (Nexus, SQL Server, node, nginx, python) |
| `mcr.microsoft.com` | SQL Server image |
| `registry.npmjs.org` | Console build (npm install) |
| `*.ubuntu.com` / `dl-cdn.alpinelinux.org` / `deb.debian.org` | OS packages inside image builds |

> Air-gapped? Build the images on a connected host and `docker save` / `podman save` them, then load on
> the target — ask for the offline image set.

### B. Package registries Nexus proxies (per ecosystem you provision)
Only the ecosystems you turn on need their upstream open.
| Ecosystem | Upstream host |
|-----------|---------------|
| PyPI (Python) | `pypi.org`, `files.pythonhosted.org` |
| npm (Node) | `registry.npmjs.org` |
| NuGet (.NET) | `api.nuget.org`, `*.nuget.org` |
| Cargo (Rust) | `crates.io`, `static.crates.io` |
| Go | `proxy.golang.org` |
| Maven (Java) | `repo1.maven.org`, `search.maven.org` |
| RubyGems | `rubygems.org` |
| Composer (PHP) | `repo.packagist.org`, `packagist.org` |
| Conan (C/C++) | `center.conan.io` |
| CRAN (R) | `cran.r-project.org`, `crandb.r-pkg.org` |
| Dart/Pub | `pub.dev` |
| Alpine | `dl-cdn.alpinelinux.org`, `pkgs.alpinelinux.org` |
| Debian / Ubuntu (apt) | `sources.debian.org` |
| HuggingFace (models) | `huggingface.co` |
| Docker (images) | `registry-1.docker.io`, `auth.docker.io` |

### C. Threat-intelligence feeds (the free gate uses these)
| Feed | Host | Purpose |
|------|------|---------|
| OSV.dev | `api.osv.dev` | Multi-ecosystem CVE matching |
| CISA KEV | `www.cisa.gov` | Known-exploited gating |
| FIRST EPSS | `api.first.org` | Exploit-probability gating |
| OpenSSF Scorecard | `api.securityscorecards.dev` | Project-health scoring |
| deps.dev | `api.deps.dev` | Dependency-graph enrichment |
| GitHub API | `api.github.com`, `github.com` | Source/scorecard lookups, Conan index |
| npm downloads | `api.npmjs.org` | Download-count enrichment |
| VS Code / Open VSX | `marketplace.visualstudio.com`, `open-vsx.org` | Extension metadata |

### D. Premium intel (only if you set a key)
| Service | Host | Enable with |
|---------|------|-------------|
| VulnCheck (exploited-in-the-wild) | `api.vulncheck.com` | `VULNCHECK_API_KEY` |
| Socket (behavioural) | `api.socket.dev` | `SOCKET_API_KEY` |

### E. AI assistant (only if you set a key)
Powers "Ask AI", "Build rules with AI", and per-decision rationales. **The only AI channel enabled by
default is Groq.** Without an AI key, these features fall back to deterministic behaviour.
| Provider | Host | Enable with |
|----------|------|-------------|
| **Groq** (default) | `api.groq.com` | `GROQ_API_KEY` |
| OpenRouter (optional alt) | `openrouter.ai` | `OPENROUTER_API_KEY` |

> No package data, CVE text, or source is sent to the AI provider for normal gating — the AI is only
> called for the assistant chat and the human-readable decision rationale. To run with **no external AI
> at all**, leave both keys unset.

### F. Identity provider (only if you enable SSO)
| Use | Host |
|-----|------|
| Microsoft Entra ID | `login.microsoftonline.com`, `graph.microsoft.com` |
| (other OIDC) | your provider's authority URL |

### G. On-prem scanners (first run only, if you start them)
| Scanner | Host | Why |
|---------|------|-----|
| privacy-filter | `huggingface.co` | Downloads its PII model once (~1 GB), then runs fully offline |
| vsix-scanner | (none after build) | Self-contained once built |

> **Minimum to run the firewall with the free gate:** the container-image hosts (A), the registries for
> the ecosystems you provision (B), and the free feeds (C). D/E/F/G are all opt-in.

---

## 2. Install (one command)

1. **Copy the config template and edit it:**
   ```
   cp .env.example .env        # Windows: copy .env.example .env
   ```
   At minimum set **`SQL_SA_PASSWORD`** (8+ chars, upper+lower+digit). Change ports if any clash.

2. **Run the installer:**
   - Linux / macOS: `./install.sh`
   - Windows (PowerShell): `.\install.ps1`

   It builds the images from source (first run takes a few minutes), starts everything, waits for the
   API, and prints the URLs.

   > Or do it by hand: `docker compose up --build -d`

3. **Open the console** at `http://localhost:8088` (or your `CONSOLE_PORT`). On the splash, leave the
   SSO switch **off** and click **Continue** to verify the stack — then turn SSO on for production
   (§5).

> **Optional scanners.** The PII redaction (privacy-filter) and AI-editor extension scanner
> (vsix-scanner) are **off by default** to keep the rollout lean — the gate works fully without them.
> To include them, run the installer with the scanners flag:
> `./install.sh --scanners` (Linux/Mac) or `.\install.ps1 -Scanners` (Windows), or by hand
> `docker compose --profile scanners up --build -d` (substitute `podman` if that's your engine).
> Note: privacy-filter downloads a ~1 GB model on first run.

---

## 3. What's running & the ports

| Service | Default URL | Audience | Purpose | Container image (pulled/built) |
|---------|-------------|----------|---------|--------------------------------|
| **console** | `http://localhost:8088` | Curation / security team | Search, set policy, gate, approve, audit | **built** here — base `node:20-alpine` (build) + `nginx:alpine` (serve) |
| **nexus** | `http://localhost:8081` | Developers | Pull approved packages; browse repos | **pulled** — `sonatype/nexus3:3.93.1` |
| **api** | `http://localhost:5000` | (internal) | Gate engine + promotion bridge + REST API | **built** here — base `mcr.microsoft.com/dotnet/sdk:10.0` + `node:20-alpine` |
| **mssql** | host `1434` → in-net `1433` | (internal) | Durable intake queue | **pulled** — `mcr.microsoft.com/mssql/server:2022-latest` |
| **privacy-filter** | (internal `8071`) | (internal) | On-prem PII redaction (optional) | **built** here — base `node:20-slim` |
| **vsix-scanner** | (internal `8099`) | (internal) | AI-editor extension code scan (optional) | **built** here — base `node:22-slim` |

All ports are overridable in `.env`.

**What your engine downloads.** On `install`, Docker/Podman **pulls** the two ready-made images
(`sonatype/nexus3` from Docker Hub, `mcr.microsoft.com/mssql/server` from Microsoft) and **builds** the
other four locally from this bundle's source — which in turn pulls their base images
(`mcr.microsoft.com/dotnet/sdk`, `node`, `nginx`). All of these come from the container-image hosts in
[§1a.A](#1a-network-access-firewall--proxy-allowlist) (Docker Hub + Microsoft Container Registry). You
do **not** install anything on the host directly — everything runs inside containers; the only host
requirement is the Docker or Podman engine itself.

---

## 3a. On-prem scanners — capability & value

The bundle ships two self-hosted security scanners. They are **built during install and ready to use**,
included now so the capability is in place for upcoming deployments. Both run **entirely on-premise on
ordinary CPU** (no GPU), responding in a fraction of a second once warm.

### privacy-filter — PII redaction
Runs OpenAI's `openai/privacy-filter` model locally to detect and redact personal data — names, emails,
phone numbers, national IDs, payment-card numbers, IP addresses, account numbers — directly inside your
environment.

- **What it's for:** scanning artifact and text content for personal/sensitive data before it is stored,
  shared, or pulled — your own on-prem data-loss-prevention layer.
- **Value it adds:** enterprise-grade PII detection with **no cloud DLP service and no data leaving your
  network** — privacy-preserving by design, and free to run.
- **Requirements:** ~1.7 GB image, ~4 GB RAM while active, one-time model download (~1 GB, then fully
  offline), CPU only.

### vsix-scanner — AI-editor extension code scanning
Runs Trail of Bits' `vsix-audit` engine with the YARA-X rule engine to inspect the actual code of
VS Code / AI-editor extensions for malicious behaviour: data-exfiltration webhooks, credential/SSH/cookie
theft, code injection, obfuscation, and RAT / command-and-control patterns.

- **What it's for:** the enforcement behind the **AI-editor extension gate** — deciding whether an
  extension is safe to allow.
- **Value it adds:** real code-level malware detection on extensions (not reputation guesswork), so risky
  editor extensions are caught before they reach developers.
- **Requirements:** ~430 MB image, ~90 MB RAM while active, self-contained after build, CPU only.

> Both idle at **0 % CPU** until something is scanned, and are built into this bundle ready to enable
> (see §1a.G / the scanners flag). Together: ~2.2 GB on disk, ~4 GB RAM when in use — fully on-prem,
> no GPU, no per-scan cloud cost.

---

## 4. First steps after install

1. **Set the Nexus admin password.** On first boot Nexus generates an initial password inside the
   container:
   ```
   docker compose exec nexus cat /nexus-data/admin.password
   ```
   Log into `http://localhost:8081` as `admin` with that value, set a permanent password, then put it in
   `.env` as `NEXUS_PASS` and re-run the installer so the API uses it.

2. **Provision an ecosystem.** In the console: **Xray → Scans List → Repositories** → click **Add** on
   an ecosystem (e.g. PyPI). This creates its quarantine + approved repos.

3. **Walk the tutorial.** Hand your curation team
   [`docs/TUTORIAL-gate-your-first-package.md`](docs/TUTORIAL-gate-your-first-package.md) — it takes
   them from zero to a gated, approved package in ~10 minutes.

4. **Point developers at the approved repo.** They pull from `<NEXUS_URL>/repository/<eco>-approved/`
   instead of the public registry — see [`docs/HOW-TO-GUIDES.md`](docs/HOW-TO-GUIDES.md) §6.

---

## 5. Enabling SSO (production)

The bundle ships SSO **off** so you can smoke-test immediately. To require sign-in:

1. Register an app in **Microsoft Entra ID** (or your OIDC provider). Note the tenant id, client id, and
   API audience. Define three app roles: **Admin**, **Approver**, **Viewer**.
2. In `.env`, set:
   ```
   SSO_ENABLED=true
   AZURE_AD_TENANT_ID=<your-tenant-guid>
   AZURE_AD_CLIENT_ID=<api-app-client-id>
   AZURE_AD_AUDIENCE=api://<api-app-client-id>
   ```
3. Re-run the installer (it rebuilds the console with the SSO button and the API with token validation).

The splash now shows **Sign in with SSO** and redirects users to your IdP; roles map from the Entra app
roles to the console's Admin / Approver / Viewer permissions.

---

## 6. Operating

| Task | Command (run in this folder) |
|------|------------------------------|
| Status | `docker compose ps` |
| Logs (all / one) | `docker compose logs -f` / `docker compose logs -f api` |
| Stop | `docker compose down` |
| Stop **and wipe data** | `docker compose down -v` |
| Update to new source | `git pull` (in the repo) then `docker compose up --build -d` |
| Restart one service | `docker compose restart api` |

**Backup.** Persistent state lives in: the `nexus-data` and `mssql-data` Docker volumes, and the
`./data` folder in this directory (policy, audit ledgers, scans). Back up all three. The audit WORM
mirror (`./data/audit.worm.jsonl`) is the immutable evidence record — retain it per your policy.

**Reset test data.** The console's red **Reset test data** button clears the ledger/queue/repos for a
clean demo — it does not change policy or provisioned ecosystems.

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Installer says "set SQL_SA_PASSWORD" | `.env` missing/incomplete | Edit `.env`, set the password, re-run |
| API never answers / console blank | Nexus still booting (1–2 min first run) | Wait, then `docker compose logs -f api`; hard-refresh the console |
| Port already in use | Another service on 8088/8081/5000/1434 | Change the matching `*_PORT` in `.env`, re-run |
| Console loads old version after update | Browser cache | Hard-refresh (Ctrl+Shift+R) or incognito |
| Can't log into Nexus | Initial password not set | `docker compose exec nexus cat /nexus-data/admin.password` |
| Enqueue fails "ecosystem-not-provisioned" | That ecosystem isn't added | Console → Xray → Scans List → Repositories → Add |
| Package never promotes | Next 30-s gate cycle hasn't run, or it was blocked | Wait a cycle; check Quarantine for the held reason |
| AI features do nothing | No Groq key | Set `GROQ_API_KEY` in `.env`, re-run (free path works without it) |

---

## 8. What's in this bundle

```
deploy/
  docker-compose.yml     the whole stack (builds from ../.. source)
  .env.example           every setting, documented — copy to .env
  install.sh             one-command installer (Linux/macOS)
  install.ps1            one-command installer (Windows)
  README.md              this file
  docs/
    TUTORIAL-gate-your-first-package.md   zero → first approved package
    HOW-TO-GUIDES.md                      9 task guides (policy, feeds, gate, approve, pull, …)
    CONSOLE-USER-MANUAL.md                full reference for every visible screen + the repository
```

The application source it builds from lives one directory up (the Advisory repo).
