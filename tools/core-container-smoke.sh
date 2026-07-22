#!/usr/bin/env bash
set -Eeuo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_name="visicore-core-smoke-${RANDOM}-${RANDOM}"
initial_image="visicore/visicore-core:${project_name}"
upgraded_image="${initial_image}-updated"
http_port="18080"
https_port="18443"

export VISICORE_CORE_IMAGE="${initial_image}"
export VISICORE_NETWORK="${project_name}-network"
export VISICORE_POSTGRES_VOLUME="${project_name}-postgres-data"
export VISICORE_CONFIG_VOLUME="${project_name}-config"
export VISICORE_EXPORTS_VOLUME="${project_name}-exports"
export VISICORE_BACKUPS_VOLUME="${project_name}-backups"
export ADMIN_HTTP_PORT="${http_port}"
export ADMIN_HTTPS_PORT="${https_port}"

cleanup() {
    docker compose --project-directory "${repository_root}" --project-name "${project_name}" down --volumes --remove-orphans >/dev/null 2>&1 || true
    docker image rm --force "${initial_image}" "${upgraded_image}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

assert_resolved_volume_names() {
    local configuration
    configuration="$(docker compose --project-directory "${repository_root}" --project-name "${project_name}" config --format json)"
    assert_resolved_volume_name "${configuration}" "postgres-data" "${VISICORE_POSTGRES_VOLUME}"
    assert_resolved_volume_name "${configuration}" "visicore-config" "${VISICORE_CONFIG_VOLUME}"
    assert_resolved_volume_name "${configuration}" "api-exports" "${VISICORE_EXPORTS_VOLUME}"
    assert_resolved_volume_name "${configuration}" "visicore-backups" "${VISICORE_BACKUPS_VOLUME}"
}

assert_resolved_volume_name() {
    local configuration="$1"
    local volume_key="$2"
    local expected_name="$3"
    awk -v key="\"${volume_key}\"" -v expected="\"name\": \"${expected_name}\"" '
        $0 ~ "^[[:space:]]*" key "[[:space:]]*:" { reading = 1; next }
        reading && index($0, expected) { found = 1; exit }
        reading && /^[[:space:]]*}/ { exit }
        END { exit(found ? 0 : 1) }
    ' <<<"${configuration}"
}

assert_container_volumes() {
    local container_id
    container_id="$(docker compose --project-directory "${repository_root}" --project-name "${project_name}" ps --quiet visicore-core)"
    [[ -n "${container_id}" ]]
    [[ "$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' "${container_id}")" == "${VISICORE_POSTGRES_VOLUME}" ]]
    [[ "$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/visicore/config"}}{{.Name}}{{end}}{{end}}' "${container_id}")" == "${VISICORE_CONFIG_VOLUME}" ]]
    [[ "$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/visicore/exports"}}{{.Name}}{{end}}{{end}}' "${container_id}")" == "${VISICORE_EXPORTS_VOLUME}" ]]
    [[ "$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/visicore/backups"}}{{.Name}}{{end}}{{end}}' "${container_id}")" == "${VISICORE_BACKUPS_VOLUME}" ]]
}

wait_for_health() {
    local attempt
    for attempt in $(seq 1 60); do
        if curl --fail --silent "http://127.0.0.1:${http_port}/healthz" >/dev/null; then
            return 0
        fi
        sleep 2
    done
    docker compose --project-directory "${repository_root}" --project-name "${project_name}" logs --no-color
    return 1
}

assert_resolved_volume_names
docker compose --project-directory "${repository_root}" --project-name "${project_name}" up --build --detach --force-recreate visicore-core
wait_for_health
assert_container_volumes

docker image tag "${initial_image}" "${upgraded_image}"
export VISICORE_CORE_IMAGE="${upgraded_image}"
docker compose --project-directory "${repository_root}" --project-name "${project_name}" up --detach --force-recreate visicore-core
wait_for_health
assert_container_volumes
