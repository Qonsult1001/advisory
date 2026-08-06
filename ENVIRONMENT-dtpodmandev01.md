# Environment-specific configuration — Direct Transact (`dtpodmandev01`)

**Audience:** Advisory product developers packaging future updates  
**Purpose:** Capture every environment-specific change applied during the `1733704` rollout so future updates can ship **parameterised / environment-aware** and do not require customer-side rework.  
**Host:** `dtPodmanDev01` (`172.19.3.5`)  
**DNS suffix:** `*.dtpodmandev01.directtransact.corp`  
**Runtime:** Podman + `podman compose` (not Docker Desktop)

---

## 1. What future updates must preserve

| Asset | Path / location | Rule |
|-------|-----------------|------|
| Install root | `/sandpit/advisory/advisory-rollout/` | Overlay files here; never relocate |
| Nginx Proxy Manager stack | `/sandpit/npm/` | **Do not touch** (certs, DB, proxy hosts) |
| Wildcard TLS | `/sandpit/npm/certs/` + NPM custom SSL `npm-1` | **Do not regenerate / overwrite** |
| Nexus data volume | compose volume for `nexus` | **Do not recreate / wipe** |
| MSSQL data volume | compose volume for `mssql` | **Do not recreate / wipe** |
| Console API base URL | `web/src/App.jsx` → `const API = "/api"` | Must remain same-origin `/api` behind HTTPS |
| Console nginx reverse proxy | `web/nginx.conf` → `/api/` and `/v1/` → `http://api:5000/...` | Must be copied into the console image |
| Package proxy listen port | host `8090` → API `PROXY_PORT` | Public HTTPS is via NPM hostname (below) |

Updates must rebuild **only** `api` + `console` with `--no-deps` so Nexus/MSSQL/NPM keep running.

---

## 2. Public hostnames (TLS terminated at Nginx Proxy Manager)

| Hostname | Upstream | Purpose |
|----------|----------|---------|
| `https://advisory.dtpodmandev01.directtransact.corp` | `http://172.19.3.5:8088` | Advisory console |
| `https://nexus.dtpodmandev01.directtransact.corp` | `http://172.19.3.5:8081` | Nexus UI / API |
| `https://npm.dtpodmandev01.directtransact.corp` | `http://172.19.3.5:81` | NPM admin UI |
| `https://pacman.dtpodmandev01.directtransact.corp` | `http://172.19.3.5:8090` | Multi-ecosystem package proxy |

Certificate: existing wildcard `*.dtpodmandev01.directtransact.corp` (NPM custom certificate).  
DNS A records all point at `172.19.3.5`.

**Product implication:** do not hard-code `http://localhost:5000` or `http://<host>:8088` in the console for API calls when the app is served behind a reverse proxy. Prefer a relative base (`/api`) or a build-time / runtime config value (e.g. `VITE_API_BASE`, `window.__ADVISORY_CONFIG__.apiBase`).

---

## 3. Host ports (this environment)

| Port | Service | Notes |
|------|---------|--------|
| `80` / `443` | Nginx Proxy Manager | Public HTTP/HTTPS |
| `81` | NPM admin | Also published as `npm.` hostname |
| `5000` | Advisory API | Internal + host publish |
| `8081` | Nexus | Internal + host publish |
| `8088` | Console | **Not** 8080 — 8080 was already taken on this host |
| `8090` | Package proxy (API) | Bound on API container; public via `pacman.` |
| `1434` | MSSQL host map | Container still listens on `1433` in-network |

**Product implication:** document ports as compose overrides (`CONSOLE_HOST_PORT`, `PROXY_PORT`, `MSSQL_HOST_PORT`) rather than assuming `8080` / `1433` on the host.

---

## 4. Console / API wiring (required for HTTPS)

### 4.1 `web/src/App.jsx`

Shipped update default:

```js
const API = "http://localhost:5000/api";
```

**This environment requires:**

```js
const API = "/api";
```

Browsers load the console from `https://advisory.…`; calling `localhost:5000` fails. Same-origin `/api` is proxied by console nginx to the API container.

### 4.2 `web/nginx.conf` (must exist and be baked into the image)

```nginx
server {
    listen 80;
    server_name localhost;
    root /usr/share/nginx/html;
    index index.html;

    location /api/ {
        proxy_pass http://api:5000/api/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /v1/ {
        proxy_pass http://api:5000/v1/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

### 4.3 `web/Dockerfile`

Must `COPY` the custom nginx config into the image, e.g.:

```dockerfile
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
```

**Post-recreate note:** after recreating the `api` container, reload console nginx (or recreate console) so upstream DNS/`api` IP is refreshed — otherwise the UI shows **API offline · sample data** (stale upstream IP → 502).

---

## 5. Package proxy (developer registry endpoints)

API listens on **`:8090`**. Developers use the HTTPS front door:

| Ecosystem | Client setting |
|-----------|----------------|
| **npm** | `https://pacman.dtpodmandev01.directtransact.corp/npm/` |
| **PyPI** | `https://pacman.dtpodmandev01.directtransact.corp/pypi/simple/` |
| **NuGet** | `https://pacman.dtpodmandev01.directtransact.corp/nuget/index/index.json` |

### Developer setup commands

```powershell
# npm
npm config set registry https://pacman.dtpodmandev01.directtransact.corp/npm/

# PyPI
pip config set global.index-url https://pacman.dtpodmandev01.directtransact.corp/pypi/simple/

# NuGet
dotnet nuget add source https://pacman.dtpodmandev01.directtransact.corp/nuget/index/index.json -n pacman
```

### npm registry path compatibility (product fix — keep in tree)

npm clients request `GET /npm/<package>` (and `/npm/@scope/name`).  
The generic handler is `/npm/index/<package>`. This environment required a back-compat route (same pattern as PyPI’s `/pypi/simple/{name}`):

```csharp
[HttpGet("/npm/{**rest}")]
public Task<IActionResult> NpmClient(string rest, CancellationToken ct)
```

**Please ship this in upstream** so customers do not re-patch after every update. Without it, `npm install` returns **E404** while `/npm/index/<pkg>` still works.

---

## 6. Compose / environment variables (environment-specific)

File: `docker-compose.yml` (install root / deploy). Values in use on this host:

| Variable | Value / guidance |
|----------|------------------|
| `PROXY_PORT` | `8090` |
| `NEXUS_URL` | `http://nexus:8081` (in-compose DNS name) |
| `NEXUS_QUARANTINE_SUFFIX` | `quarantine` |
| `NEXUS_APPROVED_SUFFIX` | `approved` |
| `NEXUS_AUTOPROVISION` | `false` (operators enable ecosystems in UI) |
| `NEXUS_USER` / `NEXUS_PASS` | **Must match the live Nexus admin password.** Default `admin`/`admin123` is **wrong** here — wrong Basic auth causes Nexus `401`/`429` and the console shows **No repositories indexed** / `provisioned=false` even though repos exist. Empty credentials = anonymous list (read-only). Set the real admin password to enable **Add / provision**. |
| `SQL_CONNECTION_STRING` | Must match the **actual** MSSQL `sa` password on the persistent volume (compose default may not match a volume created earlier). |
| `MSSQL_SA_PASSWORD` | Only applies on first volume init |
| Console host port | `8088:80` |
| API ports | `5000:5000`, `8090:8090` |
| Nexus port | `8081:8081` |
| MSSQL host port | `1434:1433` |

**Product implication:** ship a `.env.example` + documented override file (e.g. `.env.customer`) and teach `apply-update.sh` to **merge, not overwrite**, customer env values.

---

## 7. Nexus repositories already provisioned here

Do not wipe; future updates should discover dynamically:

- `pypi-quarantine` / `pypi-approved`
- `npm-quarantine` / `npm-approved`
- `nuget-quarantine` / `nuget-approved`

(plus default Nexus maven/nuget samples if present)

---

## 8. Update packaging gaps found during `1733704`

Include these in future update tarballs so the customer build does not fail:

| Item | Issue |
|------|--------|
| `SafeVersionRecommender.cs` | Referenced by update `Program.cs` but missing from tarball — stub had to be added under `src/Advisory.Api/Nexus/` |
| `LogTailer.cs` | Same as above |
| `web/src/App.jsx` API URL | Still shipped as `localhost:5000` — should be configurable |
| `web/nginx.conf` | Not in update; customer must keep it across overlays |
| npm `/npm/{**rest}` route | Missing in update; required for real npm clients |

---

## 9. Recommended update apply contract (this environment)

```text
1. Overlay changed files onto /sandpit/advisory/advisory-rollout/
2. Re-apply / preserve:
     - web/src/App.jsx → const API = "/api"
     - web/nginx.conf (and Dockerfile COPY)
     - docker-compose customer ports + env (do not reset NEXUS_*/SQL_*)
3. Do NOT touch /sandpit/npm/ or TLS material
4. Rebuild only: api + console
5. Recreate only: podman compose up -d --no-deps api console
6. Reload console nginx (or recreate console) after API IP change
7. Verify:
     - https://advisory.…/api/health → 200
     - https://advisory.…/api/scans/repositories → count > 0
     - https://pacman.…/npm/<pkg> → 200
     - https://pacman.…/pypi/simple/<pkg>/ → 200
     - https://pacman.…/nuget/index/index.json → 200
```

---

## 10. Suggested product changes (so this file shrinks over time)

Status as of update `b4b0a85` — most of these are now fixed **in the product**, so the customer no
longer re-patches:

1. ✅ **DONE** — Configurable console API base: now defaults to same-origin `/api` (overridable via
   `window.__ADVISORY_CONFIG__.apiBase`). `web/src/App.jsx`.
2. ✅ **DONE** — `web/nginx.conf` is shipped and `COPY`d into the console image by `web/Dockerfile`
   (proxies `/api/` and `/v1/` → `api:5000`).
3. ✅ **DONE** — npm client back-compat route `/npm/{**rest}` shipped in `PackageProxy.cs`
   (real `npm install` works; the generic `/npm/index|artifact/…` routes still work too).
4. ✅ **DONE** — `apply-update.sh` / `.ps1` now **preserve** `deploy/.env`, TLS/certs and the NPM stack,
   and never clobber a customised `docker-compose.yml` (writes `.from-update` alongside for you to merge).
   Ports/credentials in `.env` are untouched. `PROXY_PORT` is parameterised; `.env.example` documents it.
5. ✅ **DONE** — The applier reloads the console nginx after recreating `api`, so the UI doesn't sit on a
   stale upstream IP (`API offline · sample data`).
6. ✅ **DONE** — MANIFEST completeness: the delta now includes the build-referenced files that a customer
   on an older base might lack (`SafeVersionRecommender.cs`, `LogTailer.cs`), and packaging runs a
   build-against-the-base check so a delta that would fail to compile is caught before shipping.

### Feedback round 2 (update `091922b`)

7. ✅ **DONE** — **SBOM project dropped on re-pull.** `ScanStore.MergeAsset` omitted the `Project` field, so
   a second pull for the same asset cleared it and the row fell under `(unassigned)`. Fixed — `Project`
   survives re-pulls (`new.Project ?? old.Project`). Verified: re-pulling with no project header keeps the
   package under its project.
8. ✅ **DONE** — **Machine list showed gateway IPs, not real clients.** Behind Nginx Proxy Manager, the
   connection IP is the Podman gateway (`10.89.0.1`), so every developer collapsed to one address. The proxy
   now prefers the real client from `X-Forwarded-For` (which NPM sets) for the exposure IP + unattributed
   fallback. **Still requires** the `X-Advisory-Asset host=` header for corporate *hostnames* (reverse-DNS
   only resolves what your DNS can, and a rootless-Podman gateway IP has no useful record) — see Guide 6.
9. ⏳ **NEEDS CLIENT INPUT** — **SSO.** Not a bug: `SSO_ENABLED` is a console-splash flag; the API keys real
   auth off `AzureAd:ClientId`. Flipping the flag ALONE breaks login (console demands SSO, API can't
   validate). `.env.example` now documents the two-part dependency. **To enable, send: Entra tenant id,
   client (app) id, and API audience (Application ID URI).** Then set all four `AZURE_AD_*` + `SSO_ENABLED=true`
   and rebuild both api + console.

---

## 11. Quick reference — URLs for this environment

| Role | URL |
|------|-----|
| Console | https://advisory.dtpodmandev01.directtransact.corp |
| Nexus | https://nexus.dtpodmandev01.directtransact.corp |
| Package proxy | https://pacman.dtpodmandev01.directtransact.corp |
| NPM admin | https://npm.dtpodmandev01.directtransact.corp |
| Direct proxy (server-local) | http://172.19.3.5:8090 |

---

*Generated from the live `dtpodmandev01` sandpit after Advisory update `1733704` and package-proxy (pacman) cutover. Send this file with feedback to the Advisory developers so subsequent releases are environment-parameterised.*
