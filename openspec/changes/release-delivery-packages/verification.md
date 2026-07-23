# 发布部署包验证

- [x] Core、Edge 包均包含当前架构 Host Agent、Compose、发行描述、RSA-PSS 签名和公钥。
- [x] 包内 Compose 不包含 `build`，业务镜像必须为 Docker Hub SHA-256 digest。
- [x] 构建器保留既有发行目录内容，不会删除已签名描述或 Windows MSI。
- [x] stable 证据将候选文件名映射为正式文件名，而包与 MSI 字节保持不变。
- [x] CI 覆盖 Bash 语法和离线包结构测试；最终 Linux 语法检查由 Ubuntu Runner 执行。
