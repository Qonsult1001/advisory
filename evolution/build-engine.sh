#!/bin/bash
# Build the EVOLVE engine (yoyo) binary that the C# bridge invokes.
# The engine source lives in the SAID-ECHO repo; we build it in place and symlink the binary here.
set -euo pipefail

ENGINE_SRC="${EVOLVE_SRC:-/g/development/SAID-ECHO/EVOLVE}"
HERE="$(cd "$(dirname "$0")" && pwd)"

if [ ! -d "$ENGINE_SRC" ]; then
  echo "EVOLVE engine source not found at $ENGINE_SRC"
  echo "Clone it:  git clone https://github.com/Qonsult1001/SAID-ECHO.git"
  echo "Then point EVOLVE_SRC at <clone>/EVOLVE"
  exit 1
fi

echo "Building yoyo (release) from $ENGINE_SRC ..."
( cd "$ENGINE_SRC" && cargo build --release )

BIN="$ENGINE_SRC/target/release/yoyo"
[ -f "$BIN.exe" ] && BIN="$BIN.exe"
if [ ! -f "$BIN" ]; then echo "build produced no binary at $BIN"; exit 1; fi

mkdir -p "$HERE/bin"
cp "$BIN" "$HERE/bin/" && echo "Copied engine binary → $HERE/bin/$(basename "$BIN")"
echo "Done. Set EVOLVE_BIN=$HERE/bin/$(basename "$BIN") for the API."
