# 控制台集成回归

## 实际入口

本项目和 `VideoPlatform.Admin.Tests` 都是控制台程序，必须使用 `dotnet run`。`dotnet test` 没有执行这些控制台检查时，不能据其退出码声称通过。

在当前进程已配置 `VIDEO_PLATFORM_SSH_PASSWORD`、Python 和 Paramiko 可用的情况下，可使用专用协调入口：

```powershell
$env:PYTHONIOENCODING = 'utf-8'
python tools/run-v2-regression.py
```

脚本从 `artifacts/nuget-feed` 串行还原并构建 API、IntegrationTests 和 Admin.Tests，随后执行测试。可以使用 `--suite integration` 或 `--suite admin` 单独重跑；只运行 Admin 时不会构建或启动 API 项目。

实际控制台命令如下，调用前已完成 Release 构建，所需连接配置通过环境变量注入：

```powershell
& 'C:\Users\57477\AppData\Local\VideoPlatform\dotnet\dotnet.exe' run --project tools/VideoPlatform.IntegrationTests/VideoPlatform.IntegrationTests.csproj -c Release --no-build --no-restore --no-launch-profile
& 'C:\Users\57477\AppData\Local\VideoPlatform\dotnet\dotnet.exe' run --project tools/VideoPlatform.Admin.Tests/VideoPlatform.Admin.Tests.csproj -c Release --no-build --no-restore --no-launch-profile
```

IntegrationTests 通过 `TEST_API_URL` 访问本地真实 API，通过 `TEST_ADAPTER_URL` 指定其内置模拟适配器的监听地址。Admin.Tests 自行启动动态端口 HTTP 宿主，使用 `ADMIN_TEST_DATABASE_URL`，运行目录可通过 `ADMIN_TEST_DATA_PATH` 指定。

## 隔离与清理

协调脚本复用 `tools/v2-environment.py` 的 SSH、服务器受限测试配置和动态端口隧道。每轮创建全新的 `vp_regression_<随机标识>` 数据库，所有者为已有测试角色 `video_platform_v2_test`。初始化和业务写入只进入该新库，Admin 额外使用随机 `admin_test_*` schema。

本地 API 和模拟适配器使用空闲端口；API 就绪后才启动 IntegrationTests。完成后停止本脚本启动的进程，删除本轮新库及运行目录，保留脱敏日志和 `summary.json`。生产库 `video_platform_v2` 不参与测试；脚本不调用部署入口。数据库名及本地删除路径均受本轮生成值约束。

汇总同时要求程序退出码为 0、出现最终完成声明、逐条“通过”行数与声明一致且大于 0，避免把空运行当作通过。

## 本次真实结果

2026-09-07，经 `10.37.200.74` SSH 隧道连接 PostgreSQL `18.6 (Ubuntu 18.6-0ubuntu0.26.04.1)`，数据库会话时区为 `Etc/UTC`。

| 项目 | 最终通过检查数 | 退出码 | 证据 |
| --- | --- | --- | --- |
| IntegrationTests | 78 | 0 | `artifacts/v2-regression/a2fd1231d1b5483abf02c3a2a5608955/integration.log` |
| Admin.Tests | 20 | 0 | `artifacts/v2-regression/9e3445ea582f45b7a91f212aba946cf3/admin.log` |

首轮时间为 `17:07:27` 至 `17:07:53`（`+08:00`）。IntegrationTests 完整通过 78 项；Admin 首轮通过 17 项后，在后台心跳断言失败，故首轮总体退出码为 1，该记录原样保留。

失败原因是 Admin 测试仍查找服务名称 `worker`，而现有正式实现及客户端回归已使用“后台任务”。仅将测试中在线、降级两处断言同步为当前名称，并使用 `Single` 验证唯一服务项；未修改共享实现。

Admin 单独重跑时间为 `17:08:53` 至 `17:09:12`（`+08:00`），20 项检查全部通过，包括原失败项、HTTP 管理路由以及最后管理员并发保护。本次合计通过 98 项控制台检查，不将首次部分通过的 17 项重复计数。每组检查可能包含多个断言，98 是程序报告的检查数，不是独立断言总数。

两轮 `summary.json` 均确认数据库、运行目录清理完成。结束后检查确认测试及 API 进程不存在，Integration 使用的 `58704`、`58705` 端口不再监听。共享 API 构建占用已释放。

既有隧道脚本在进程退出时输出 `ConnectionResetError / WinError 10054`，表示连接被对端重置；测试完成声明、逐条检查数和随后的独立清理检查均成功。未修改共享隧道源码。

本轮源码修改仅为新增协调脚本、Integration 模拟适配器端口配置、Admin 独立目录配置及过时服务名称断言更新。API、Infrastructure、Hikvision.Adapter、desktop 和 web 源码未由本轮修改，未部署。数据库及 API/管理业务路径为真实实现；设备适配器和 Admin 媒体 HTTP 为模拟实现，结果不代表设备实机或已部署候选的线上验收。
