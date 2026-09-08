# Worker 独立回归

## 运行方式

先从工作区本地 NuGet 源还原依赖，包含 `Npgsql 10.0.0`：

```powershell
& 'C:\Users\57477\AppData\Local\VideoPlatform\dotnet\dotnet.exe' restore tools/VideoPlatform.Worker.Tests/VideoPlatform.Worker.Tests.csproj --source artifacts/nuget-feed
```

在当前进程环境变量 `VIDEO_PLATFORM_SSH_PASSWORD` 已配置、Python 与 Paramiko 可用的情况下运行：

```powershell
$env:PYTHONIOENCODING = 'utf-8'
python tools/VideoPlatform.Worker.Tests/run-postgres.py
```

专用入口复用 `tools/v2-environment.py` 的 SSH 连接、动态端口隧道及服务器测试配置，并复用导出专用入口的只读数据库身份与清理检查。只向测试子进程提供隔离库 `video_platform_v2_test` 的 `WORKER_TEST_DATABASE_URL`，不打印凭据，不执行部署或共享环境初始化。

入口执行 `dotnet test -c Release --no-restore`，保留原始退出码，结果写入 `artifacts/worker-tests/worker-postgres.trx`。现有框架为每项数据库测试创建随机 `worker_test_*` schema，执行迁移，结束后清理。缺少数据库配置时直接运行 `dotnet test` 会跳过数据库用例，不能作为真实回归通过的证据。

## 真实验证记录

2026-09-07，测试记录时间为 `16:55:47` 至 `16:56:49`（`+08:00`），耗时约 62.2 秒。本地源依赖还原、Release 编译和测试均成功。

连接经 `10.37.200.74` 的 SSH 隧道进入 `video_platform_v2_test`，PostgreSQL 版本为 `18.6 (Ubuntu 18.6-0ubuntu0.26.04.1)`，数据库会话时区为 `Etc/UTC`。

| 测试类别 | 执行 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- | --- |
| DatabaseTests，真实 PostgreSQL | 14 | 14 | 0 | 0 |
| FailureAndRaceTests，真实 PostgreSQL | 16 | 16 | 0 | 0 |
| UnitTests，含参数化用例 | 14 | 14 | 0 | 0 |
| 合计 | 44 | 44 | 0 | 0 |

覆盖导出并发领取、行锁跳过、提交响应丢失恢复、旧执行结果隔离、取消竞态、权限撤销、超时与重试、文件校验、报警事务回滚与幂等、媒体和云台租约、设备同步并发、保留期清理及心跳。TRX 记录执行 44 项，通过 44 项，失败和未执行均为 0；专用入口退出码为 0。

执行后只读检查确认隔离库没有新增测试 schema 残留，本地 `bin/Release/net10.0/.runtime` 为空。测试退出时既有隧道脚本出现 `ConnectionResetError / WinError 10054`，表示连接被对端重置；完整 TRX 和随后独立数据库清理检查均成功，没有修改共享隧道源码。

本次没有修改任何现有 Worker 用例或 Infrastructure 共享源码，仅新增专用运行脚本和本报告。没有连接生产库 `video_platform_v2` 运行初始化，没有部署。数据库及 Worker 逻辑为真实实现，设备适配器使用现有模拟实现；结果不代表物理设备联调或候选服务线上验收。
