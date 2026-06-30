# Podman deployment - verified notes

This records what was found running the rollout bundle against a real **Podman 4.6.2** install
(Ubuntu 22.04 in WSL2). Read this alongside `README.md` before deploying on Podman.

## What was verified

- **The bundle extracts and the install script runs on Podman.** `install.sh` correctly detects the
  container engine and the compose tool, and starts the image builds. (On this host only
  `podman-compose` 1.6.0 was present - the `podman compose` plugin form was not installed; the
  installer handled that via its `podman-compose` fallback.)
- **The image builds complete** (api, console, scanners) - the build phase finished with success.

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

- Build path on Podman: **verified working** (after the missing-deps fix).
- Full end-to-end run (all 6 containers healthy + a package gated) on Podman: **not completed in the
  WSL2 test environment** due to host memory pressure during the build+start spike. On a host with
  adequate RAM (8 GB+) this is expected to complete; verify with `podman-compose ps` and the console at
  `http://<host>:8088`. The same end-to-end flow is verified working on Docker.
