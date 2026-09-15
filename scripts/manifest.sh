#!/usr/bin/env bash
# Generate Jellyfin plugin manifest entry and prepend it to the dist manifest.
# Replacement for scripts/manifest.py (bash + curl + jq, no Python runtime needed).
#
# Usage: scripts/manifest.sh Jellyfin.Plugin.PhoenixAdult@v2026.0822.1200.0.zip
#
# TARGET_ABI overrides the derived targetAbi (the release workflow pins it to
# 12.0.0.0). Anything below MINIMUM_TARGET_ABI is rejected: this fork only
# supports Jellyfin 12+ (net10.0 / Jellyfin.Controller 12.x) since v2026.912.926.0.
set -euo pipefail

MANIFEST_URL="https://raw.githubusercontent.com/gythialy/Jellyfin.Plugin.PhoenixAdult/dist/manifest.json"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CSPROJ="${SCRIPT_DIR}/../Jellyfin.Plugin.PhoenixAdult/PhoenixAdult.csproj"
MINIMUM_TARGET_ABI="12.0.0.0"

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

# targetAbi: Jellyfin 12+ only. TARGET_ABI wins when set; otherwise derive it from the
# Jellyfin.Controller/Jellyfin.Model PackageReference version + ".0"
# (strips any prerelease/build suffix like packaging.Version.base_version does).
if [[ -n "${TARGET_ABI:-}" ]]; then
    target_abi="${TARGET_ABI}"
else
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
fi

# Refuse to publish a pre-12 entry again, even if the csproj is downgraded by mistake.
# Only NEW releases are affected: the pre-12 entries already in dist stay untouched.
if (( ${target_abi%%.*} < ${MINIMUM_TARGET_ABI%%.*} )); then
    echo "error: targetAbi ${target_abi} is below ${MINIMUM_TARGET_ABI}; new releases only support Jellyfin 12+ (legacy 10.x entries stay in dist)" >&2
    exit 1
fi

timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

source_url="https://github.com/gythialy/Jellyfin.Plugin.PhoenixAdult/releases/download/v${version}/Jellyfin.plugin.PhoenixAdult@v${version}.zip"

# Fetch the current dist manifest first: the new entry is prepended to it, and every version
# that was already published must survive unchanged. The pre-12 (Jellyfin 10.x) entries are no
# longer maintained but stay installable for users still on those hosts.
current_manifest="$(mktemp)"
new_manifest="$(mktemp)"
trap 'rm -f "${current_manifest}" "${new_manifest}"' EXIT

curl -fsSL "${MANIFEST_URL}" > "${current_manifest}"

# Prepend the new version entry to [0].versions
jq --arg checksum "${checksum}" \
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
    }] + .[0].versions)' "${current_manifest}" > "${new_manifest}"

# Guard: a release may only add a version, never drop or rewrite one.
lost="$(jq -nr --slurpfile prev "${current_manifest}" --slurpfile next "${new_manifest}" \
    '[$prev[0][0].versions[]] - [$next[0][0].versions[]] | map(.version) | join(", ")')"
if [[ -n "${lost}" ]]; then
    echo "error: manifest would drop or modify already published versions: ${lost}" >&2
    exit 1
fi

mv "${new_manifest}" manifest.json

echo "manifest.json updated: v${version}, targetAbi=${target_abi}, checksum=${checksum}"
echo "kept $(jq '.[0].versions | length' manifest.json) versions in dist (legacy pre-12 entries preserved)"
