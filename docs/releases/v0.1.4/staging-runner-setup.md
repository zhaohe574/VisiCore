# v0.1.4 staging Runner 准备说明

本说明只适用于 `v0.1.4-rc.N` 候选演练。三台 Runner 与生产节点、生产数据、生产恢复密钥、生产管理员账号和生产 Edge 凭据完全隔离。

## Runner 与网络

| Runner 标签 | 主机职责 | 受控资源 |
| --- | --- | --- |
| `visicore-staging-linux-amd64` | amd64 Core Host Agent、Core、Linux Edge | `visicore-staging-core-amd64`、`visicore-staging-linux-amd64-edge` |
| `visicore-staging-linux-arm64` | arm64 Core Host Agent、Core、Linux Edge | `visicore-staging-core-arm64`、`visicore-staging-linux-arm64-edge` |
| `visicore-staging-windows-x64` | Windows Edge Host Agent、Edge、Viewer | `visicore-staging-windows-x64-edge` |

所有 Runner 必须通过现有私网或 VPN 访问内部 TLS DNS；防火墙仅允许该 DNS、Docker Hub 和 GitHub Release／Actions 所需的受信域名。任何主机名、Compose 项目、Docker 卷、工作目录和 Edge 名称均须保留 `visicore-staging` 前缀。不得把生产地址、生产卷或生产节点地址写进 Runner 配置。

## Linux Runner

每台 Linux Runner 必须具备以下固定本地条件：

- `/opt/visicore-staging/fixtures/v0.1.2.vcbackup`：只含 staging 数据的 v0.1.2 加密备份。
- `/opt/visicore-staging/core/compose.yaml`：Core Host Agent 使用的固定 Compose 文件，四个持久卷名都以 `visicore-staging-` 开头。
- `/opt/visicore-staging/core/v0.1.2.env`：`VISICORE_CORE_IMAGE` 必须是 `visicore/visicore-core@sha256:...`，不可使用标签或 `latest`。
- 正在运行的 `visicore-core-host-agent.service`、相同架构的 Linux Edge、Edge Host Agent，以及已登记的 staging Edge 名称。
- Core Host Agent 与 Edge Host Agent 均预置 RC 发行公钥，并允许受控执行；Core 已写入 v0.1.2 已知良好 `compose.known-good.yaml`。
- `docker`、`docker compose`、`curl`、`jq`、`gh`、`git` 和 `systemctl` 可用；Runner 服务账号仅拥有这些 staging Docker 资源的运维权限。

工作流只从 GitHub `staging` Environment Secret 读取以下值：`VISICORE_STAGING_ADMIN_USERNAME`、`VISICORE_STAGING_ADMIN_PASSWORD`、`VISICORE_STAGING_RECOVERY_KEY`。在 Runner 本地不得持久化这些值，也不得配置发行私钥。候选发布工作流在 GitHub 托管执行器中使用现有发行私钥生成故障描述，Runner 只验证其公钥签名。

Linux Runner 需要配置的非机密变量：

```text
VISICORE_STAGING_WORKSPACE=/opt/visicore-staging
VISICORE_STAGING_BASELINE_BACKUP_PATH=/opt/visicore-staging/fixtures/v0.1.2.vcbackup
VISICORE_STAGING_CORE_COMPOSE_FILE=/opt/visicore-staging/core/compose.yaml
VISICORE_STAGING_BASELINE_ENV_FILE=/opt/visicore-staging/core/v0.1.2.env
VISICORE_STAGING_CORE_PROJECT=visicore-staging-core-amd64
VISICORE_STAGING_API_BASE_URL=https://core-amd64.staging.visicore.internal
VISICORE_STAGING_ORIGIN=https://core-amd64.staging.visicore.internal
VISICORE_STAGING_EDGE_AGENT_NAME=visicore-staging-linux-amd64-edge
```

arm64 使用对应的 `core-arm64`、`linux-arm64-edge` 和 arm64 内部 DNS。脚本会先删除该 Compose 项目自己的卷，再调用 `/api/v1/setup/restore`；因此这些卷不得与任何非 staging 服务共享。

## Windows Runner

Windows Runner 必须已安装 v0.1.2 Edge MSI，并把该 MSI 固定在 `C:\ProgramData\VisiCore\EdgeHostAgent\known-good\edge-node.msi`。`VisiCore Edge Agent` 与 `VisiCore Edge Host Agent` 服务必须运行，Host Agent 必须只信任发行公钥且允许执行。Windows Edge 以 `visicore-staging-windows-x64-edge` 身份登记到 amd64 staging 中心。

设置以下非机密变量：

```text
VISICORE_STAGING_WORKSPACE=C:\VisiCoreStaging
VISICORE_STAGING_API_BASE_URL=https://core-amd64.staging.visicore.internal
VISICORE_STAGING_EDGE_AGENT_NAME=visicore-staging-windows-x64-edge
VISICORE_STAGING_EDGE_SERVICE_NAME=VisiCore Edge Agent
VISICORE_STAGING_EDGE_HOST_SERVICE_NAME=VisiCore Edge Host Agent
```

Windows 演练经中心的升级计划让 Host Agent 下载并执行受签名 RC MSI，随后确认服务运行；Viewer 则安装候选 MSI，并执行 `--verify-mpv-runtime` 和 `--verify-login-shell`。

## 演练结果回填模板

每个 RC 的三平台作业会上传局部 JSON；汇总作业只在三个结果均为 `passed` 时创建并签名 `staging-evidence.json`，随后附加到候选 Release。实际完成后在 [证据索引](evidence.md) 回填：

| 字段 | 实际值 |
| --- | --- |
| 候选标签与来源提交 | 待填写 |
| Linux amd64 Actions 运行 | 待填写 |
| Linux arm64 Actions 运行 | 待填写 |
| Windows x64 Actions 运行 | 待填写 |
| Core 保护备份 ID 与回滚失败码 | 待填写 |
| `staging-evidence.json` SHA-256 | 待填写 |
| RC Release 证据资产链接 | 待填写 |

stable 提升成功后，另起独立文档提交填写 stable Release、Docker Hub 正式标签和 `latest` 的验证链接。不得修改已发布 RC 或 stable 标签。
