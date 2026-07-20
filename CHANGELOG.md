# 更新日志

本项目遵循语义化版本。

## 0.3.0

- 新增统一 Edge Agent 协议，支持 Linux Docker 与 Windows x64 Service 使用同一配对、公钥凭据、设备同步、预检和配置回执链路。
- 新增独立 Edge Host Agent、受签名发行清单、最小回执、Docker 固定 Compose 升级与 Windows MSI 固定安装器执行边界。
- 发布流程新增多架构 Linux Edge 镜像、Windows MSI、SBOM、SHA-256 和离线 RSA 签名发行清单。
- 修复查看端 Release 对 libmpv 发行包的解压与哈希校验，并随 Release 附带边缘升级签名公钥。

## 0.2.0

- 项目正式定名为“视枢（VisiCore）”，统一源码工程、程序集、管理端、查看端、Compose、数据库默认标识和发布镜像名称。
- Docker 安装默认拉取 `ghcr.io/zhaohe574/visicore-*` 发布镜像；源码开发仍可通过 `--build` 本地构建。
- 此版本是全新品牌基线，与 `v0.1.0` 的本地卷、数据库默认名、浏览器会话和桌面端本地设置不兼容。

## 0.1.0

- 首个开源版本，提供 Docker 安装、ONVIF／RTSP 核心能力和可签名的外部设备插件契约。
- 移除厂商 SDK、内部部署材料和历史数据库兼容路径。
