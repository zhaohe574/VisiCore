# 独立后台服务交接说明

版本：2.0.0。目标框架：.NET 10。入口依次调用 `AddPlatform`、`AddPlatformJobs`、`Bootstrap.InitializeAsync`，再启动独立通用宿主。没有修改 API、共享数据库迁移或适配器文件，没有提交或部署。

## 任务与配置

| 任务 | 周期 | 行为 |
| --- | --- | --- |
| 录像机同步 | 30 秒 | 启用设备调用 `DeviceService.SyncAsync`，禁用设备只更新适配器注册状态。单设备失败不阻塞其他设备。 |
| 报警入库 | 2 秒 | 每设备每轮最多读取 100 条，数据库事务提交后才确认游标。 |
| 媒体与云台回收 | 5 秒 | 检查登录会话、账号状态、功能权限、数据范围、设备启用状态及租约。分批游标防止失败资源阻塞后续回收。 |
| 资源核对 | 启动时及每 30 秒 | 读取适配器资源清单，释放孤儿媒体与导出，关闭数据库中长期缺失的媒体资源。新媒体有 2 分钟启动宽限。 |
| 导出处理 | 2 秒 | 持久化抢占、提交、轮询、取消、超时与旧 Worker 租约接管。 |
| 保留期清理 | 5 分钟 | 按设置分批清理报警、审计和导出，尊重恢复事件外键。 |
| 心跳 | 5 秒 | 写入 `service_heartbeats`，名称为 `worker:{实例标识}`，附带版本、实例及各任务运行状态。 |

周期任务不重叠执行。超时或故障会延迟下一轮，不会累积无限后台任务。单资源失败按 4～60 秒退避，失败缓存最多保留 4096 项；需要安全停止的资源继续留在数据库中，恢复连接后再次清理。周期故障也有最长 60 秒退避。

沿用 `PlatformOptions` 配置：`PLATFORM_DATABASE_URL`、`PLATFORM_DATA_PATH`、`HIK_ADAPTER_API_URL`、`HIK_ADAPTER_INTERNAL_KEY`、`ZLM_API_URL`、`ZLM_API_SECRET`。空库初始化还需符合密码规则的 `PLATFORM_ADMIN_PASSWORD`。

部署时 API 与 Worker 必须使用相同数据库、数据目录和 DataProtection 密钥目录。适配器的 `HIK_EXPORT_ROOT` 必须与 `PlatformOptions.ExportsPath` 指向同一实际目录；必须显式对齐 `HIK_ADAPTER_API_URL`，当前基础设施与适配器的默认端口不同。Worker 不监听网络端口。

新增可选配置：

| 环境变量 | 默认值 | 范围 |
| --- | --- | --- |
| `WORKER_DEVICE_CONCURRENCY` | 4 | 1～32 |
| `WORKER_MAINTENANCE_CONCURRENCY` | 4 | 1～16 |
| `WORKER_EXPORT_TIMEOUT_MINUTES` | 360 | 1～2880 |

导出硬上限为全局 2、每设备 1，可通过数据库设置进一步降低；文件保留天数和空间配额读取 `settings`，默认 7 天、100 GB。剩余磁盘空间不足 10% 时停止启动新导出，并请求停止仍在执行的导出。适配器自身的 `HIK_EXPORT_QUOTA_GB` 应与平台设置一致。

## 报警语义

- 使用 `(device_id, source_id)` 去重。报警、历史、恢复关联、outbox 和 checkpoint 在同一事务中提交；提交失败不发送 ack。ack 响应丢失时，下一轮会再次确认已提交游标，包括空批次。
- 适配器已在日志写入前按通道拆分报警，每条日志有独立源 ID。Worker 逐条映射全局通道 ID，不根据 `payload.channels` 再拆分，避免重复报警。
- `payload` 保存适配器完整事件，包括源 `channel`、原始 SDK 报文和未知事件数据。未登记通道保存为 `channel_id = NULL`，按设备级可见性处理，但不会错误参与设备级恢复关联。
- 恢复事件保持独立 `new` 记录，通过 `recovery_of` 关联同设备、同通道、同事件类型的最近未恢复报警；只更新原事件 `recovered`、版本和历史，不改变原人工状态。
- 因恢复事件本身为 `new`，它会增加按人工状态统计的待处理数量。这是当前明确保留的产品行为，测试已覆盖；后续若要排除，需由 API 统计／列表共同决定，Worker 未自行过滤。
- 字段损坏、设备标识不符或非法图片会保存原始事件和中文隔离原因，作为 `system.adapter_record_invalid` 报警继续入库。批次或游标结构无效时保留原 checkpoint，不确认上游日志。

## 导出与清理语义

- 调度使用 PostgreSQL 事务 advisory lock（事务咨询锁）和 `FOR UPDATE SKIP LOCKED`（跳过已锁行），跨 Worker 共同预留容量；单任务外部调用使用独立连接持有会话锁。
- 数据库租约为 2 分钟。进程重启后接管过期租约，先 GET 适配任务；存在的任务只轮询，不重复 POST。请求响应丢失后仍使用同一 jobId。适配任务不存在时允许幂等提交，明确人工重试可重启适配器终态任务。
- 执行结果同时校验实例所有权与 `started_at`，旧执行不能覆盖重试后的新执行。每次提交及完成入库前重新验证登录、`export.create` 和通道范围。
- 连续 5 次执行失败转为 `failed`；进程重启不延长原任务绝对超时时间。取消未确认时保留 `worker_id` 并占用容量，直到 DELETE 确认终态或返回 404。
- `outputDirectory` 固定为导出根目录下的 `{jobId:D}` 子目录。结果必须是此任务目录内的绝对路径、非空 MP4／ZIP；检查目录边界、路径段、符号链接及目录联接。
- 完成、失败和取消记录按配置保留，失败任务不会立即消失。清理逐项验证目录树，只删除该 jobId 目录，保留其他任务、`.tasks` 和任何目录外文件。清理失败时保留数据库记录，下一轮重试。
- 只写事务内 outbox，由 API 负责事件分发。Worker 不删除 outbox，不提供 API、下载或前端功能。

## 必须跨模块确认的差异

以下事项不在允许修改范围内，已通过工作更新报告主代理。请在真实候选版本验收前核对其最终实现：

1. **适配器取消确认：**当前 `ExportService.Cancel` 先标记 `cancelled` 就返回，`Job.Work` 可能仍在下载、封装或执行清理。Worker 按既定终态契约释放容量，不能从现有响应判断写文件是否真正结束。需要适配器 DELETE 等待工作任务退出，且 GET 仅在工作完全退出后返回可清理终态；或由协作方明确增加结束标记，再接入 Worker。否则并发占位与文件清理存在竞争窗口。
2. **API 导出重试：**当前重试直接清空 `worker_id` 和租约。应在同一更新条件中要求 `worker_id IS NULL`，旧任务未确认停止时返回冲突，避免绕过正在取消任务的容量占位。Worker 已加入执行代次校验，但无法替代 API 的这个条件。
3. **媒体租约竞争：**Worker 在调用前复查失效条件，但现有 `StopPtzInternalAsync`／`StopInternalAsync` 没有预期租约条件。查询后、停止锁获取前发生续期或租约转交，可能停止新租约。彻底修复需要媒体服务在其已有锁内按预期认证会话和租约再次检查；Worker 未修改该文件。
4. **适配代理通信：**当前会话工具没有跨代理消息接口，无法直接向指定代理线程发送询问。本实现根据实际 `DeviceRegistry.SessionsAsync`、`LiveSessions.Snapshot`、`ExportService.Snapshot` 和 `AlarmJournal` 源码确认结构，未猜测接口。清单固定为 `{bootId,devices,live,playback,exports}`，媒体项读取 `sessionId`，导出项读取 `jobId` 和 `deviceId`。

## 构建与测试

本地工具链：`C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe`。

```powershell
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' build 'src/VideoPlatform.Worker/VideoPlatform.Worker.csproj'
$env:WORKER_TEST_DATABASE_URL='Host=127.0.0.1;Port=55485;Database=postgres;Username=postgres'
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' test 'tools/VideoPlatform.Worker.Tests/VideoPlatform.Worker.Tests.csproj'
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' publish 'src/VideoPlatform.Worker/VideoPlatform.Worker.csproj' -c Release -r linux-x64 --self-contained false -o 'src/VideoPlatform.Worker/bin/Release/publish-linux-x64'
```

上述连接串仅用于本机独立测试实例。测试为每个用例创建随机 `worker_test_*` schema，结束后只删除自己的 schema；未设置连接串时数据库用例会明确跳过，不能当作集成通过。测试文件仅写入 Worker 测试目录，测试数据库运行目录及 TRX 报告已在本目录的忽略文件中排除。

测试覆盖原子提交与 ack 顺序、重复事件、多设备同号通道、恢复人工状态、未登记通道、并发容量、锁行跳过、崩溃后的租约接管、提交响应丢失、取消竞争、执行代次、路径拒绝、自动失败上限、权限撤销、媒体／PTZ 超时、资源核对、失败资源公平回收、外键保留和心跳。

真实未覆盖：海康硬件调用、ZLMediaKit 播放连接实际断开、FFmpeg 真实录像内容、真实适配器进程与 Worker 联合崩溃、操作系统链接置换竞争、磁盘真正写满、Linux 运行环境、容量压测及 24 小时稳定性。PostgreSQL 为真实数据库，适配器调用使用按实际 JSON 结构构造的测试替身；Linux 发布构建成功不等于真实 Linux 部署验收。
