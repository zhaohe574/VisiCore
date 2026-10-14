# 海康多设备适配器 2.0.0

适配器运行于 .NET 10，真实设备需要 Linux x64 HCNetSDK。启动无需录像机环境凭据，平台通过内部接口注册设备。进程仅监听回环地址，所有接口（包括 `/health`）均要求 `X-Adapter-Key`。不修改录像机录像或设备配置。

## 候选环境

| 环境变量 | 配置 |
| --- | --- |
| `HIK_ADAPTER_INTERNAL_KEY` | 与平台共享的内部密钥，必填 |
| `HIK_ADAPTER_API_URL` | 候选配置 `http://127.0.0.1:5092`，只接受明确回环 IP |
| `HIK_SDK_DIR` | HCNetSDK 所在目录，默认 `/opt/video-platform/sdk/hikvision` |
| `LD_LIBRARY_PATH` | 包含 HCNetSDK 及其依赖的现有库路径 |
| `HIK_ADAPTER_DATA_ROOT` | 建议 `/var/lib/video-platform/v2/adapter` |
| `HIK_EXPORT_ROOT` | 必须与平台一致：`/var/lib/video-platform/v2/exports` |
| `HIK_ZLM_API_URL` | 候选隔离实例：`http://127.0.0.1:18082` |
| `HIK_ZLM_API_SECRET` | 现有 ZLMediaKit 管理密钥 |
| `HIK_ZLM_RTSP_URL` | 候选隔离实例：`rtsp://127.0.0.1:18555` |
| `HIK_FFMPEG_PATH` | 默认 `/usr/bin/ffmpeg`，需要 `libx264`、AAC 编码器 |
| `HIK_FFPROBE_PATH` | 默认 `/usr/bin/ffprobe` |
| `HIK_TRANSCODE_GLOBAL` | 共享视频兼容转码上限，默认 4 |
| `HIK_EXPORT_QUOTA_GB` | 导出目录配额，默认 100 |
| `HIK_ADAPTER_SIMULATOR` | 仅显式为 `1` 时启用模拟，生产环境留空 |

```powershell
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' build src/Hikvision.Adapter/Hikvision.Adapter.csproj -c Release
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' publish src/Hikvision.Adapter/Hikvision.Adapter.csproj -c Release -r linux-x64 --self-contained false
```

上述命令分别生成 Release 构建及 Linux 候选发布物。服务器安装 .NET 10 后运行 `dotnet Hikvision.Adapter.dll`。首次启动不加载原生 SDK，首个真实设备操作时才初始化。媒体发布使用 RTSP，候选 RTMP 11936 无需在适配器配置。

## 内部接口与平台协作

以 `docs/v2-接口协作契约.md` 为路由与 JSON 契约。接口已覆盖设备注册、更新、禁用、同步、实时、录像检索、回放查询与控制、云台、预置位、异步导出、事件读取及确认、重启核对。

- 设备 PUT 幂等；凭据变化先停止该设备的媒体、下载与云台，再创建独立设备实例。禁用或删除保留已持久化报警日志。
- `sync` 返回 `device` 和 `channels`，每条通道使用 `channel`。保留实机验证过的 ISAPI 完整通道枚举，不把 CVR 截断为 64 通道。
- `sessionId`、`jobId` 为平台传入的非空 GUID；相同标识重复创建返回原会话，不增加引用。绑定不同参数返回 409。媒体删除可重复调用。
- 实时使用 `app=live`，流名 `vp2_d{deviceId}_c{channel}_s{streamType}_{profile}`。回放使用 `app=playback`，流名另含平台会话 GUID。接口仅返回流名，不返回设备凭据。
- `codec` 为 `H264`／`H265` 或 FFprobe 识别的其他名称，实际转码时 `transcoded=true`。回放异步起流，探测完成前为 `state=starting, codec=unknown`，平台轮询 GET 取得最终状态。
- 浏览器输出检测 H.265 后使用 CPU `libx264`。相同设备、通道、码流与配置只占一个共享转码。H.264 直接输出；原生配置保留视频编码。
- 内部 FFprobe／FFmpeg 对 ZLM 的读取 URL 附带 `token={共享内部密钥}`。平台 `/internal/zlm/on-play?key=共享内部密钥` 应仅在回调来源可信、媒体读取来源为回环且 `params` 中 `token` 通过恒定时间比较匹配 AdapterKey 时放行该内部读取。普通客户端仍必须通过平台 grant 授权。外部响应绝不包含该 key，无令牌读取必须拒绝。
- `/internal/sessions` 返回 `{bootId,devices,live,playback,exports}`；媒体项含 `id,sessionId,deviceId,channel,profile,state,stream`。启动清理 `vp2_` 前缀遗留 ZLM 流。平台发现 `bootId` 变化时重新注册设备，对不存在的活动会话执行失败或重新创建策略。
- 适配器契约没有续租接口；平台负责会话授权、续租、撤权和清理，适配器按平台 DELETE 释放引用。共享上游仅在最后一个合法订阅释放后关闭。

## 报警与媒体可靠性

- SDK 进程生命周期集中管理，报警和异常回调按 `NET_DVR_ALARMER.lUserID` 路由。报警回调返回前复制间接通道、图片指针；回调内无磁盘或网络操作。
- 每设备报警缓冲最多 256 条、64 MiB，写入分段 NDJSON 并刷盘。队列溢出或磁盘异常写入健康状态及 `system.adapter_alarm`；未入队事件不会被宣称已持久化。
- `after` 为不透明游标，首次可省略或传 `0:0`。响应 `nextCursor` 必须原样传回；平台事务入库成功后 POST `events/ack`。确认持久化且拒绝倒退、越界或非记录边界，清理已确认旧分段。损坏行隔离后继续读取，异常巨行不会无限占用内存。
- 回放缓冲最多 256 条、16 MiB，不持续写入 PS 文件。4 MiB 高水位暂停 SDK 发送，1 MiB 低水位恢复；独立探测样本最多 2 MiB，单路托管压缩数据暂存峰值低于 40 MiB，SDK／FFmpeg 自身内存另计。任一边界溢出仍使会话失败，不任意丢弃压缩帧。
- 无解码窗口时该设备的 SDK OSD 时间返回错误 12，因此回放位置读取 FFmpeg 已输出的真实媒体时间，SDK 播放进度仅用于识别上游传输完成。FFmpeg 按媒体时间戳发布；倍速重建当前媒体位置的发布进程并缩放媒体时间戳，视频可保持原编码，变速音频使用 `atempo`。定位重新打开目标片段，不将百分比或墙钟推算冒充真实位置。支持 0.25、0.5、1、2、4，SDK 不支持则报错。
- 录像缺口为 `gap`，不会伪造画面或录像时间。继续操作进入下一有效片段。播放完成通知推进片段，故障清理会关闭原生回调源与 FFmpeg。
- 云台命令 10 秒无保活即停止，独立看门狗不等待录像查询。平台仍负责独占租约和每 2 秒保活。

## 导出

创建立即返回 `queued`／`running` 状态，全局并发 2、单设备并发 1；等候队列上限 1000。按时间调用 SDK 下载到平台导出目录，随后 FFprobe 探测、FFmpeg MP4 封装。视频使用 `copy`，仅 MP4 不兼容音频转换 AAC；校验成品视频编码。各检索片段独立封装，缺口及片段变化输出 ZIP 和中文时间清单，完成后删除下载临时文件。

文件路径拒绝越出平台导出根目录及符号链接、目录联接。下载、封装和打包期间持续检查磁盘与配额。取消中止下载及进程并清理临时文件；取消和失败状态持久化。进程重启把未完成任务标为失败，同一任务参数可再次 POST 重试。平台负责 7 天成品保留、数据库任务记录与下载授权。

内部导出 DELETE 等待下载任务、FFmpeg 进程、文件句柄和临时文件清理全部结束后才返回。取消确认期间 GET、重复 POST 和 `/internal/sessions` 保持原 `queued`／`running` 状态，不提前发布终态；重复 DELETE 等待同一个工作任务，不会提前释放全局或设备配额。HTTP 调用超时不代表取消完成，平台需继续查询确认后再清理或重试。

## 可重复验证

```powershell
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' run --project tools/VideoPlatform.Adapter.Tests/VideoPlatform.Adapter.Tests.csproj
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' run --project tools/VideoPlatform.Adapter.Tests/VideoPlatform.Adapter.Tests.csproj -- --media
```

第一条不依赖设备、ZLM 或 FFmpeg，覆盖内部 HTTP 契约、两设备同号通道、幂等引用、报警路由和恢复、回放缓冲与控制、云台保护、导出取消及目录限制。第二条额外需要配置 FFmpeg／FFprobe，实际生成测试录像，验证 MP4 原编码封装和分段 ZIP。测试进程退出时关闭自建 HTTP 服务，临时产物位于测试项目构建目录。

模拟模式中，注册任意两个不同设备 ID 后各得到 81 个相同编号通道，81 号离线，1 号支持模拟云台；每次查询有两个片段及中间缺口，同步会持久化一条归属该设备的模拟报警。模拟媒体会话只用于 API 集成，不发布伪装成实机的流。模拟导出会使用 FFmpeg 生成测试视频。

真实海康登录、全 81 通道同步、RTSP 鉴权钩子、H.265 兼容转码、真实 SDK 下载、云台及报警必须在候选环境单独验收。上述自动测试不计作实机通过，本实现未部署服务器。

## 独立复核与实机诊断

`Diagnostics/NativeAbiProbe.cpp` 直接包含厂商 Linux 头文件，可使用 Clang 的 `--target=x86_64-unknown-linux-gnu -fsyntax-only` 验证结构大小及字段偏移。已纠正旧声明中的 Linux `HWND` 大小及登录结构对齐，回放结构为 160 字节，登录结构为 416 字节；录像检索“全部类型／锁定状态”为 `0xff`，不是 `0xffffffff`。

设置 `HIK_ADAPTER_REVIEW_SELF_TEST=1` 运行适配器，可独立验证 ABI、40 MiB 压缩数据暂存、并发注册刷新、内部媒体令牌和导出持久化失败恢复；另设 `HIK_ADAPTER_REVIEW_MEDIA=1` 验证真实 FFmpeg 倍速时间戳、音频时长、H.265 保留与 H.264 转码。

独立复核还会故意延迟下载退出，验证排队取消、重复取消、GET 终态屏障、重试隔离、全局配额持有和临时文件清理顺序。启用媒体验证时共 7 项。测试项目校验厂商 Linux ABI 的 160 字节回放结构、关键字段偏移和 416 字节登录结构。

`Diagnostics/remote_recordings.py` 在服务器内读取设备凭据，仅输出脱敏的 SDK 状态和统计；默认仅检索，显式 `--media` 才创建临时回放及按时间下载，文件只写入平台导出目录并自动清理。2026-09-07 独立 SDK 诊断已验证正确参数下的 V50／V40／V30 检索、回放数据回调、暂停／继续／倍速命令、按时间下载 100% 及 H.264 编码。它不代表候选 API→SDK→ZLM→客户端整个流程已通过。
