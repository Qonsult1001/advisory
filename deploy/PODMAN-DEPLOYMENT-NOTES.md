# Podman deployment - verified notes

This records what was found running the rollout bundle against a real **Podman 4.6.2** install
(Ubuntu 22.04 in WSL2). Read this alongside `README.md` before deploying on Podman.

## What was verified

- **The bundle extracts and the install script runs on Podman.** `install.sh` correctly detects the
  container engine and the compose tool, and starts the image builds. (On this host only
  `podman-compose` 1.6.0 was present - the `podman compose` plugin form was not installed; the
  installer handled that via its `podman-compose` fallback.)
- **The image builds complete** (api, console, scanners) - the build phase finished with success.
- **The full stack comes up and serves.** After the fixes below, `./install.sh --podman` ran to
  `✓ Stack is up.` with all four core containers Up (nexus, mssql, api, console) and every endpoint
  answering **HTTP 200**: API health (`:5000/api/health`), console (`:8088`), Nexus (`:8081`).
- **The firewall gate works end-to-end.** `POST /api/gate/evaluate` for `npm/left-pad@1.3.0` returned a
  real decision (`Allow`) with the OSV source actually queried (`status:Empty, 366ms`), the OpenSSF
  malware feed run, and honest coverage/gaps reported - i.e. a genuine live evaluation, not a stub. The
  catalog DB is populated (`/api/catalog/ecosystems` lists PyPI, npm, NuGet, Cargo, Go, Maven, RubyGems,
  HuggingFace... as `live`).

## Issues found and fixed

### 1. Missing build dependencies (FIXED)

The first install failed at the API image build:

```
COPY Advisory.said /app/Advisory.said
target api: failed to compute cache key: "/Advisory.said": not found
```

Cause: the API Dockerfile copies several files that are **gitignored** (the `said` binaries, the
`Advisory.said` brain file, `RESEARCH.md`) plus `tools/reachability/`. The packaging step used
`git archive`, which only exports tracked files, so those were missing from the bundle.

Fix (already applied): the packaging step now also copies the gitignored build dependencies and
`tools/reachability/`. The bundle grew from ~6 MB to ~34 MB to include them. After the fix the API
image builds. **If you ever repackage, use the current `package.sh`.**

### 2. Short image names not resolvable (FIXED)

The build then failed on the console/scanner images with:

```text
Error: creating build container: short-name resolution enforced but cannot prompt without a TTY
```

Cause: unlike Docker, Podman **refuses to guess the registry** for a short image name (`node:20-alpine`,
`nginx:alpine`, `sonatype/nexus3:...`). Interactively it prompts; from a non-interactive installer it
can't, so the build aborts and no containers start. Only the already-fully-qualified
`mcr.microsoft.com/...` images built.

Fix (already applied): every base image is now **fully qualified to `docker.io/...`** in the
Dockerfiles (`docker.io/library/node:20-alpine`, `docker.io/library/nginx:alpine`,
`docker.io/library/node:20-slim`, `docker.io/library/node:22-slim`) and in the compose
(`docker.io/sonatype/nexus3:3.93.1`). This is portable - Docker treats the qualified name identically,
so the same images work on both engines. (Alternatively an admin can add
`unqualified-search-registries = ["docker.io"]` to `/etc/containers/registries.conf`, but qualifying
the images needs no host change and is what the bundle ships.)

### 3. Benign warnings you can ignore

On rootless Podman in WSL2 you may see, and can ignore:

- `HEALTHCHECK is not supported for OCI image format and will be ignored` - Podman builds OCI images;
  the container healthchecks are cosmetic-only there, the stack still runs.
- `failed to move the rootless netns slirp4netns process to the systemd user.slice` - a rootless-WSL
  cgroup message; networking still works (every endpoint returned 200 with it present).

## Requirements confirmed for Podman

- **Podman 4.x+** (4.6.2 verified). Podman 3.4.x is too old.
- A compose tool: either `podman compose` (plugin) **or** `podman-compose` (standalone). The installer
  uses whichever is present.
- **Memory headroom is critical.** SQL Server (~2 GB) + Nexus (~1 GB) + the build itself will saturate
  a small host. On a memory-constrained WSL2 / VM, building all images while starting heavy containers
  can make the engine unresponsive. Give the engine **at least 8 GB RAM** (more is better), and prefer
  to let the build finish before the heavy containers are under load.

## Recommended Podman install sequence

```bash
# 1. extract
tar -xzf advisory-rollout-20260630.tar.gz
cd advisory-rollout/deploy

# 2. (optional but safer on a small host) build first, then start - avoids build + run memory spike
podman-compose build           # or: podman compose build
podman-compose up -d           # or: podman compose up -d

#    ...or just run the installer, which does build-then-up for you:
./install.sh
```

## On WSL2 specifically

If you deploy in WSL2 (rather than a native Linux host), set a generous memory limit in
`C:\Users\<you>\.wslconfig`:

```
[wsl2]
memory=12GB
processors=4
```

then `wsl --shutdown` once to apply. Native Linux hosts (where IT will actually deploy) do not have
this WSL memory-partition constraint.

## Status

- Build path on Podman: **verified working** (after the missing-deps + short-name fixes).
- Full end-to-end run on Podman: **verified working.** `./install.sh --podman` (Podman 4.6.2,
  `podman-compose` 1.6.0, Ubuntu 22.04 in WSL2) reached `✓ Stack is up.` with the four core containers
  (nexus, mssql, api, console) Up, all endpoints returning 200, and a live package gate
  (`npm/left-pad@1.3.0 → Allow`, OSV queried). Scanners are built and left stopped; start them with
  `--scanners`.
- Verify on your host with `podman-compose ps` and the console at `http://<host>:8088`. The same
  end-to-end flow is also verified on Docker.
