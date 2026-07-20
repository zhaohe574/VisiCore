#!/usr/bin/env bash
set -euo pipefail

base_sha="${1:?请提供 Pull Request 基线提交 SHA}"
while IFS= read -r commit; do
  if ! git log -1 --format=%B "$commit" | grep -qiE '^Signed-off-by: .+ <.+>$'; then
    echo "提交 $commit 缺少 DCO Signed-off-by。"
    exit 1
  fi
done < <(git rev-list "${base_sha}..HEAD")
