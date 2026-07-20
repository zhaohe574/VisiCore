# 贡献指南

欢迎提交 Issue 和 Pull Request。请先阅读 README 的能力与安全边界。

## 提交要求

- 每个提交必须包含 DCO 签署：`git commit -s -m "说明"`。
- 不提交设备地址、账号、密码、令牌、私钥、证书、厂商 SDK、构建产物或 `.env`。
- 后端修改至少运行相关 `dotnet test`；管理端修改运行 `npm run typecheck` 和 `npm run build`。
- 新设备能力优先实现为独立 `external-edge` 插件，不得把厂商 SDK 引入核心项目。

## Pull Request

说明问题、实现方式、测试结果、兼容性影响和安全边界。涉及插件时必须给出不可变镜像摘要、签名公钥 ID、支持的平台版本与最小权限说明。
