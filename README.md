# 视枢（VisiCore）

面向受控网络的视频资产、权限、ONVIF／RTSP 接入、实时会话、回放中继、PTZ、告警和审计平台。开源首版不包含厂商 SDK、设备凭据或预置设备。

## 部署边界

- Docker 安装只运行一个 `visicore-core` 容器，镜像内置 PostgreSQL `18.4`、MediaMTX `1.19.2`、API、StreamGateway、管理端和 Nginx。
- 宿主机仅发布管理端 HTTP `8080` 与 HTTPS `8443`。数据库、MediaMTX Control API、RTSP 和 HLS 均固定在容器回环地址，不能直接访问。
- `src/VisiCore.QtViewer` 是可选 Windows 原生查看端源码，不包含在 Docker 安装中。
- 设备厂商能力通过独立、受信任的边缘插件扩展；核心不下载、加载或执行上传的 DLL、脚本或容器镜像。

## 首次安装

### 1. 启动核心容器

安装 Docker Desktop 并启用 Docker Compose v2 后，在仓库根目录执行：

```powershell
Copy-Item .env.example .env
# 如需局域网初始化，编辑 .env 中的 ADMIN_HTTP_BIND_ADDRESS；公网使用 HTTPS。
docker compose up -d --build
docker compose ps
```

不需要创建 PostgreSQL 或 MediaMTX 容器，也不需要配置数据库密码。看到 `visicore-core` 为 `healthy` 后，访问 <http://127.0.0.1:8080/admin>。

### 2. 全新安装

初始化页选择“全新安装”，填写：

1. 平台公共访问地址。HTTPS 是默认要求；仅在受控局域网中确认风险后可使用 HTTP。
2. 首个系统管理员账号和密码。

提交后，核心会验证内置 PostgreSQL 与 MediaMTX、创建数据库、运行迁移并自动重启。页面会一次性显示恢复密钥；必须离线保存该密钥，它无法在后台再次查看。

### 3. 从备份恢复到新服务器

新服务器同样只需启动核心容器。在初始化页选择“恢复备份”，上传 `.vcbackup` 文件并输入原服务器的恢复密钥。恢复流程会：

- 校验归档格式、认证标签和完整性。
- 恢复业务数据库、运行配置和 TLS 证书。
- 为新容器重建内部 PostgreSQL 连接配置。
- 重启核心服务；使用备份中的管理员账号登录。

恢复密钥不包含在备份中。丢失密钥时，离线备份无法解密。

## 平台备份

系统管理员在“数据备份”页可立即创建、下载、上传、删除和恢复加密备份。

- 自动备份每天 `03:00`（Asia/Shanghai）运行，保留最近 30 份。
- 手动备份与上传备份由管理员手动删除。
- 备份包含 PostgreSQL 自定义格式转储、运行配置、HTTPS 配置和 TLS 文件；不包含录像导出文件。
- 恢复前会自动生成当前状态保护点；恢复会中断当前会话并使现有登录令牌失效。

备份、导出、配置和数据库数据分别由以下 Docker 命名卷持久化：

| 卷 | 内容 |
| --- | --- |
| `visicore_postgres-data` | 内置 PostgreSQL 数据目录 |
| `visicore_visicore-config` | 运行配置、TLS 与内部密钥 |
| `visicore_visicore-backups` | 加密平台备份 |
| `visicore_api-exports` | 录像导出文件 |

Compose 通过 `VISICORE_POSTGRES_VOLUME`、`VISICORE_CONFIG_VOLUME`、`VISICORE_BACKUPS_VOLUME` 和 `VISICORE_EXPORTS_VOLUME` 显式绑定这些名称，避免 Compose 项目名或部署目录变化时创建空卷。已有实例升级时必须保持这四个变量不变；只在部署独立的新实例时才修改它们。

`docker compose down` 不会删除这些卷。`docker compose down -v` 会永久删除全部平台数据和备份，执行前必须确认已下载并验证可恢复的备份。

## HTTPS

核心镜像可在容器内以 `8443` 终止 TLS。启用前可在初始化后进入“中心 HTTPS”页面上传 PEM 证书与私钥，保存公共访问地址后点击“应用并重启中心”。首次证书上传应通过运行 Docker 的本机回环地址访问；局域网 HTTP 不允许上传私钥。

完整变量说明见 [TLS 目录说明](deploy/core/tls/README.md)。外部 TLS 反向代理必须保留原始 `Host` 并传递 `X-Forwarded-Proto: https`。

## 升级

当前单容器方案只支持全新安装或从本平台加密备份恢复。此前依赖外置 PostgreSQL 或外置 MediaMTX 的部署不提供原地迁移；应保持旧部署，或另行完成数据迁移评估后再切换。

常规镜像升级：

```powershell
docker compose config --format json
$coreContainer = docker compose ps --quiet visicore-core
docker inspect $coreContainer --format '{{range .Mounts}}{{if eq .Type "volume"}}{{println .Destination .Name}}{{end}}{{end}}'
docker compose pull visicore-core
docker compose up -d --no-deps --force-recreate visicore-core
docker compose ps
```

升级前，确认四个挂载目标仍分别使用 `visicore_postgres-data`、`visicore_visicore-config`、`visicore_visicore-backups`、`visicore_api-exports`。配置不同、卷组不完整或未找到正在运行的核心容器时，停止升级并先修正部署配置。受控的 Core Host Agent 会自动执行同一项检查并拒绝不连续的升级。

升级前在后台创建并下载备份，离线保管恢复密钥。升级后检查：

```powershell
Invoke-WebRequest http://127.0.0.1:8080/healthz
Invoke-WebRequest http://127.0.0.1:8080/readyz
```

## 边缘与查看端

边缘节点支持 Linux Docker 与 Windows Service。使用一次性注册码完成配对；设备凭据只以 `AgentEnvelope` 加密信封下发并在节点内存中短暂解封。详细的服务权限、升级、回滚和恢复边界见 [边缘节点部署](docs/edge-node-deployment.md)。

Windows 查看端使用 `visicore-viewer-v<版本>.msi`。查看端只请求中心签发的视频会话，不保存摄像头地址、账号或密码。

## 版本边界

根目录 `VERSION` 只表示整套 GitHub Release 的发行批次。Core、Edge、Viewer 的版本分别位于 [`versions/core.txt`](versions/core.txt)、[`versions/edge.txt`](versions/edge.txt)、[`versions/viewer.txt`](versions/viewer.txt)，管理端版本位于 `src/VisiCore.Admin/package.json`。各端可以独立递增；构建、镜像标签和 Windows MSI 使用对应端的版本，不再要求与发行批次相同。

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
