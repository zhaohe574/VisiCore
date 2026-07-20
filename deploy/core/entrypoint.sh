#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "${1:-run}" != "run" ]]; then
    echo "未知命令：${1}" >&2
    exit 2
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
terminate() {
    for child in "${children[@]:-}"; do
        kill -TERM "${child}" 2>/dev/null || true
    done
}
trap terminate INT TERM

ASPNETCORE_URLS=http://127.0.0.1:8081 dotnet /app/api/VisiCore.Api.dll --webroot /app/wwwroot &
children+=("$!")
if [[ "${configured}" == "true" ]]; then
    ASPNETCORE_URLS=http://127.0.0.1:15095 dotnet /app/gateway/VisiCore.StreamGateway.dll &
    children+=("$!")
fi
nginx -g 'daemon off;' &
children+=("$!")

set +e
wait -n "${children[@]}"
status=$?
set -e
terminate
wait || true
exit "${status}"
