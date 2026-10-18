#!/usr/bin/env bash
# Provider 统一回归：数据驱动执行 ProviderRegression/ProviderCases.json（需要网络）。
#
# 用法:
#   scripts/provider-regression.sh
#   FILTER="FullyQualifiedName~ProviderRegressionSearchTests" scripts/provider-regression.sh
#   CONFIG=Debug scripts/provider-regression.sh
set -euo pipefail

cd "$(dirname "$0")/.."

CONFIG="${CONFIG:-Release}"
FILTER="${FILTER:-Category=Online&FullyQualifiedName~ProviderRegression}"
PROJECT="Jellyfin.Plugin.PhoenixAdult.Tests/Jellyfin.Plugin.PhoenixAdult.Tests.csproj"

echo ">> config=${CONFIG}"
echo ">> filter=${FILTER}"
exec dotnet test "${PROJECT}" -c "${CONFIG}" --nologo --filter "${FILTER}"
