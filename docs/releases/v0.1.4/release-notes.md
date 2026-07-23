# VisiCore v0.1.4

## 升级影响

本版本引入受控的 RC 到 stable 发布闭环。中心升级继续在切换前生成加密保护备份；Edge 使用不可变镜像 digest 或受签名 MSI；Windows Viewer 使用受控运行时校验。

## 维护窗口与兼容性

中心切换和数据库恢复可能中断当前会话。升级前确认四个 Docker 持久卷连续，并在后台创建且下载备份、离线保存恢复密钥。外置 PostgreSQL 或 MediaMTX 部署不支持原地迁移。

## 回滚

Core 使用 `backup-restore`：由 Core Host Agent 在保护备份存在时执行恢复。Linux Edge 使用 `image-only` 回退到上一个已验证 digest；Windows Edge 与 Viewer 使用上一版本 MSI。任何不可逆迁移不得使用镜像回退。

## 验证结果

RC 与 stable 的最终运行记录、制品摘要、签名、provenance 和预发布演练链接见 [证据索引](evidence.md)。在该索引填充前，本说明仅是待发布版本说明。
