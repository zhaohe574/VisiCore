# VisiCore · 视枢

面向海康 SDK 兼容录像机的视频管理与值守平台，提供 Vue 管理端、WPF 原生桌面端，以及独立运行的 API、后台任务和设备适配服务。

## 功能

- 多录像机、全局通道、业务组织、用户角色和显式数据范围管理。
- 实时预览、录像检索与回放、1／4／9／16 分屏、个人布局及轮巡。
- 会话撤销、媒体租约与共享上游、云台独占控制、报警处理闭环。
- 异步录像导出、版本发布、MSI／ZIP 下载及独立更新器。
- PostgreSQL 持久化任务、事务发件箱、审计和资源回收。

## 技术与目录

| 路径 | 用途 |
| --- | --- |
| `VisiCore.slnx` | .NET 10 解决方案入口。 |
| `web-admin/` | Vue 3、TypeScript、Vite、Pinia、Element Plus 管理和值守页面。 |
| `src/VideoPlatform.Api/` | `/api/v2`、OpenAPI 和 SignalR。 |
| `src/VideoPlatform.Domain/`、`src/VideoPlatform.Application/`、`src/VideoPlatform.Infrastructure/` | 领域、应用与基础设施职责。 |
| `src/VideoPlatform.Worker/` | 导出、报警入库、保留策略与后台恢复。 |
| `src/Hikvision.Adapter/` | 海康 HCNetSDK、多设备隔离与媒体适配。 |
| `src/VideoPlatform.Desktop/`、`src/VideoPlatform.Updater/` | WPF、LibVLCSharp 和独立更新器。 |
| `database/v2/` | 第二版数据库迁移；根目录其他 SQL 属于第一版历史。 |
| `deploy/`、`tools/`、`docs/` | 部署定义、构建与验证工具、中文文档。 |

服务端使用 PostgreSQL、Nginx、ZLMediaKit、FFmpeg。海康 SDK 需自行取得合法使用权限并按适配器文档配置，不随仓库分发。

## 开发与验证

准备 .NET 10 SDK（版本策略见 `global.json`）、Node.js 22 或更新的受支持 LTS，以及 Windows 桌面构建环境。Python 验证与发布脚本使用 Paramiko，图标生成使用 Pillow。

```powershell
dotnet build VisiCore.slnx -c Release
npm ci --prefix web-admin
npm run build --prefix web-admin
npm test --prefix web-admin
dotnet test tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj -c Release --filter "Category!=Package&Category!=Candidate"
python -m unittest discover -s tools -p test_v2_soak.py -v
```

以上桌面回归不启动正式 MSI 安装或真实录像机测试。数据库、设备联调、长时值守和整机安装验收按各工具的 README 单独配置；缺少环境导致跳过的测试不能计作通过。

```powershell
npm run dev --prefix web-admin
```

Web 本地代理目标由 `VITE_API_PROXY_TARGET` 配置，HTTPS 证书须正常受信任。第二版 API 依赖数据库和后台服务，完整启动与环境配置见部署文档；不得把真实账号、密码、令牌或私钥提交到 Git。

## 文档与状态

- [架构说明](docs/v2-架构说明.md)、[部署运维与回滚](docs/v2-部署运维.md)。
- [实施进度](docs/v2-实施进度.md)、[验收清单](docs/v2-验收清单.md)。
- [桌面复测](docs/v2-桌面2.0.1复测.md)、[容量验证](docs/v2-容量验证.md)、[连续值守](docs/v2-连续值守.md)。
- [品牌更名与兼容说明](docs/VisiCore-更名说明.md)、[变更记录](CHANGELOG.md)。

当前源码包含平台第二版与桌面 2.0.1 基线、后续修复及 VisiCore 品牌更新。此次 Git 整理不等于重新发布安装包或变更线上服务。已有构建、自动回归和部分实机验证记录；24 小时连续值守最近一轮提前停止、未通过，整机安装、完整 DPI、真实报警和 PTZ 停止矩阵仍待验收。MSI／ZIP 尚未代码签名。

`artifacts/`、本机验收截图、原始日志、备份及专有 SDK 均不随源码推送。文档中的历史本机产物路径是证据定位，不是克隆后应当存在的源码依赖。
