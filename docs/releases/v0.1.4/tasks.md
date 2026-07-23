# v0.1.4 实施任务

- [x] 将 CI 改为可复用工作流，并让 RC 发布强制等待完整质量门禁。
- [x] 将发布工作流收敛为 RC 标签与手动候选重跑；候选阶段不更新 `latest`。
- [x] 新增 production 审批后的 RC 提升工作流，复用候选 MSI 与 OCI digest。
- [x] 扩展统一发行描述与发布目录 API，保留旧 Agent 可忽略的兼容字段。
- [x] 在升级计划中保留阶段、制品 digest、保护备份和失败码，并增加批次确认 API。
- [x] 增加 SPDX、CycloneDX、GitHub provenance、Cosign 和离线验证说明。
- [x] 提供可复用 staging-validation 工作流、三台固定自托管 Runner 演练脚本和带 RSA-PSS 签名的汇总证据。
- [x] 将 stable 提升改为强制验证完整 staging 证据、候选提交与制品摘要。
- [ ] 在受控预发布基础设施完成本版本的实际 Linux amd64、Linux arm64、Windows x64 演练，并把运行 ID 写入 `evidence.md`。
- [ ] RC 演练通过后，由发布负责人从 GitHub Actions 手工运行 `Promote RC Release`。
