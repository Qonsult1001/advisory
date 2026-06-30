# Advisory — IT rollout bundle

A self-contained Docker deployment of the Advisory software-supply-chain firewall: the **web console**
your curation team uses, the **API/gate** that allows/blocks packages, the **Nexus repository** your
developers pull approved packages from, plus the SQL queue and on-prem scanners.

This folder is **isolated** — it has its own compose project, ports, volumes, and data directory. It
does not interfere with any development copy of Advisory on the same machine.

---

## 1. Prerequisites

- **Docker** with Compose v2 (Docker Desktop on Windows/Mac, or Docker Engine + compose plugin on
  Linux). Verify: `docker version` and `docker compose version`.
- ~6 GB free disk and ~4 GB RAM available to Docker (SQL Server + Nexus are the heavy parts).
- Outbound internet on first run (to build images and let Nexus proxy the public registries).

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

---

## 3. What's running & the ports

| Service | Default URL | Audience | Purpose |
|---------|-------------|----------|---------|
| **console** | `http://localhost:8088` | Curation / security team | Search, set policy, gate, approve, audit |
| **nexus** | `http://localhost:8081` | Developers | Pull approved packages; browse repos |
| **api** | `http://localhost:5000` | (internal) | Gate engine + promotion bridge + REST API |
| **mssql** | host `1434` → in-net `1433` | (internal) | Durable intake queue |
| **privacy-filter** | (internal `8071`) | (internal) | On-prem PII redaction |
| **vsix-scanner** | (internal `8099`) | (internal) | AI-editor extension code scan |

All ports are overridable in `.env`.

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
