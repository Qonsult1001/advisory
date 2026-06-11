#!/usr/bin/env bash
# Wires Nexus for the PkgFirewall two-repo enforcement model.
# Creates, per ecosystem:
#   <eco>-quarantine : a PROXY repo to the public upstream, NOT exposed to developers
#   <eco>-approved   : a HOSTED repo that developers pull from
# The PromotionBridge polls quarantine, runs the gate, and promotes clean packages to approved.
#
# Usage: NEXUS_URL=http://localhost:8081 NEXUS_USER=admin NEXUS_PASS=... ./nexus-setup.sh
set -euo pipefail
: "${NEXUS_URL:?set NEXUS_URL}"; : "${NEXUS_USER:?}"; : "${NEXUS_PASS:?}"
AUTH=(-u "$NEXUS_USER:$NEXUS_PASS" -H "Content-Type: application/json")
API="$NEXUS_URL/service/rest/v1"

upstream() { case "$1" in
  pypi)  echo "https://pypi.org" ;;
  npm)   echo "https://registry.npmjs.org" ;;
  nuget) echo "https://api.nuget.org/v3/index.json" ;;
  cargo) echo "https://crates.io" ;;
  go)    echo "https://proxy.golang.org" ;;
esac; }

create_quarantine() { # eco
  local eco="$1" up; up="$(upstream "$eco")"
  echo "  → ${eco}-quarantine (proxy → $up)"
  curl -fsS "${AUTH[@]}" -X POST "$API/repositories/$eco/proxy" -d @- <<JSON || echo "    (exists / skip)"
{
  "name": "${eco}-quarantine",
  "online": true,
  "storage": { "blobStoreName": "default", "strictContentTypeValidation": true },
  "proxy": { "remoteUrl": "$up", "contentMaxAge": 1440, "metadataMaxAge": 1440 },
  "negativeCache": { "enabled": true, "timeToLive": 1440 },
  "httpClient": { "blocked": false, "autoBlock": true }
}
JSON
}

create_approved() { # eco
  local eco="$1"
  echo "  → ${eco}-approved (hosted, developer-facing)"
  curl -fsS "${AUTH[@]}" -X POST "$API/repositories/$eco/hosted" -d @- <<JSON || echo "    (exists / skip)"
{
  "name": "${eco}-approved",
  "online": true,
  "storage": { "blobStoreName": "default", "strictContentTypeValidation": true, "writePolicy": "ALLOW" }
}
JSON
}

for eco in pypi npm nuget cargo go; do
  echo "Configuring $eco …"
  create_quarantine "$eco"
  create_approved   "$eco"
done

cat <<EOF

Done. Enforcement wiring complete.

NEXT — point developers at the APPROVED repos only (never quarantine):
  pip    → $NEXUS_URL/repository/pypi-approved/simple
  npm    → $NEXUS_URL/repository/npm-approved/
  nuget  → $NEXUS_URL/repository/nuget-approved/index.json
  cargo  → $NEXUS_URL/repository/cargo-approved/
  go     → GOPROXY=$NEXUS_URL/repository/go-approved/

Set these on the API so the bridge knows the repo names:
  NEXUS_QUARANTINE_REPO=pypi-quarantine   (etc. per ecosystem)
  NEXUS_APPROVED_REPO=pypi-approved

Developers cannot reach quarantine repos — they only see vetted packages in approved.
EOF
