# 可观测性

从 `0.1.3` 起，中心 API 通过 OpenTelemetry 统一输出 trace、metrics 和 logs。未配置导出端点时，应用仍保留现有控制台或 Windows Event Log 日志，不会向网络发起遥测连接。

## 启用 OTLP

为核心 API 设置 `OTEL_EXPORTER_OTLP_ENDPOINT`，例如：

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
```

应用会以 `VisiCore.Api` 作为服务名，并将当前核心版本写入资源属性。OTLP 接收器必须位于受控内网；不要把未认证的 Collector 暴露到公网。

## 指标

| 指标 | 含义 |
| --- | --- |
| `visicore.stream.sessions.active` | 当前未撤销且未过期的媒体会话数。 |
| `visicore.stream.sessions.issued` | 成功创建的实时或回放会话累计数，含 `operation` 标签。 |
| `visicore.edge.agents.online` | 两分钟内有心跳的启用 Edge Agent 数。 |
| `visicore.edge.agents.stale` | 超过两分钟没有心跳的启用 Edge Agent 数。 |
| `visicore.edge.heartbeats` | 成功处理的心跳累计数，含 `platform` 标签。 |
| `visicore.upgrade.failures` | 按受控 `failure.code` 标签聚合的升级失败目标数。 |
| `visicore.backup.results` | 按 `result` 标签聚合的备份结果数。 |

业务快照每 15 秒从平台数据库刷新。升级指标仅接受平台已定义的失败码；空值归为 `unspecified_failure`，旧数据或自由文本归为 `unclassified_failure`。原始失败摘要保留在升级记录中，不作为指标标签输出。管理端的“运行指标”页读取 `GET /api/v1/admin/observability/overview`，需要 `ManageOperations` 权限；该接口只返回聚合值，不返回设备凭据、会话票据或升级制品地址。
