# 更新日志

本项目遵循语义化版本。

## 0.1.6

- GitHub Release 公开制品收敛为签名校验清单、Core／Edge 双架构 Linux Docker 部署包、Windows Edge MSI 和 Windows Viewer MSI；发布治理证据仅保留在 Actions 工件中。
- Linux Core 与 Edge 部署包自动准备 Docker Engine、固定 Docker Hub digest、安装受限 Host Agent，并支持 Ubuntu 22.04/24.04、Debian 12 与 RHEL/Rocky/AlmaLinux 9 的 amd64、arm64 首次安装。
- RC 到 stable 复用相同 Linux 包、Windows MSI 和 OCI digest，仅重命名公开文件并重新签名稳定版元数据与校验清单。

## 0.1.5

- 新增 OpenSpec 重要变更档案、发行清单和 Pull Request／RC 发布治理门禁。
- 版本中心新增发行治理记录，关联不可变文档提交、GitHub Release、Actions、staging 证据、SBOM、provenance 和既有升级计划。
- 治理记录仅保存受限 GitHub 外链；后台不保存 GitHub、Docker Hub 或发行签名凭据，RC 与 stable 继续由 GitHub Actions 执行。

## 0.1.4

- 建立 RC 到 stable 受控发布闭环：RC 复用完整 CI，正式版本只提升同一提交、MSI 字节和 OCI digest；新增发布档案、SPDX、CycloneDX、Cosign、GitHub provenance 与预发布演练模板。
- 升级计划新增后续批次人工确认、持久化过程时间线、制品摘要、保护备份 ID 与失败码展示；数据库迁移和回滚策略由受签名发行描述显式声明。
- 管理端新增可深链的业务路由，增加“运行指标”页面；运行总览、边缘节点、备份、升级与告警页面复用统一数据加载状态。
- 扩展 Playwright 管理端冒烟测试，显式模拟已完成初始化状态，覆盖运营指标导航、指标数据渲染和深链 URL；CI 新增该 E2E 任务。
- CI 补齐 StreamGateway、DeviceWorker 和 NotificationWorker 的恢复、构建与单元测试。
- Compose 与 `.env.example` 的默认核心镜像更新至 `visicore/visicore-core:0.1.4`；升级失败仪表将旧摘要归并为受控 `failure.code` 标签，避免高基数指标。
- 收紧本地测试报告、浏览器缓存、覆盖率、NuGet 包与临时制品的忽略规则。

## 0.1.3

- API 引入自动发现的端点模块契约，首次迁出系统安装、认证、公开离线设备、边缘 Agent、备份、设备插件和 HTTPS 配置域；后续新增域无需继续扩展 `Program.cs`。
- 新增统一 Problem Details 错误模型、可复用的权限端点过滤器与审计装饰器，作为后续域迁移基础设施。
- 接入 OpenTelemetry trace、metrics 和 logs 管线；新增媒体会话数、边缘心跳、升级失败码与备份结果指标，并提供受运维权限保护的后台仪表接口。

## 0.1.2

- 修复核心 API 的 DELETE 路由请求体推断，避免容器在启动阶段退出。
- Compose 卷名改为稳定显式名称；受控核心升级在镜像切换前后校验运行容器的卷连续性。
- 分离 Core、Edge、Viewer 与管理端的版本来源，发布工作流、镜像、MSI、SBOM 和版本上报按对应制品版本生成。
- 新增核心容器升级切换与卷复用冒烟测试，以及 Core Host Agent、Edge Agent 的版本与运行态回归测试。

## 0.1.1

- 核心 Docker 镜像内置 PostgreSQL `18.4`、MediaMTX `1.19.2`、API、StreamGateway、管理端与 Nginx；首次安装不再依赖额外数据库或媒体容器。
- 新增加密平台备份、每日自动保留、上传下载、跨服务器恢复与恢复密钥机制。
- 新增中心和边缘节点受控升级计划、签名发行描述、升级回执与版本兼容校验。
- 新增 Core Host Agent，支持在受限的 Linux 主机上执行已验证的中心镜像升级。
- 边缘节点增加本地配置持久化、资源策略、注册使用量上报与受控更新执行器。
- Windows Edge Node 安装包集成更新执行器；后台增加中心、Docker 边缘节点和 Windows 边缘节点的发布计划管理。
- 发布流水线统一基于 Git 标签生成 Viewer MSI、Edge Node MSI、核心镜像和边缘节点镜像，并附带多架构摘要、SBOM 与签名清单；Docker 镜像仅发布到 Docker Hub，不发布到 GitHub Packages。

## 0.1.0

- 作为无历史的 VisiCore 开源基线，提供中心 Docker 部署、ONVIF／RTSP 核心能力、Windows 查看端与 Windows 边缘节点安装包。
- 中心镜像发布到 Docker Hub 的 `visicore/visicore-core`，同时支持 Linux x64 与 ARM64。
- GitHub Release 仅发布 Windows Viewer、Windows Edge Node MSI、哈希校验、查看端运行时说明和 Windows 受控升级签名文件。
