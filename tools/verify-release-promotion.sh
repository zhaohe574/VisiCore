#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 3 ]]; then
  echo "用法：$0 <公开 RC 资产目录> <内部治理资产目录> <候选 Git 标签>" >&2
  exit 64
fi

asset_dir="$1"
governance_dir="$2"
candidate_tag="$3"
if [[ ! "$candidate_tag" =~ ^v([0-9]+\.[0-9]+\.[0-9]+)-rc\.([1-9][0-9]*)$ ]]; then
  echo "候选标签格式无效：$candidate_tag" >&2
  exit 64
fi

release_id="${candidate_tag#v}"
expected_assets=(
  checksums.txt
  "visicore-core-${release_id}-linux-amd64.tar.gz"
  "visicore-core-${release_id}-linux-arm64.tar.gz"
  "visicore-edge-${release_id}-linux-amd64.tar.gz"
  "visicore-edge-${release_id}-linux-arm64.tar.gz"
  "visicore-edge-${release_id}-windows-amd64.msi"
  "visicore-viewer-${release_id}-windows-amd64.msi"
)

mapfile -t actual_assets < <(find "$asset_dir" -maxdepth 1 -type f -printf '%f\n' | sort)
mapfile -t sorted_expected < <(printf '%s\n' "${expected_assets[@]}" | sort)
[[ "$(printf '%s\n' "${actual_assets[@]}")" == "$(printf '%s\n' "${sorted_expected[@]}")" ]] || {
  echo "GitHub Release Assets 不符合白名单。" >&2
  printf '实际：%s\n' "${actual_assets[*]}" >&2
  exit 65
}

checksum_body="$(mktemp)"
signature="$(mktemp)"
canonical_descriptor="$(mktemp)"
canonical_fault="$(mktemp)"
trap 'rm -f "$checksum_body" "$signature" "$canonical_descriptor" "$canonical_fault"' EXIT
grep -E '^[0-9a-f]{64}  ' "$asset_dir/checksums.txt" > "$checksum_body"
[[ "$(wc -l < "$checksum_body" | tr -d ' ')" == 6 ]] || { echo "checksums.txt 缺少六个下载制品摘要。" >&2; exit 65; }
(cd "$asset_dir" && sha256sum --check "$checksum_body")

required=(release-descriptor.json release-descriptor.signature.base64 staging-fault-descriptor.json staging-fault-descriptor.signature.base64 release-sha256.txt release-sha256.signature.base64 release-evidence.json visicore-edge-release-public-key.pem)
for file in "${required[@]}"; do
  test -s "$governance_dir/$file" || { echo "缺少内部发行证据：$file" >&2; exit 65; }
done

checksum_signature="$(sed -n 's/^# rsaPssSha256: //p' "$asset_dir/checksums.txt")"
[[ -n "$checksum_signature" ]] || { echo "checksums.txt 缺少内嵌 RSA-PSS 签名。" >&2; exit 65; }
printf '%s' "$checksum_signature" | base64 --decode > "$signature"
openssl dgst -sha256 -verify "$governance_dir/visicore-edge-release-public-key.pem" -signature "$signature" -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:-1 "$checksum_body" > /dev/null

pushd "$governance_dir" > /dev/null
sed 's#  artifacts/release/#  #' release-sha256.txt > release-sha256.local.txt
sha256sum --check release-sha256.local.txt
jq -cS -j . release-descriptor.json > "$canonical_descriptor"
cmp --silent release-descriptor.json "$canonical_descriptor"
base64 --decode release-descriptor.signature.base64 > "$signature"
openssl dgst -sha256 -verify visicore-edge-release-public-key.pem -signature "$signature" -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:-1 release-descriptor.json
jq -cS -j . staging-fault-descriptor.json > "$canonical_fault"
cmp --silent staging-fault-descriptor.json "$canonical_fault"
base64 --decode staging-fault-descriptor.signature.base64 > "$signature"
openssl dgst -sha256 -verify visicore-edge-release-public-key.pem -signature "$signature" -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:-1 staging-fault-descriptor.json
jq -e --arg candidate_tag "$candidate_tag" --arg fault_version "${BASH_REMATCH[1]}-staging.1" '
  .releaseId == ($candidate_tag + "-staging-fault") and .productVersion == $fault_version and .channel == "rc" and
  (.artifacts[] | select(.component == "core") | .artifactReference | test("@sha256:0{64}$"))
' staging-fault-descriptor.json > /dev/null
jq -e --arg candidate_tag "$candidate_tag" '
  .releaseId == $candidate_tag and .channel == "rc" and (.sourceCommit | test("^[0-9a-f]{40}$")) and .rollbackStrategy == "backup-restore" and .databaseMigrationMode == "automatic-backup"
' release-descriptor.json > /dev/null
jq -e --arg candidate_tag "$candidate_tag" '
  .releaseId == $candidate_tag and .channel == "rc" and (.artifacts.core.digest | test("^[0-9a-f]{64}$")) and (.artifacts.edge.digest | test("^[0-9a-f]{64}$")) and
  (.artifacts.linuxPackages.coreAmd64.sha256 | test("^[0-9a-f]{64}$")) and (.artifacts.linuxPackages.edgeArm64.sha256 | test("^[0-9a-f]{64}$"))
' release-evidence.json > /dev/null
rm -f release-sha256.local.txt
popd > /dev/null

echo "候选发行已通过公开资产白名单、摘要、内部 RSA-PSS 签名和提升元数据验证：$candidate_tag"
