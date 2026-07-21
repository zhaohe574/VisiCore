#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "${1:-run}" != "run" ]]; then
    echo "未知命令：${1}" >&2
    exit 2
fi

app_uid="${APP_UID:-1654}"
config_directory="/var/lib/visicore/config"
backup_directory="${VISICORE_BACKUP_DIRECTORY:-/var/lib/visicore/backups}"
maintenance_directory="${VISICORE_MAINTENANCE_DIRECTORY:-/var/lib/visicore/maintenance}"
postgres_password_file="${VISICORE_EMBEDDED_POSTGRES_PASSWORD_FILE:-${config_directory}/internal-postgres-password}"
backup_key_file="${VISICORE_BACKUP_KEY_FILE:-${config_directory}/backup-recovery.key}"
restore_request_file="${maintenance_directory}/restore-request.json"
upgrade_restore_request_file="${maintenance_directory}/upgrade-restore-request.json"

mkdir -p "${PGDATA}" "${config_directory}" "${backup_directory}" "${maintenance_directory}"
chown -R postgres:postgres "${PGDATA}"
chown -R "${app_uid}:${app_uid}" "${config_directory}" "${backup_directory}" "${maintenance_directory}"
chmod 0700 "${config_directory}" "${backup_directory}" "${maintenance_directory}"

create_secret_file() {
    local target="$1"
    local byte_count="$2"
    if [[ ! -s "${target}" ]]; then
        umask 077
        od -An -N"${byte_count}" -tx1 /dev/urandom | tr -d ' \n' > "${target}"
        chown "${app_uid}:${app_uid}" "${target}"
        chmod 0400 "${target}"
    fi
}

create_secret_file "${postgres_password_file}" 48
create_secret_file "${backup_key_file}" 32

if [[ ! -f "${PGDATA}/PG_VERSION" ]]; then
    init_password_file="$(mktemp)"
    cp "${postgres_password_file}" "${init_password_file}"
    chown postgres:postgres "${init_password_file}"
    chmod 0600 "${init_password_file}"
    gosu postgres initdb --username=visicore --auth-host=scram-sha-256 --pwfile="${init_password_file}" -D "${PGDATA}"
    rm -f "${init_password_file}"
fi

configured=false
if [[ -r "${VISICORE_RUNTIME_CONFIG}" ]]; then
    # 初始化器以 0600 原子写入配置；运行期再收紧为只读，避免常驻进程意外覆盖安装密钥。
    chmod 0400 "${VISICORE_RUNTIME_CONFIG}"
    configured=true
fi

https_config=/run/nginx/visicore-https.conf
printf '%s\n' '# HTTPS 未启用。' > "${https_config}"
https_configuration="${VISICORE_HTTPS_CONFIGURATION:-/var/lib/visicore/config/https-configuration.json}"
https_enabled="${VISICORE_HTTPS_ENABLED:-false}"
if [[ -f "${https_configuration}" ]]; then
    if grep -Eq '"enabled"[[:space:]]*:[[:space:]]*true' "${https_configuration}"; then
        https_enabled=true
    elif grep -Eq '"enabled"[[:space:]]*:[[:space:]]*false' "${https_configuration}"; then
        https_enabled=false
    else
        echo "HTTPS 待应用配置文件无效。" >&2
        exit 2
    fi
fi

if [[ "${https_enabled}" == "true" ]]; then
    uploaded_tls_directory="${VISICORE_UPLOADED_TLS_DIRECTORY:-/var/lib/visicore/config/tls}"
    certificate_pointer="${uploaded_tls_directory}/certificate.json"
    if [[ -f "${certificate_pointer}" ]]; then
        certificate_version="$(sed -n 's/.*"version":"\([0-9a-f]\{32\}\)".*/\1/p' "${certificate_pointer}" | head -n 1)"
        if [[ -z "${certificate_version}" ]]; then
            echo "HTTPS 待应用证书索引无效。" >&2
            exit 2
        fi
        certificate_file="${uploaded_tls_directory}/${certificate_version}/tls.crt"
        private_key_file="${uploaded_tls_directory}/${certificate_version}/tls.key"
    else
        tls_directory=/run/visicore/tls/
        certificate_file="${VISICORE_TLS_CERTIFICATE_FILE:-${tls_directory}tls.crt}"
        private_key_file="${VISICORE_TLS_PRIVATE_KEY_FILE:-${tls_directory}tls.key}"
    fi
    for tls_file in "${certificate_file}" "${private_key_file}"; do
        if [[ ! -r "${tls_file}" ]]; then
            echo "HTTPS 证书或私钥不可读。" >&2
            exit 2
        fi
    done
    cat > "${https_config}" <<EOF
server {
    listen 8443 ssl;
    server_name _;
    ssl_certificate ${certificate_file};
    ssl_certificate_key ${private_key_file};
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_session_cache shared:VisiCoreTls:10m;
    ssl_session_timeout 1d;
    ssl_session_tickets off;
    add_header Strict-Transport-Security "max-age=31536000" always;
    include /etc/nginx/snippets/visicore-server.conf;
}
EOF
fi

children=()
workload_children=()
terminate() {
    for child in "${children[@]:-}"; do
        kill -TERM "${child}" 2>/dev/null || true
    done
}
terminate_workload() {
    for child in "${workload_children[@]:-}"; do
        kill -TERM "${child}" 2>/dev/null || true
    done
}
trap terminate INT TERM

gosu postgres postgres -D "${PGDATA}" -c listen_addresses=127.0.0.1 -c port=5432 &
postgres_pid="$!"
children+=("${postgres_pid}")
export PGPASSWORD="$(<"${postgres_password_file}")"
for _ in $(seq 1 60); do
    if pg_isready --host=127.0.0.1 --port=5432 --username=visicore --dbname=postgres >/dev/null 2>&1; then
        break
    fi
    sleep 1
done
if ! pg_isready --host=127.0.0.1 --port=5432 --username=visicore --dbname=postgres >/dev/null 2>&1; then
    echo "内置 PostgreSQL 未在预期时间内就绪。" >&2
    terminate
    exit 1
fi
unset PGPASSWORD

if [[ "${configured}" == "true" && "${VISICORE_APPLY_MIGRATIONS:-true}" == "true" ]]; then
    # 迁移始终在新镜像自身容器内执行，中心 Host Agent 只负责受限 Compose 切换与失败恢复。
    gosu visicore dotnet /app/api/VisiCore.Api.dll --migrate
fi

start_workload() {
    /usr/local/bin/mediamtx /etc/visicore/mediamtx.yml &
    mediamtx_pid="$!"
    children+=("${mediamtx_pid}")
    workload_children+=("${mediamtx_pid}")

    ASPNETCORE_URLS=http://127.0.0.1:8081 gosu visicore dotnet /app/api/VisiCore.Api.dll --webroot /app/wwwroot &
    api_pid="$!"
    children+=("${api_pid}")
    workload_children+=("${api_pid}")
    if [[ "${configured}" == "true" ]]; then
        ASPNETCORE_URLS=http://127.0.0.1:15095 gosu visicore dotnet /app/gateway/VisiCore.StreamGateway.dll &
        gateway_pid="$!"
        children+=("${gateway_pid}")
        workload_children+=("${gateway_pid}")
    fi
    nginx -g 'daemon off;' &
    nginx_pid="$!"
    children+=("${nginx_pid}")
    workload_children+=("${nginx_pid}")
}

restore_requested=false
trap 'restore_requested=true' USR1
restore_monitor() {
    while [[ ! -f "${restore_request_file}" && ! -f "${upgrade_restore_request_file}" ]]; do
        sleep 1
    done
    kill -USR1 "$$"
}

start_workload
restore_monitor &
restore_monitor_pid="$!"

set +e
while true; do
    wait -n "${children[@]}"
    status=$?
    if [[ "${restore_requested}" == "true" ]]; then
        set -e
        kill -TERM "${restore_monitor_pid}" 2>/dev/null || true
        terminate_workload
        for child in "${workload_children[@]}"; do
            wait "${child}" 2>/dev/null || true
        done
        active_restore_request="${restore_request_file}"
        generated_upgrade_restore_request=""
        if [[ ! -f "${active_restore_request}" && -f "${upgrade_restore_request_file}" ]]; then
            # Core Host Agent 只能引用当前保护备份，恢复密钥始终留在容器受限配置目录内。
            archive_path="$(sed -n 's/.*"archivePath"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "${upgrade_restore_request_file}" | head -n 1)"
            expected_prefix="${backup_directory}/items/"
            if [[ -z "${archive_path}" || "${archive_path}" != "${expected_prefix}"*.vcbackup || ! -f "${archive_path}" ]]; then
                echo "中心升级保护备份请求无效。" >&2
                rm -f "${upgrade_restore_request_file}"
                terminate
                exit 1
            fi
            generated_upgrade_restore_request="${maintenance_directory}/.upgrade-restore-${RANDOM}-${RANDOM}.json"
            umask 077
            printf '{"archivePath":"%s","recoveryKey":"%s","requestedBy":"core-host-agent"}\n' "${archive_path}" "$(<"${backup_key_file}")" > "${generated_upgrade_restore_request}"
            chown "${app_uid}:${app_uid}" "${generated_upgrade_restore_request}"
            chmod 0400 "${generated_upgrade_restore_request}"
            active_restore_request="${generated_upgrade_restore_request}"
        fi
        if ! dotnet /app/maintenance/VisiCore.Maintenance.dll restore \
            --request "${active_restore_request}" \
            --config-directory "${config_directory}" \
            --postgres-password-file "${postgres_password_file}" \
            --backup-key-file "${backup_key_file}"; then
            echo "平台备份恢复失败，已清除恢复请求；若数据库已开始替换，请使用自动保护点重新恢复。" >&2
            rm -f "${restore_request_file}"
            rm -f "${upgrade_restore_request_file}" "${generated_upgrade_restore_request}"
            terminate
            exit 1
        fi
        rm -f "${restore_request_file}"
        rm -f "${upgrade_restore_request_file}" "${generated_upgrade_restore_request}"
        terminate
        exit 75
    fi
    set -e
    kill -TERM "${restore_monitor_pid}" 2>/dev/null || true
    wait "${restore_monitor_pid}" 2>/dev/null || true
    terminate
    for child in "${children[@]}"; do
        wait "${child}" 2>/dev/null || true
    done
    exit "${status}"
done
