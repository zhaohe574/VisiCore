# 预发布环境

该目录用于与生产隔离的 RC 演练。每个环境都必须使用发行描述中记录的 `repository@sha256:...`，不可用 `latest` 或普通版本标签替代。

## 环境矩阵

| 环境 | 必测角色 |
| --- | --- |
| `linux-amd64-core` | Core Host Agent、Core、Linux Edge |
| `linux-arm64-core` | Core Host Agent、Core、Linux Edge |
| `windows-x64-edge` | Windows Edge、Windows Viewer |

从 [staging.env.example](staging.env.example) 复制一个不提交的 `.env`，填入从同一 RC Release 工作流的 `visicore-release-governance` 工件读取的 digest，然后执行：

```powershell
docker compose --env-file .env -f compose.yaml config
docker compose --env-file .env -f compose.yaml up -d
```

使用 `tools/verify-release-promotion.sh <公开资产目录> <治理工件目录> <候选标签>` 对下载后的候选 Release 做离线验签与摘要核验。`Staging Validation` 会在候选 Release 发布后自动调度三台固定自托管 Runner；完整的目录、服务、密钥边界和结果回填要求见 [`docs/releases/v0.1.4/staging-runner-setup.md`](../../docs/releases/v0.1.4/staging-runner-setup.md)。故障注入和保护备份恢复必须在独立预发布实例完成。
