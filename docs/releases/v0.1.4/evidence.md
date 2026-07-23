# v0.1.4 不可变证据索引

本文件在 RC 与 stable 执行后填写实际链接和摘要。没有运行记录时不得将本版本标记为已发布。

| 证据 | RC | stable |
| --- | --- | --- |
| GitHub Actions 运行 | 待执行 | 待提升 |
| Git commit | 待执行 | 必须与 RC 相同 |
| GitHub Release | 待执行 | 待提升 |
| `release-evidence.json` | 待执行 | 待提升 |
| `release-sha256.txt` 与 RSA-PSS 签名 | 待执行 | 待提升 |
| SPDX 与 CycloneDX | 待执行 | 待提升 |
| GitHub provenance | 待执行 | 待提升 |
| Core OCI digest 与 Cosign 验证 | 待执行 | 待提升 |
| Edge OCI digest 与 Cosign 验证 | 待执行 | 待提升 |
| `staging-evidence.json` 与 RSA-PSS 签名 | 待执行 | 候选副本摘要必须写入 stable `release-evidence.json` |
| 预发布演练记录 | 待执行 | 只引用 RC，不重新演练 |

运行 RC 后，将 Actions 运行 URL、候选 `releaseId`、`sourceCommit`、MSI SHA-256、两个 OCI digest、`staging-evidence.json` 摘要和预发布环境运行记录填入此处。正式提升只引用 RC 证据，不重新构建产品制品；stable 成功后另以独立文档提交回填实际链接，禁止改写已发布标签。
