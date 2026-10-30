#!/usr/bin/env bash
# 采集 madouqu.com 上实际存在的番号厂牌，输出 JSON 数组。
# 用途：更新 data/IdRoutes.json 中 madou 路由的 Labels（番号特别多，靠人工枚举无法覆盖）。
#
# 用法：
#   bash scripts/harvest-madou-labels.sh            # 输出 JSON 数组
#   bash scripts/harvest-madou-labels.sh --stats    # 附带每个厂牌的条目数
set -euo pipefail

UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36"
BASE="https://madouqu.com"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

curl -sL -A "$UA" --max-time 60 "$BASE/sitemap.xml" -o "$tmp/index.xml"

grep -o '<loc>[^<]*sitemap-posts[^<]*</loc>' "$tmp/index.xml" \
  | sed -e 's#<loc>##' -e 's#</loc>##' \
  | while read -r url; do
      curl -sL -A "$UA" --max-time 120 "$url" | gunzip -c 2>/dev/null || true
    done > "$tmp/posts.xml"

python3 - "$tmp/posts.xml" "$@" <<'PY'
import collections
import json
import re
import sys

path = sys.argv[1]
show_stats = "--stats" in sys.argv[2:]

text = open(path, encoding="utf-8", errors="replace").read()
slugs = [s.strip("/") for s in re.findall(r"<loc>https://madouqu\.com/video/([^<]+)</loc>", text)]

labels = collections.Counter()
for slug in slugs:
    # 番号厂牌 = 开头可选的数字 + 字母（mdsr0005 / mdx-0149 / 91cm-255）
    match = re.match(r"^(\d*[a-z]+)", slug)
    if match:
        labels[match.group(1)] += 1

if show_stats:
    json.dump([[k, v] for k, v in sorted(labels.items(), key=lambda kv: -kv[1])], sys.stdout, ensure_ascii=False, indent=2)
else:
    json.dump(sorted(labels), sys.stdout, ensure_ascii=False)
print()
print(f"# {len(slugs)} video urls -> {len(labels)} labels", file=sys.stderr)
PY
