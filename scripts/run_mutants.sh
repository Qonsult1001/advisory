#!/usr/bin/env bash
# run_mutants.sh — mutation testing for PkgFirewall via Stryker.NET.
#
# Adapted from yoyo-evolve (MIT) which used cargo-mutants. Mutation testing checks whether the test
# suite actually CATCHES bugs: it mutates the code and verifies a test fails. A high "survival" rate
# means tests pass but don't really protect us — exactly the gaps the evolution agent should close.
#
# Usage:
#   ./scripts/run_mutants.sh                 # default: fail if mutation score < 60%
#   ./scripts/run_mutants.sh --threshold 70  # custom minimum score
#
# Exits 0 if mutation score >= threshold, 1 otherwise. Requires the dotnet-stryker tool
# (installed on demand below).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

THRESHOLD=60
[ "${1:-}" = "--threshold" ] && THRESHOLD="${2:-60}"

echo "→ Ensuring dotnet-stryker is available…"
if ! dotnet stryker --version >/dev/null 2>&1; then
  dotnet tool install -g dotnet-stryker >/dev/null 2>&1 || dotnet tool update -g dotnet-stryker >/dev/null 2>&1 || true
  export PATH="$PATH:$HOME/.dotnet/tools"
fi

echo "→ Running mutation tests (threshold: ${THRESHOLD}%)…"
# Stryker reads the test project; point it at the API under test.
( cd tests/PkgFirewall.Tests && \
  dotnet stryker --project ../../src/PkgFirewall.Api/PkgFirewall.Api.csproj \
    --threshold-break "$THRESHOLD" --reporter json --reporter cleartext 2>&1 ) | tee .evolve/mutants.log

# Stryker exits non-zero when score < threshold-break; surface that.
status=${PIPESTATUS[0]:-1}
if [ "$status" -eq 0 ]; then
  echo "✓ Mutation score meets the ${THRESHOLD}% threshold."
else
  echo "✗ Mutation score below ${THRESHOLD}% — there are tests that pass but don't catch bugs."
  echo "  These are high-value targets: add assertions/tests that kill the surviving mutants."
fi
exit "$status"
