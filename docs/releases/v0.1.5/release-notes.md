# VisiCore v0.1.5

## 发布治理

- 重要变更使用 OpenSpec 建立提案、设计、任务与验证档案。
- 版本中心可关联 GitHub Release、Actions、staging 证据、SBOM、provenance 与升级计划。
- 后台只展示可追溯外链；RC、stable、镜像标签和签名仍由 GitHub Actions 受控执行。

## 升级与回滚

本版本为内部平台库新增治理记录表。升级前自动创建保护备份；如升级失败，按 `backup-restore` 策略恢复。外置数据库部署不支持原地迁移。
