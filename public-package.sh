#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# Pack & publish Airflow.MSDI.AutoRegister to NuGet.org
# Usage:
#   ./public-package.sh                 # pack current csproj version, then push it
#   ./public-package.sh --no-pack       # skip packing; push latest nupkg in artifacts/
#   ./public-package.sh path/to/x.nupkg # push a specific nupkg (no packing)
# Prerequisites:
#   - NUGET_API_KEY environment variable must be set (see README/release docs)
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CSPROJ="src/Airflow.MSDI.AutoRegister/Airflow.MSDI.AutoRegister.csproj"
NUPKG="${1:-}"

# ---- 1. Resolve the nupkg to push ----
if [[ -n "$NUPKG" && "$NUPKG" != "--no-pack" ]]; then
  # 显式指定了文件:直接用,不重新打包
  :
elif [[ "$NUPKG" == "--no-pack" ]]; then
  # 跳过打包,取 artifacts/ 里最新的 nupkg
  NUPKG="$(ls -1t artifacts/*.nupkg 2>/dev/null | head -n 1 || true)"
else
  # 默认:读取 csproj 里的当前版本号,重新打包并推送该版本(避免推到旧包)
  VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$CSPROJ" | head -n 1)"
  if [[ -z "$VERSION" ]]; then
    echo "Error: cannot read <Version> from $CSPROJ" >&2
    exit 1
  fi
  echo "Packing current version ${VERSION} (Release)..."
  dotnet pack "$CSPROJ" -c Release -o artifacts
  NUPKG="artifacts/Airflow.MSDI.AutoRegister.${VERSION}.nupkg"
fi

# ---- 2. Validate ----
if [[ -z "$NUPKG" ]]; then
  echo "Error: no .nupkg found to push." >&2
  echo "Hint: run ./public-package.sh (it packs first), or: dotnet pack -c Release -o artifacts" >&2
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

# ---- 3. Push ----
echo "Pushing: $NUPKG"
dotnet nuget push "$NUPKG" \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY"

PACKAGE_ID="$(basename "$NUPKG" .nupkg)"
echo ""
echo "✅ Published. Package page: https://www.nuget.org/packages/${PACKAGE_ID%.[0-9]*}"
