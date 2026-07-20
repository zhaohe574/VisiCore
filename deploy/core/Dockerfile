ARG NODE_IMAGE=node:22-alpine
FROM ${NODE_IMAGE} AS admin-build
WORKDIR /src/admin
COPY src/VisiCore.Admin/package.json src/VisiCore.Admin/package-lock.json ./
RUN npm ci
COPY src/VisiCore.Admin ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS dotnet-build
WORKDIR /src
COPY src ./src
RUN dotnet restore src/VisiCore.Api/VisiCore.Api.csproj
RUN dotnet restore src/VisiCore.StreamGateway/VisiCore.StreamGateway.csproj
RUN dotnet restore src/VisiCore.Maintenance/VisiCore.Maintenance.csproj
RUN dotnet publish src/VisiCore.Api/VisiCore.Api.csproj --configuration Release --no-restore --output /app/api
RUN dotnet publish src/VisiCore.StreamGateway/VisiCore.StreamGateway.csproj --configuration Release --no-restore --output /app/gateway
RUN dotnet publish src/VisiCore.Maintenance/VisiCore.Maintenance.csproj --configuration Release --no-restore --output /app/maintenance

FROM bluenviron/mediamtx:1.19.2 AS mediamtx-runtime

FROM postgres:18.4-bookworm
ARG APP_UID=1654
WORKDIR /app
USER root
RUN set -eux; \
    attempt=1; \
    until apt-get update; do \
      if [ "${attempt}" -ge 3 ]; then exit 1; fi; \
      attempt=$((attempt + 1)); \
      rm -rf /var/lib/apt/lists/*; \
      sleep 5; \
    done; \
    apt-get install --yes --no-install-recommends bash dumb-init libicu72 nginx wget \
    && sed -i 's|pid /run/nginx.pid;|pid /run/nginx/nginx.pid;|' /etc/nginx/nginx.conf \
    && rm -rf /var/lib/apt/lists/* \
    && if ! getent passwd "${APP_UID}" >/dev/null; then useradd --uid "${APP_UID}" --create-home --shell /usr/sbin/nologin visicore; fi \
    && mkdir -p /var/lib/visicore/config /var/lib/visicore/exports /var/lib/visicore/backups /var/lib/visicore/maintenance /var/cache/nginx /var/lib/nginx /var/log/nginx /run/nginx /run/visicore/tls /etc/nginx/snippets /etc/visicore \
    && chown -R "${APP_UID}:${APP_UID}" /app /var/lib/visicore /var/cache/nginx /var/lib/nginx /var/log/nginx /run/nginx
COPY --from=mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
COPY --from=mediamtx-runtime /mediamtx /usr/local/bin/mediamtx
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV PGDATA=/var/lib/postgresql/data
ENV VISICORE_RUNTIME_CONFIG=/var/lib/visicore/config/runtime.json
ENV VISICORE_EMBEDDED_POSTGRES_PASSWORD_FILE=/var/lib/visicore/config/internal-postgres-password
ENV VISICORE_BACKUP_KEY_FILE=/var/lib/visicore/config/backup-recovery.key
ENV VISICORE_BACKUP_DIRECTORY=/var/lib/visicore/backups
ENV VISICORE_MAINTENANCE_DIRECTORY=/var/lib/visicore/maintenance
COPY --from=dotnet-build /app/api /app/api
COPY --from=dotnet-build /app/gateway /app/gateway
COPY --from=dotnet-build /app/maintenance /app/maintenance
COPY --from=admin-build /src/admin/dist /app/wwwroot
COPY deploy/core/nginx.conf /etc/nginx/conf.d/default.conf
COPY deploy/core/nginx-server.conf /etc/nginx/snippets/visicore-server.conf
COPY deploy/core/mediamtx.yml /etc/visicore/mediamtx.yml
COPY deploy/core/entrypoint.sh /app/entrypoint.sh
RUN chmod 0755 /app/entrypoint.sh
EXPOSE 8080 8443
ENTRYPOINT ["dumb-init", "--", "/app/entrypoint.sh"]
CMD ["run"]
