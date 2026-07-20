# 中心 HTTPS 证书

启用中心镜像的 HTTPS 前，将受信任 CA 签发的证书链和私钥放在此目录：

- `tls.crt`：PEM 格式证书链。
- `tls.key`：与证书匹配的 PEM 格式私钥。

私钥不可提交到仓库。生产环境建议由宿主机的受限部署账户写入，并限制该目录仅被 Docker 管理员和运行用户读取。

在 `.env` 中设置：

```text
VISICORE_HTTPS_ENABLED=true
ADMIN_HTTPS_BIND_ADDRESS=0.0.0.0
ADMIN_HTTPS_PORT=8443
```

如使用其他文件名或受控挂载目录，可调整 `VISICORE_TLS_DIRECTORY`、`VISICORE_TLS_CERTIFICATE_FILE` 和 `VISICORE_TLS_PRIVATE_KEY_FILE`。容器只接受 `/run/visicore/tls/` 下的只读证书路径；缺失、不可读或不在该目录内的文件会让容器拒绝启动。
