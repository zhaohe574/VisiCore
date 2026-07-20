# 更新日志

本项目遵循语义化版本。

## 未发布

- 核心 Docker 镜像内置 PostgreSQL `18.4`、MediaMTX `1.19.2`、API、StreamGateway、管理端与 Nginx；首次安装不再依赖额外数据库或媒体容器。
- 新增加密平台备份、每日自动保留、上传下载、跨服务器恢复与恢复密钥机制。

## 0.1.0

- 作为无历史的 VisiCore 开源基线，提供中心 Docker 部署、ONVIF／RTSP 核心能力、Windows 查看端与 Windows 边缘节点安装包。
- 中心与边缘节点镜像分别发布到 Docker Hub 的 `visicore/visicore-core` 和 `visicore/visicore-edge`，同时支持 Linux x64 与 ARM64。
- GitHub Release 仅发布 Windows Viewer、Windows Edge Node MSI、哈希校验、查看端运行时说明和 Windows 受控升级签名文件。
