#!/usr/bin/env bash
# Generate Jellyfin plugin manifest entry and prepend it to the dist manifest.
# Replacement for scripts/manifest.py (bash + curl + jq, no Python runtime needed).
#
# Usage: scripts/manifest.sh Jellyfin.Plugin.PhoenixAdult@v2026.0822.1200.0.zip
set -euo pipefail

MANIFEST_URL="https://raw.githubusercontent.com/gythialy/Jellyfin.Plugin.PhoenixAdult/dist/manifest.json"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CSPROJ="${SCRIPT_DIR}/../Jellyfin.Plugin.PhoenixAdult/PhoenixAdult.csproj"

filename="${1:?Usage: manifest.sh <zip-file>}"

# Extract version: "Name@v1.2.3.zip" -> "1.2.3"
version="${filename#*@}"
version="${version#v}"
version="${version%.zip}"

if [[ -z "${version}" || "${version}" == "${filename}" ]]; then
    echo "error: filename '${filename}' does not contain '@version'" >&2
    exit 1
fi

# MD5 checksum of the zip file
if [[ ! -f "${filename}" ]]; then
    echo "error: zip file not found: ${filename}" >&2
    exit 1
fi
checksum="$(md5sum "${filename}" | cut -d' ' -f1)"

# targetAbi: Jellyfin.Controller/Jellyfin.Model PackageReference version + ".0"
# strips any prerelease/build suffix like packaging.Version.base_version does
jellyfin_version="$(grep -oE '<PackageReference Include="(Jellyfin\.Controller|Jellyfin\.Model)" Version="[0-9][^"]*"' "${CSPROJ}" \
    | head -1 \
    | grep -oE 'Version="[0-9][^"]*"' \
    | cut -d'"' -f2 \
    | cut -d'-' -f1)"

if [[ -z "${jellyfin_version}" ]]; then
    echo "error: Jellyfin version not found in ${CSPROJ}" >&2
    exit 1
fi
target_abi="${jellyfin_version}.0"

timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

source_url="https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/download/v${version}/Jellyfin.plugin.PhoenixAdult@v${version}.zip"

# Fetch existing manifest and prepend the new version entry to [0].versions
curl -fsSL "${MANIFEST_URL}" | jq --arg checksum "${checksum}" \
    --arg targetAbi "${target_abi}" \
    --arg sourceUrl "${source_url}" \
    --arg timestamp "${timestamp}" \
    --arg version "${version}" \
    '.[0].versions = ([{
        "checksum": $checksum,
        "changelog": "Auto Released by Actions",
        "targetAbi": $targetAbi,
        "sourceUrl": $sourceUrl,
        "timestamp": $timestamp,
        "version": $version
    }] + .[0].versions)' > manifest.json

echo "manifest.json updated: v${version}, targetAbi=${target_abi}, checksum=${checksum}"
