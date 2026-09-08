# VisiCore（视枢）平台 API

.NET 10 模块化 API，业务接口统一位于 `/api/v2`。职责包括认证权限、设备与通道、组织、媒体、报警、导出、审计、在线会话和桌面版本管理。

## 接口与安全

- `GET /health`：公开健康检查，包含数据库连通性检查。
- `/api/v2/openapi/v2.json`：真实路由生成的 OpenAPI；元数据集中于 `ApiContractMetadata.cs`。
- `/api/v2/auth`：登录、当前用户、刷新、注销、资料和密码。
- 业务路由按模块定义于 `Modules/`，不再保留第一版业务接口兼容层；旧版本检查与下载入口仍提供升级路径。
- Web 使用 HttpOnly 安全 Cookie 和防伪令牌；桌面使用 Bearer。会话可由服务器主动撤销。
- 普通账号未配置范围默认无权访问。媒体操作使用全局通道 ID，由服务端解析设备与设备内通道号。

## 运行配置

`PLATFORM_DATABASE_URL` 和 `HIK_ADAPTER_INTERNAL_KEY` 为必要配置。首次空库启动使用 `PLATFORM_ADMIN_USER`、`PLATFORM_ADMIN_PASSWORD` 创建管理员；后续启动不会覆盖已有密码。

数据目录由 `PLATFORM_DATA_PATH` 配置，适配器地址为 `HIK_ADAPTER_API_URL`，媒体服务使用 `ZLM_API_URL`、`ZLM_API_SECRET`，公开入口为 `PLATFORM_PUBLIC_URL`。具体部署、证书和服务配置见 [部署运维](../../docs/v2-部署运维.md)。

```powershell
dotnet build src/VideoPlatform.Api/VideoPlatform.Api.csproj -c Release
dotnet run --project src/VideoPlatform.Api/VideoPlatform.Api.csproj
```

运行前须通过当前进程环境注入有效配置。启动时自动执行 `database/v2` 迁移，生产部署前必须备份；不要把真实凭据放入仓库。后台恢复由 Worker 执行，设备命令交给独立适配器。
