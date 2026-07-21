# 更新日志

本项目遵循语义化版本。

## 0.1.1

- 核心 Docker 镜像内置 PostgreSQL `18.4`、MediaMTX `1.19.2`、API、StreamGateway、管理端与 Nginx；首次安装不再依赖额外数据库或媒体容器。
- 新增加密平台备份、每日自动保留、上传下载、跨服务器恢复与恢复密钥机制。
- 新增中心和边缘节点受控升级计划、签名发行描述、升级回执与版本兼容校验。
- 新增 Core Host Agent，支持在受限的 Linux 主机上执行已验证的中心镜像升级。
- 边缘节点增加本地配置持久化、资源策略、注册使用量上报与受控更新执行器。
- Windows Edge Node 安装包集成更新执行器；后台增加中心、Docker 边缘节点和 Windows 边缘节点的发布计划管理。
- 发布流水线统一基于 Git 标签生成 Viewer MSI、Edge Node MSI、核心镜像和边缘节点镜像，并附带多架构摘要、SBOM 与签名清单。

## 0.1.0

- 作为无历史的 VisiCore 开源基线，提供中心 Docker 部署、ONVIF／RTSP 核心能力、Windows 查看端与 Windows 边缘节点安装包。
- 中心与边缘节点镜像分别发布到 GHCR 的 `ghcr.io/zhaohe574/visicore-core` 和 `ghcr.io/zhaohe574/visicore-edge-node`，同时支持 Linux x64 与 ARM64。
- GitHub Release 仅发布 Windows Viewer、Windows Edge Node MSI、哈希校验、查看端运行时说明和 Windows 受控升级签名文件。
