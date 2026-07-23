# 发布部署包提案

## 目标

为 v0.1.6 将 Core 与 Edge Linux 交付收敛为可校验的 Docker 部署包，并将 GitHub Release 的上传制品固定为七项公开下载文件。

## 边界

Core 继续仅以 Docker Hub 不可变 digest 运行；部署包不携带镜像层，不引入原生 Core 二进制交付。升级、回滚和备份恢复继续由已有的受签名升级计划执行。
