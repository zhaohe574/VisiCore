# 发布部署包设计

- RC 构建 Linux Host Agent、镜像 digest、Windows MSI 和四个 Linux tar.gz；包内包含安装脚本、Docker Compose、Host Agent、发行描述与签名公钥。
- 安装器支持 Ubuntu 22.04/24.04、Debian 12、RHEL/Rocky/AlmaLinux 9 的 amd64、arm64，并只从 Docker Hub digest 拉取业务镜像。
- stable 不重建：直接复制候选包与 MSI，重命名为正式版本文件名，重新生成 `checksums.txt`、stable 描述和证据。
- 发行描述、SBOM、CycloneDX、staging 证据和 Host Agent 独立归档仅作为 Actions 治理工件保存。
