# VisiCore v0.1.6

## Linux Docker 部署包

Release 上传以下 Linux 包：

- `visicore-core-0.1.6-linux-amd64.tar.gz`
- `visicore-core-0.1.6-linux-arm64.tar.gz`
- `visicore-edge-0.1.6-linux-amd64.tar.gz`
- `visicore-edge-0.1.6-linux-arm64.tar.gz`

在 Release 页面下载目标包和 `checksums.txt` 后，于同一目录执行：

```bash
asset=visicore-core-0.1.6-linux-amd64.tar.gz
grep -F "  ${asset}" checksums.txt | sha256sum --check -
tar -xzf "$asset"
sudo ./visicore-core/install.sh install
```

Edge 安装只需将 `asset` 和解压目录替换为对应 Edge 文件。安装器会验证包内 RSA-PSS 发行描述，自动安装 Docker Engine 与 Compose 插件，并以 Docker Hub SHA-256 digest 启动业务容器。支持 Ubuntu 22.04/24.04、Debian 12、RHEL/Rocky/AlmaLinux 9 的 amd64、arm64。

`status` 与 `uninstall` 可直接执行；`upgrade` 和 `rollback` 必须由平台后台的受签名升级计划发起，以保留保护备份和回滚事务。

## Windows

- `visicore-edge-0.1.6-windows-amd64.msi`：Windows Edge Node。
- `visicore-viewer-0.1.6-windows-amd64.msi`：Windows Viewer。

Windows Server Core 不提供 Core 首次安装器。

## 发布保证

RC 到正式版复用同一 Linux 包、Windows MSI 和 Core／Edge OCI digest。正式版仅将公开文件名从 RC 改为稳定版本，并重新签名 `checksums.txt` 和内部治理证据；不会重新构建产品制品。
