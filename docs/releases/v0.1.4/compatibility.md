# v0.1.4 兼容性与迁移矩阵

| 组件或路径 | 最低版本／前置条件 | 迁移策略 | 回滚策略 | 验证方式 |
| --- | --- | --- | --- | --- |
| Core Host Agent | 以发行描述 `minimumHostAgentVersion` 为准 | `automatic-backup` | `backup-restore` | 验签、保护备份、`/healthz`、`/readyz` 与观察窗口 |
| Linux Edge（amd64、arm64） | 以发行描述 `minimumHostAgentVersion` 为准 | `none` | `image-only` | Host Agent 验签、镜像 digest、就绪回执 |
| Windows Edge（x64） | 以发行描述 `minimumHostAgentVersion` 为准 | `none` | `image-only` | MSI SHA-256、Host Agent 回执、服务状态 |
| Windows Viewer（x64） | 受控 Qt／libmpv 运行时变量已配置 | 不涉及数据库 | 重新安装上一 MSI | 安装、卸载、`--verify-mpv-runtime`、`--verify-login-shell` |
| 外置 PostgreSQL／MediaMTX 部署 | 不支持原地迁移 | 不适用 | 保持旧部署或从备份恢复 | 单独的数据迁移评估 |

## 数据库边界

`automatic-backup` 仅表示 Core 在切换前创建加密保护备份。其对应的 `rollbackStrategy` 必须为 `backup-restore`。不得把含不可逆数据库改变的版本标为 `image-only`；没有数据库迁移的 Edge 与 Viewer 才可以使用镜像或 MSI 回退。

## 版本与通道

- RC：`releaseId=v0.1.4-rc.N`，`channel=rc`，不更新 `latest`。
- stable：`releaseId=v0.1.4`，`channel=stable`，`promotedFrom=v0.1.4-rc.N`。
- 两个描述均记录同一个 40 位 `sourceCommit`。所有 Docker 制品均使用 `repository@sha256:...` 作为升级输入。
