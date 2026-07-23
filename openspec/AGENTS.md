# VisiCore OpenSpec 变更约定

## 适用范围

跨组件、公开 API、数据迁移、部署、发布工作流、安全边界和用户可见行为变更，必须在实现前创建 `openspec/changes/<change-id>/`。纯文档、测试及局部低风险修复可以豁免。

## 必需内容

每项变更必须包含中文的 `proposal.md`、`design.md`、`tasks.md`、`verification.md` 与 `change.json`。`change.json` 的 `status` 依次使用 `proposed`、`approved`、`implemented`、`verified`；只有 `verified` 的变更才能被 RC 发行档案引用。

## 发行关联

每个重要变更必须声明目标 `releaseId`。对应的 `docs/releases/<release-id>/release-manifest.json` 必须引用该 change ID，并将发布说明、兼容性、验证和证据索引固定为同一发行档案。stable 发布后，档案可在独立文档提交中归档；已发布标签不得改写。

## 安全边界

OpenSpec 不得包含设备地址、账号、密码、令牌、私钥、证书、生产备份或可恢复凭据。发行签名、Docker Hub 凭据和 GitHub 发布权限只保留在 GitHub Actions 环境。
