#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  echo "用法：$0 --release-tag <vX.Y.Z-rc.N> --core-image <Docker Hub digest> --edge-image <Docker Hub digest> --descriptor <file> --descriptor-signature <file> --public-key <file> --core-agent-root <dir> --edge-agent-root <dir> --output <dir>" >&2
  exit 64
}

release_tag=""
core_image=""
edge_image=""
descriptor=""
descriptor_signature=""
public_key=""
core_agent_root=""
edge_agent_root=""
output=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-tag) release_tag="$2"; shift 2 ;;
    --core-image) core_image="$2"; shift 2 ;;
    --edge-image) edge_image="$2"; shift 2 ;;
    --descriptor) descriptor="$2"; shift 2 ;;
    --descriptor-signature) descriptor_signature="$2"; shift 2 ;;
    --public-key) public_key="$2"; shift 2 ;;
    --core-agent-root) core_agent_root="$2"; shift 2 ;;
    --edge-agent-root) edge_agent_root="$2"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ "$release_tag" =~ ^v([0-9]+\.[0-9]+\.[0-9]+)-rc\.([1-9][0-9]*)$ ]] || usage
for value in "$descriptor" "$descriptor_signature" "$public_key"; do [[ -s "$value" ]] || { echo "缺少签名发行输入：$value" >&2; exit 65; }; done
[[ "$core_image" =~ ^visicore/visicore-core@sha256:[0-9a-f]{64}$ ]] || { echo "Core 必须使用 Docker Hub digest。" >&2; exit 65; }
[[ "$edge_image" =~ ^visicore/visicore-edge@sha256:[0-9a-f]{64}$ ]] || { echo "Edge 必须使用 Docker Hub digest。" >&2; exit 65; }

root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
version="${release_tag#v}"
key_id="$(jq -r '.signingPublicKeyId' "$descriptor")"
source_commit="$(jq -r '.sourceCommit' "$descriptor")"
[[ "$key_id" != null && "$source_commit" =~ ^[0-9a-f]{40}$ ]] || { echo "发行描述缺少签名公钥或来源提交。" >&2; exit 65; }
canonical_descriptor="$(mktemp)"
descriptor_signature_binary="$(mktemp)"
trap 'rm -f "$canonical_descriptor" "$descriptor_signature_binary"' EXIT
jq -cS -j . "$descriptor" > "$canonical_descriptor"
base64 --decode "$descriptor_signature" > "$descriptor_signature_binary"
openssl dgst -sha256 -verify "$public_key" -signature "$descriptor_signature_binary" -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:-1 "$canonical_descriptor" > /dev/null

mkdir -p "$output"

for runtime in linux-x64 linux-arm64; do
  case "$runtime" in linux-x64) architecture=amd64 ;; linux-arm64) architecture=arm64 ;; esac
  for component in core edge; do
    stage="$(mktemp -d)"
    trap 'rm -rf "$stage"; rm -f "$canonical_descriptor" "$descriptor_signature_binary"' EXIT
    package_root="$stage/visicore-${component}"
    mkdir -p "$package_root"/{release,host-agent,systemd}
    cp "$descriptor" "$package_root/release/release-descriptor.json"
    cp "$descriptor_signature" "$package_root/release/release-descriptor.signature.base64"
    cp "$public_key" "$package_root/release/release-public-key.pem"
    if [[ "$component" == core ]]; then
      install -m 0755 "$root/deploy/packages/install-core.sh" "$package_root/install.sh"
      install -m 0644 "$root/deploy/packages/core.compose.yaml" "$package_root/compose.yaml"
      install -m 0755 "$core_agent_root/$runtime/VisiCore.CoreHostAgent" "$package_root/host-agent/VisiCore.CoreHostAgent"
      install -m 0644 "$root/deploy/core/visicore-core-host-agent.service" "$package_root/systemd/visicore-core-host-agent.service"
      jq -n --arg release_tag "$release_tag" --arg source_commit "$source_commit" --arg architecture "$architecture" --arg key_id "$key_id" --arg image "$core_image" '{schemaVersion:1,component:"core",releaseTag:$release_tag,sourceCommit:$source_commit,architecture:$architecture,signingPublicKeyId:$key_id,coreImage:$image}' > "$package_root/package-manifest.json"
    else
      install -m 0755 "$root/deploy/packages/install-edge.sh" "$package_root/install.sh"
      install -m 0644 "$root/deploy/packages/edge.compose.yaml" "$package_root/edge-agent.compose.yaml"
      install -m 0755 "$edge_agent_root/$runtime/VisiCore.EdgeHostAgent" "$package_root/host-agent/VisiCore.EdgeHostAgent"
      install -m 0644 "$root/deploy/edge-host-agent/visicore-edge-host-agent.service" "$package_root/systemd/visicore-edge-host-agent.service"
      jq -n --arg release_tag "$release_tag" --arg source_commit "$source_commit" --arg architecture "$architecture" --arg key_id "$key_id" --arg image "$edge_image" '{schemaVersion:1,component:"edge",releaseTag:$release_tag,sourceCommit:$source_commit,architecture:$architecture,signingPublicKeyId:$key_id,edgeImage:$image}' > "$package_root/package-manifest.json"
    fi
    package_file="$output/visicore-${component}-${version}-linux-${architecture}.tar.gz"
    [[ ! -e "$package_file" ]] || { echo "部署包输出已存在：$package_file" >&2; exit 73; }
    tar -C "$stage" -czf "$package_file" "visicore-${component}"
    rm -rf "$stage"
    trap 'rm -f "$canonical_descriptor" "$descriptor_signature_binary"' EXIT
  done
done

for package in "$output"/*.tar.gz; do
  tar -tzf "$package" | grep -Eq '^visicore-(core|edge)/install\.sh$' || { echo "部署包结构无效：$package" >&2; exit 65; }
done
