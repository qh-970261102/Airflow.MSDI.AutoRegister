#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# One-click publish of Airflow.MSDI.AutoRegister to NuGet.org
# Usage:
#   ./public-package.sh                 # push latest nupkg from artifacts/
#   ./public-package.sh path/to/x.nupkg # push a specific file
# Prerequisites:
#   - NUGET_API_KEY environment variable must be set (see README/release docs)
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Resolve the nupkg to push: prefer the argument, otherwise the latest in artifacts/
NUPKG="${1:-}"
if [[ -z "$NUPKG" ]]; then
  NUPKG="$(ls -1t artifacts/*.nupkg 2>/dev/null | head -n 1 || true)"
fi

if [[ -z "$NUPKG" ]]; then
  echo "Error: no .nupkg found. Run first: dotnet pack -c Release -o artifacts" >&2
  exit 1
fi

if [[ ! -f "$NUPKG" ]]; then
  echo "Error: file not found: $NUPKG" >&2
  exit 1
fi

if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "Error: NUGET_API_KEY environment variable is not set" >&2
  echo "Hint: run export NUGET_API_KEY='your-key' (consider adding it to ~/.bashrc)" >&2
  exit 1
fi

echo "Pushing: $NUPKG"
dotnet nuget push "$NUPKG" \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY"

PACKAGE_ID="$(basename "$NUPKG" .nupkg)"
echo ""
echo "✅ Published. Package page: https://www.nuget.org/packages/${PACKAGE_ID%.[0-9]*}"
