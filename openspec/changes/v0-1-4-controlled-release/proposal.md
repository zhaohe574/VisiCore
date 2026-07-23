# v0.1.4 RC 到 stable 受控发行试点

## 目标

将 v0.1.4 作为首次候选制品提升试点：RC 构建一次，stable 只提升相同提交、MSI 字节与 OCI digest。

## 边界

保持 Docker Hub 单仓库、不可变 digest、RSA-PSS 发行描述、Host Agent 验签、保护备份与回滚。外置数据库部署不支持原地迁移。
