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
RUN dotnet publish src/VisiCore.Api/VisiCore.Api.csproj --configuration Release --no-restore --output /app/api
RUN dotnet publish src/VisiCore.StreamGateway/VisiCore.StreamGateway.csproj --configuration Release --no-restore --output /app/gateway

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim
ARG APP_UID=1654
WORKDIR /app
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends bash dumb-init nginx wget \
    && sed -i 's|pid /run/nginx.pid;|pid /run/nginx/nginx.pid;|' /etc/nginx/nginx.conf \
    && rm -rf /var/lib/apt/lists/* \
    && if ! getent passwd "${APP_UID}" >/dev/null; then useradd --uid "${APP_UID}" --create-home --shell /usr/sbin/nologin visicore; fi \
    && mkdir -p /var/lib/visicore/config /var/lib/visicore/exports /var/cache/nginx /var/lib/nginx /var/log/nginx /run/nginx /run/visicore/tls /etc/nginx/snippets \
    && chown -R "${APP_UID}:${APP_UID}" /app /var/lib/visicore /var/cache/nginx /var/lib/nginx /var/log/nginx /run/nginx
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV VISICORE_RUNTIME_CONFIG=/var/lib/visicore/config/runtime.json
COPY --from=dotnet-build /app/api /app/api
COPY --from=dotnet-build /app/gateway /app/gateway
COPY --from=admin-build /src/admin/dist /app/wwwroot
COPY deploy/core/nginx.conf /etc/nginx/conf.d/default.conf
COPY deploy/core/nginx-server.conf /etc/nginx/snippets/visicore-server.conf
COPY deploy/core/entrypoint.sh /app/entrypoint.sh
RUN chmod 0755 /app/entrypoint.sh
USER ${APP_UID}
EXPOSE 8080 8443
ENTRYPOINT ["dumb-init", "--", "/app/entrypoint.sh"]
CMD ["run"]
