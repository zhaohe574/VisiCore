# 视枢（VisiCore）

面向受控网络的视频资产、权限、ONVIF／RTSP 接入、实时会话、回放中继、PTZ、告警和审计平台。开源首版通过 Docker 运行，默认不包含任何厂商 SDK、设备凭据或预置设备。

## 能力边界

- 内置标准 ONVIF Profile S／G 与通用 RTSP 直连。
- 管理端提供资产、区域、账号、角色、设备插件、边缘节点、告警与审计管理。
- 核心容器内置 API、管理端、通知处理与流网关；MediaMTX 由部署方独立运行。
- `src/VisiCore.QtViewer` 是可选的 Windows 原生查看端源码，不包含在 Docker 安装中。
- 厂商专有能力通过独立、已签名的边缘插件扩展。核心 API 不下载、加载或执行上传的 DLL、脚本或容器镜像。

## 首次安装（Windows Docker Desktop）

视枢只启动一个 `visicore-core` 核心容器。PostgreSQL 与 MediaMTX 由部署方独立运行，首次安装时通过浏览器完成连接测试和系统初始化。以下步骤适用于 Windows Docker Desktop；Linux 部署使用相同的网络、配置和浏览器向导。

### 安装前确认

- 已安装 Docker Desktop，并启用 Docker Compose v2。
- 已准备 PostgreSQL 16+ 管理账号。该账号必须可以创建数据库；首次安装的目标数据库必须不存在。
- 已准备 MediaMTX `1.19.2`，且同机模式使用仓库提供的配置文件。不要直接使用镜像默认配置，默认配置没有启用视枢需要的 Control API。
- 首次安装仅支持全新数据库。不会迁移旧拆分容器、旧数据库或旧配置卷。

### 1. 启动视枢核心容器

在仓库根目录执行。该命令会创建 `visicore-network` 内部网络，并将管理页面的 HTTP 与 HTTPS 端口默认绑定到本机回环地址。

```powershell
Copy-Item .env.example .env
# 如需局域网初始化，编辑 .env 并设置 ADMIN_HTTP_BIND_ADDRESS；公网请使用 TLS 反向代理。
docker compose up -d --build
docker compose ps
```

看到 `visicore-core` 为 `healthy` 后继续。首次运行时核心处于引导态：`/healthz` 返回成功，`/readyz` 会在初始化完成前保持未就绪，这是预期行为。

```powershell
Invoke-WebRequest http://127.0.0.1:8080/healthz
```

如果 `.env` 中自定义了 `VISICORE_NETWORK`，请在以下命令中将 `visicore-network` 替换为该网络名称。

### 2. 准备 PostgreSQL

PostgreSQL 不需要向 Windows 宿主机发布 `5432` 端口。只要其容器和 `visicore-core` 同在 `visicore-network`，核心便可通过内部 DNS 访问它。

已有 PostgreSQL 容器时，将其接入网络并添加稳定别名：

```powershell
docker network connect --alias postgres visicore-network <PostgreSQL容器名>
```

新建 PostgreSQL 容器时，可在 Docker Desktop 创建，或使用下列示例。请将 `<安全密码>` 替换为你自行保管的强密码；不要把真实密码提交到仓库或写入 `.env`。

```powershell
docker run -d --name visicore-postgres --restart unless-stopped --network visicore-network --network-alias postgres -e POSTGRES_PASSWORD=<安全密码> -v visicore-postgres-data:/var/lib/postgresql/data postgres:18.4
```

安装页中填写 `postgres`、端口 `5432`。不要填写 `localhost` 或 `127.0.0.1`，它们在核心容器中指向核心自身。默认 Docker PostgreSQL 未启用 TLS，选择"不使用 TLS"；只有数据库明确启用了 TLS 时才选择其他 TLS 模式。

### 3. 准备 MediaMTX

同机 MediaMTX 同样不需要发布 Control API、RTSP 或 HLS 的宿主机端口。必须挂载 [MediaMTX 配置模板](deploy/linux/mediamtx.yml)，它会启用 `9997` Control API、`8888` HLS，以及视枢的内部鉴权回调。

```powershell
docker run -d --name visicore-mediamtx --restart unless-stopped --network visicore-network --network-alias mediamtx -v "${PWD}\deploy\linux\mediamtx.yml:/mediamtx.yml:ro" bluenviron/mediamtx:1.19.2
docker logs visicore-mediamtx
```

日志应包含以下监听信息：

```text
[API] started with listener on :9997
[HLS] started with listener on :8888
```

已有 MediaMTX 容器时，先将它接入网络：

```powershell
docker network connect --alias mediamtx visicore-network <MediaMTX容器名>
```

若该容器使用镜像默认配置，仍须以本仓库的 `mediamtx.yml` 重新创建或挂载配置后重启；仅加入网络不能启用 Control API。远程模式只能填写由 TLS 反向代理提供的 HTTPS Control API 与 HLS 地址，详见 [外置 MediaMTX 部署](docs/mediamtx-external.md)。

### 4. 在浏览器完成初始化

访问 <http://127.0.0.1:8080> 或 <http://127.0.0.1:8080/admin>。浏览器会自动显示"视枢初始化"向导：

1. **PostgreSQL**：填写主机 `postgres`、端口 `5432`、TLS 模式、管理账号和密码、目标数据库（默认 `visicore`），然后点击"测试 PostgreSQL"。测试仅验证连通性、TLS、建库权限和目标库不存在，不创建数据库、角色或表。
2. **MediaMTX**：同机模式保持默认的 `http://mediamtx:9997/` 与 `http://mediamtx:8888/`，点击"测试 MediaMTX"。测试只访问 Control API 与 HLS 地址，不会拉取设备流。
3. **系统设置**：填写公共访问地址和首个系统管理员。管理员普通账号可由数字、字母、下划线和连字符组成；也可使用邮箱。邮箱统一转为小写保存和登录，普通账号保持大小写敏感。

每一步都必须测试成功才能继续；修改已测试步骤中的任一参数会使该步骤及后续步骤的测试失效。最终点击"完成初始化"时，系统会重新执行全部校验，再创建数据库、运行初始迁移和创建首个管理员。运行期继续使用同一 PostgreSQL 管理账号，不会创建应用数据库账号。

浏览器不会保存管理员密码或 PostgreSQL 密码；它们也不会写入 `.env`、镜像或日志。首个管理员密码只以哈希形式保存到数据库。PostgreSQL 管理密码为运行期连接所必需，会随连接串写入 `visicore-config` Docker 命名卷中的配置文件，并设为 `0400`、仅核心进程可读；宿主机 Docker 管理员仍可访问该卷。初始化成功后核心容器会自动重启并进入登录页。

### 5. 验证安装

容器自动重启后，登录管理端并执行：

```powershell
docker compose ps
Invoke-WebRequest http://127.0.0.1:8080/healthz
Invoke-WebRequest http://127.0.0.1:8080/readyz
docker network inspect visicore-network
```

`healthz` 与 `readyz` 均应返回 `200`，网络检查中应能看到 `visicore-core`、PostgreSQL 和 MediaMTX 三个容器。

### 常见问题

| 现象 | 处理方式 |
| --- | --- |
| PostgreSQL 测试失败 | 确认主机为 `postgres`、端口为 `5432`、密码为容器创建时的 `POSTGRES_PASSWORD`，并确认目标数据库不存在。 |
| MediaMTX 提示无法连接 Control API 或 HLS | 确认容器已经加入 `visicore-network` 且别名为 `mediamtx`；查看日志是否出现 `[API] started with listener on :9997`。没有该日志时，需挂载本仓库的配置模板后重建 MediaMTX。 |
| 页面无法从局域网打开 | 在 `.env` 中设置 `ADMIN_HTTPS_BIND_ADDRESS=0.0.0.0`，准备 `deploy/core/tls/tls.crt` 与 `tls.key` 后设置 `VISICORE_HTTPS_ENABLED=true`，重新执行 `docker compose up -d`。HTTPS 默认端口为 `8443`；HTTP 会明文传输密码，仅可在受控网络使用。 |
| 重复点击完成初始化 | 已有配置、数据库或管理员时会拒绝覆盖。先确认现有部署状态，勿通过删除数据库绕过该保护。 |

中心镜像可在容器内以 `8443` 终止 TLS。启用前必须在 `deploy/core/tls/` 提供受信任 CA 的 `tls.crt` 和 `tls.key`，并设置 `VISICORE_HTTPS_ENABLED=true`；完整证书挂载和变量约定见 [TLS 目录说明](deploy/core/tls/README.md)。外部 TLS 反向代理仍必须保留原始 `Host` 并传递 `X-Forwarded-Proto: https`。不要将 PostgreSQL、MediaMTX Control API 或 HLS 端口直接暴露到公网。

## 升级

`v0.3.0` 的单核心部署只支持全新安装，不提供旧拆分容器、数据库、卷、浏览器会话或查看端本地设置的升级路径。后续发行版升级前请由 PostgreSQL 管理员备份数据库，并备份 `visicore-config` 与 `api-exports` 卷；完成备份后停止服务、拉取或构建新镜像并重新启动。已有安装再次提交初始化页面时会拒绝覆盖配置、数据库或管理员。

```powershell
docker compose down
docker compose pull
docker compose up -d
```

停止并保留核心卷：`docker compose down`。彻底删除核心卷：`docker compose down -v`。后者不会删除外置 PostgreSQL、MediaMTX 或边缘节点数据。

## 边缘与设备插件

边缘节点支持 Linux Docker 与原生 Windows Service 两种交付。先在管理端创建一次性注册码，再使用 Edge Agent 的受控 bootstrap 文件完成注册；设备凭据只以 `AgentEnvelope` 加密信封下发并在该节点内存中短暂解封。核心容器和流网关均不保存或解密设备明文凭据。受控升级必须通过独立 Host Agent 验签执行，业务 Agent 不挂载 Docker Socket、不运行 shell 命令。

正式 Docker 部署请使用 GitHub Release 的 Linux 包并从固定目录启动，具体步骤见[边缘节点部署](docs/edge-node-deployment.md)。本地开发可复制 `deploy/linux/edge-agent.env.example` 为 `deploy/linux/.env` 后执行以下命令；中心地址和注册码仍只能在本机配置页测试并确认：

```powershell
docker compose --env-file deploy/linux/.env -f deploy/linux/edge-agent.compose.yaml up -d --build
```

外部设备插件以独立仓库和 Compose 覆盖文件发布。管理员只能登记由部署侧信任公钥签名、镜像摘要和 SHA-256 固定的 manifest；插件容器不得挂载 Docker Socket，也不得获得 API 容器写权限。详细约定见 [设备插件说明](docs/设备插件.md)。

Docker、Windows MSI、Host Agent 信任根、升级回滚与故障恢复见[边缘节点部署](docs/edge-node-deployment.md)。MediaMTX、现场转流、厂商 SDK 和容量压测不包含在 Edge Agent 中，仍需独立部署和验收。

## 开发与质量

后端为 .NET 8，管理端为 React 与 TypeScript。常用检查：

```powershell
dotnet test .\tests\VisiCore.Api.Tests\VisiCore.Api.Tests.csproj --configuration Release
Push-Location .\src\VisiCore.Admin
npm ci
npm run typecheck
npm run build
Pop-Location
```

## 开源协作

- 许可证：[Apache-2.0](LICENSE)。
- 贡献要求：[CONTRIBUTING.md](CONTRIBUTING.md)，提交必须包含 DCO `Signed-off-by`。
- 漏洞请使用 GitHub Private Vulnerability Reporting，勿通过公开 Issue 披露。
- 第三方组件与许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
