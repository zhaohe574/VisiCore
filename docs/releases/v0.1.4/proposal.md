# v0.1.4 发布提案

## 目标

将 `0.1.4` 作为 VisiCore 的首个 RC 到 stable 受控发布试点。候选版本只由 `v0.1.4-rc.N` 触发构建；正式版本只能提升已验收的候选制品，禁止重新编译 MSI 或重新构建镜像。

## 范围

- 将 CI 作为 RC 构建前置门禁，覆盖 Core、Linux Edge、Windows Edge、Viewer、管理端和测试项目。
- 为统一发行描述增加发行标识、通道、来源提交、提升来源、迁移与回滚策略。
- 为中心与边缘升级计划增加持久化过程时间线及后续批次人工确认。
- 为 RC 与 stable 附加 SPDX、CycloneDX、GitHub provenance、Cosign 签名和可独立验证的摘要证据。
- 建立 Linux amd64、Linux arm64、Windows x64 的预发布演练记录。

## 不在范围内

- 不改变 Docker Hub 单仓库策略，不引入 GHCR 或双镜像仓库。
- 不把 `latest` 作为 Host Agent 的升级输入；Host Agent 继续消费带 `sha256` 的不可变镜像引用。
- 不支持外置 PostgreSQL 或外置 MediaMTX 部署的原地迁移。
- 不替换现有 RSA-PSS 发行描述验签、保护备份或回滚实现。

## 发布决策

stable 提升需要 production 环境审批，并且必须证明：候选与正式标签指向同一提交；Windows MSI SHA-256 一致；Core 与 Edge OCI digest 一致。任一项不成立即拒绝发布。
