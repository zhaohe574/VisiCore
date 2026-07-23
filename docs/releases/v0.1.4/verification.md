# v0.1.4 验证清单

## RC 前置质量

- [ ] 可复用 CI 全部通过。
- [ ] `VERSION` 与 `v0.1.4-rc.N` 的产品版本相同。
- [ ] Windows Edge MSI、Core Host Agent 双架构包、Viewer MSI、Core 和 Edge 双架构镜像均已生成。
- [ ] 发行描述、`release-sha256.txt` 和 RSA-PSS 签名通过离线验证。
- [ ] SPDX 与 CycloneDX 均包含本批制品摘要，GitHub provenance 可查询，OCI digest 有 Cosign 签名。

## 独立预发布环境

- [ ] 三台自托管 Runner 已分别带有 `visicore-staging-linux-amd64`、`visicore-staging-linux-arm64`、`visicore-staging-windows-x64` 标签，且均只能访问 staging 内部 DNS、Docker Hub 和 GitHub 受信域名。
- [ ] Linux Runner 仅从 `/opt/visicore-staging/fixtures/v0.1.2.vcbackup` 恢复；恢复密钥和 staging 管理员密码仅由 GitHub `staging` Environment Secret 注入。
- [ ] Windows Runner 的 Host Agent 固定保留 v0.1.2 已知良好 MSI；Windows Edge 与 Viewer 连到 amd64 staging 中心，不接触生产节点。
- [ ] Linux amd64 中心：从 `v0.1.2` 升级至 RC，观察窗口内 `healthz` 与 `readyz` 成功。
- [ ] Linux arm64 中心：从 `v0.1.2` 升级至 RC，验证同一 Core OCI digest 的 arm64 manifest。
- [ ] Linux amd64 与 arm64 Edge：登记 RC 描述并完成 Host Agent 验签、拉取、切换和回执。
- [ ] Windows x64 Edge：安装并升级 RC MSI，检查服务与 Host Agent 回执。
- [ ] Windows x64 Viewer：安装 RC MSI，执行运行时与登录壳验证。
- [ ] 故障注入：使一个金丝雀目标失败，确认计划暂停且未自动推进下一批。
- [ ] 恢复演练：使用升级保护备份恢复，记录备份 ID、恢复结果和失败码。
- [ ] `staging-evidence.json` 与其 RSA-PSS 签名已附加到 RC Release；其中候选标签、来源提交、Core／Edge digest、MSI 摘要和三平台结果均一致。

## 提升验证

- [ ] `v0.1.4-rc.N` 与 `v0.1.4` 指向同一 Git commit。
- [ ] RC 与 stable 的 Edge／Viewer MSI SHA-256 相同。
- [ ] RC 与 stable 的 Core／Edge OCI digest 相同；只有正式标签与 `latest` 被移动到该 digest。
- [ ] stable 描述的 `promotedFrom` 指向 RC，`sourceCommit` 与候选一致。
- [ ] 在干净环境运行离线验证命令，验证 RSA-PSS、摘要、Cosign 与 GitHub provenance。
