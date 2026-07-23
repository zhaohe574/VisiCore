# v0.1.5 发布提案

## 目标

交付发布治理中心：重要变更用 OpenSpec 追溯，已验签版本在后台关联 GitHub 发布证据外链和升级计划。

## 边界

GitHub Actions 仍是 RC 与 stable 的唯一发布执行者。后台不保存 GitHub 凭据、不触发工作流、不推送标签，也不替代 RSA-PSS、Cosign 或 provenance 验证。
