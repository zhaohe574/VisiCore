# v0.1.6 兼容性与迁移矩阵

| 范围 | 支持 | 不支持或限制 |
| --- | --- | --- |
| Core Linux | Ubuntu 22.04/24.04、Debian 12、RHEL/Rocky/AlmaLinux 9；amd64、arm64 | Core Windows Server 首次安装 |
| Edge Linux | 同上；Docker 部署包 | 非 Linux Docker Edge 首次部署 |
| Windows | Edge x64 MSI、Viewer x64 MSI | Core Windows 自动安装器 |
| 升级与回滚 | 后台受签名升级计划、保护备份和已有恢复流程 | 直接运行部署包的 `upgrade`、`rollback` 绕过治理 |
| RC 到 stable | 同一包、MSI、OCI digest；仅文件名与稳定版证据改变 | stable 重新构建产品制品 |

既有 Compose 源码部署保持可用；生产新装优先使用本版本 Linux 部署包。四个 Core 数据卷名称仍是升级兼容契约。
