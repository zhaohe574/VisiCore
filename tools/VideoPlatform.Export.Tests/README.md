# 导出时间时区回归

## 修复内容

`ExportEndpoints.cs` 创建导出时对起止时间调用 `ToUniversalTime()`，以零偏移 `DateTimeOffset` 写入 PostgreSQL `timestamptz`，保留实际时刻。例如，`2026-09-07T00:15:00+08:00` 对应 `2026-09-06T16:15:00Z`。

范围校验仍按实际时刻比较，审计说明保留请求原始偏移。重试不重新写入录像起止时间，只清除租约并重新排队；执行时间和过期时间由现有 Worker 领取任务时刷新。列表时间来自数据库，下载按过期时刻与 `UtcNow` 比较。

## 测试范围

专用项目直接链接生产导出接口及现有 `VideoPlatform.Worker.Tests/DatabaseHarness.cs`，使用真实 HTTP、认证、权限和 Npgsql；设备适配器使用框架已有模拟实现。

- 创建与列表：`+08:00`、UTC、负偏移、非整点偏移、起止偏移不同、UTC 跨日、微秒精度和恰好 24 小时。
- 无效范围：相同时刻、倒序时刻、超过 24 小时均返回 400，不插入导出和创建审计。
- 重试：失败及取消任务重新排队后保留录像时间，清除租约；Worker 尚未释放或重复重试返回 409。
- 下载：未来过期时间允许下载，已过期返回 409；列表过期时间保持 UTC。

## 运行方式

仅将 `WORKER_TEST_DATABASE_URL` 配置为隔离测试库连接串，例如 `tools/v2-environment.py` 中的 `video_platform_v2_test`。禁止指向生产库。框架会在该测试库创建随机 schema，执行真实迁移，完成后清理 schema 和本地测试目录。

```powershell
& 'C:\Users\57477\AppData\Local\VideoPlatform\dotnet\dotnet.exe' test tools/VideoPlatform.Export.Tests/VideoPlatform.Export.Tests.csproj -c Release --logger 'console;verbosity=normal'
```

上述命令编译导出接口并运行四项数据库回归；缺少连接串时四项测试明确跳过，不能将进程退出码 0 当作数据库测试通过。

也可以通过专用入口复用服务器已有测试配置。前提是当前进程已配置 `VIDEO_PLATFORM_SSH_PASSWORD`，并安装 Python 和 Paramiko：

```powershell
$env:PYTHONIOENCODING = 'utf-8'
python tools/VideoPlatform.Export.Tests/run-postgres.py
```

该入口从 `/home/liteware/.config/video-platform/v2-test.json` 读取配置，复用 `tools/v2-environment.py` 的 SSH 隧道和环境构造函数，仅连接 `video_platform_v2_test`，使用动态本地端口。凭据只保留在内存和子进程环境中，不打印、不写入结果文件；不会调用共享环境脚本的初始化或部署入口。执行前后只读检查测试 schema，并生成 `artifacts/export-time-tests/export-time-postgres.trx`。

## 本次验证记录

2026-09-07 首轮：API Release 构建通过，0 警告、0 错误；当时未配置测试连接，四项数据库测试均跳过。

2026-09-07 补充真实回归：通过 `10.37.200.74` 的 SSH 隧道连接隔离库 `video_platform_v2_test`，PostgreSQL 版本为 `18.6 (Ubuntu 18.6-0ubuntu0.26.04.1)`，数据库会话时区为 `Etc/UTC`。测试记录时间为 `16:20:17` 至 `16:20:29`（`+08:00`），测试运行耗时约 11.8 秒。

| 测试 | 结果 |
| --- | --- |
| 创建入库及列表保持各偏移对应时刻 | 通过 |
| 无效时间范围拒绝且不写入任务 | 通过 |
| 重试保留时间范围并检查 Worker 占用 | 通过 |
| 下载按实际过期时刻判断、列表返回 UTC | 通过 |

原始 TRX 记录：执行 4 项，通过 4 项，失败 0 项，未执行 0 项。测试进程退出码为 0。执行后检查确认隔离库没有新增 `worker_test_*` schema 残留，本地 `.runtime` 测试目录为空。

测试进程退出时，既有 SSH 隧道脚本输出了 `ConnectionResetError / WinError 10054`，表示连接被对端重置。四项断言及随后独立执行的数据库清理检查均成功；本次没有修改共享隧道脚本。

本次执行的是本地生产导出接口源码配合真实远程 PostgreSQL 的回归，不是对已部署候选 1628 的线上验收。设备适配器使用模拟实现，下载使用测试字节文件，未验证设备实际录像导出。没有连接生产库 `video_platform_v2` 执行初始化，未部署，未修改共享 MediaService、Adapter 或 desktop。
