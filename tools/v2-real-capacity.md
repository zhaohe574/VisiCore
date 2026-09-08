# 真实媒体容量验收

工具仅通过现有 API 创建临时普通用户和媒体会话，不初始化数据库、不修改配额或设备、不操作云台、不修改录像。默认仅检查参数，必须显式 `--execute` 才连接服务器。本次交付只做实现与语法检查，未执行实机验收。

```powershell
python tools/v2-real-capacity.py --help
python tools/v2-real-capacity.py --duration 30 --public-url https://10.37.200.74
python tools/v2-real-capacity.py --execute --account-count 800 --duration 30 --public-url https://10.37.200.74 --device-id 1 --local-ca-file artifacts/video-platform.crt
python tools/v2-real-capacity.py --execute --live-only --duration 60 --live-channel-id 101 --local-ca-file artifacts/video-platform.crt
python tools/v2-real-capacity.py --execute --playback-only --duration 30 --playback-channel-id 108 --local-ca-file artifacts/video-platform.crt
```

第三条及后续命令会在获准环境实际运行。默认公共地址为候选 `https://10.37.200.74:8443`，正式入口可使用无端口 HTTPS 地址或显式 `:443`。默认选择设备通道 8；多个录像机同号时必须提供 `--device-id` 或准确的全局通道 ID，不会猜选设备。`--account-count` 默认 800、至少 100，全部经真实 API 建立并复查，不使用模拟数据；`--account-count 100` 可用于小规模准备。`--prepare-concurrency` 默认 8、最多 16，仅控制创建账号，并发登录固定 100，实时固定 100、回放固定 20。测量时长至少 30 秒。回放取本次开始前十分钟截止的 120 秒录像范围，先通过真实检索确认非空；起流过慢或 duration 过长导致录像播放结束，会如实失败，不循环录像掩盖限制。

复跑可附加 `--reuse-prefix capacity_a76d4ecafb25412dba94 --account-count 800`。工具先只读验证该命名空间内账号恰好为 `capacity_[20位小写十六进制]_[5位序号]`，序号从 `00000` 连续至 `account-count - 1`，全部已禁用、`roleIds=[]` 且 ID 唯一，并确认同编码角色已移除。任何不符均中止，清理也不修改这些未获验证的账号。验证后通过 API 为这些既有 ID 设置新的随机密码、启用并绑定本次精确通道范围角色；不新增账号，仍仅登录 100 个。报告 `accountPreparationMode` 区分 `create` 与 `reuse`，准备请求分别保留 `setup.user-create`、`setup.user-reuse` 分类，finally 禁用并解除本次全部复用账号的角色。

本机需 Python 3.10 以上、Paramiko、`VIDEO_PLATFORM_SSH_PASSWORD` 及已核对的 SSH 主机密钥。可用 `--known-hosts` 或 `--host-key-sha256` 指定信任。远程读取 production 配置内的 `adminPassword`、`zlmSecret` 和 `adapterKey`，凭据不传回本机、不进入报告。默认 `--config /home/liteware/.config/video-platform/v2-production.json`。内部自签证书使用 `--local-ca-file artifacts/video-platform.crt`：连接前解析本机公开证书，SFTP 上传至本次随机临时目录，远程 SSLContext 显式信任该证书，仍要求证书链、有效期和主机名校验，完成后删除临时证书。也可用互斥的 `--ca-file` 指定远程账号可读的受信任 CA 文件；两者均未指定时使用远程系统信任库。不修改系统证书或私钥权限，不支持跳过 TLS 验证。

使用随机前缀创建一个普通角色，仅授予所选模式的观看权限及通道读取权限，并设置精确通道范围；800 个独立随机密码账号全部绑定该角色，准备结束后通过真实用户分页接口复查数量、状态和角色，再选固定 100 个账号并发登录，逐个核对实际权限与可见通道。默认同时保持 100 路同通道 native 子码流和 20 个不同用户的独立 native 回放，HTTPS TS 连接读取真实字节并检查 188 字节 TS 同步结构。全连接就绪后开始计时，每个登录用户交替查询通道及本人信息。报告分开记录准备阶段、媒体起流、查询测量及清理耗时，各操作提供全部请求和成功请求的 P95、原始耗时、失败数量及脱敏错误分类，账号创建耗时不会混入查询 P95。

ZLM 只调用 `getMediaList`、`getMediaPlayerList`，按 vhost／app／stream 去除协议重复；要求实时唯一 RTSP 拉流上游、100 个新增 TS 连接及相对基线增加 100 的 readerCount。来源判定要求每个协议条目均有 `originTypeStr="pull"`，`originUrl` 可解析为具有主机名和有效端口的 `rtsp`／`rtsps` 地址，且所有条目的来源摘要、来源类型一致。数字 `originType` 仅作诊断，不靠编号猜协议，也不将 `3` 和 `4` 同时列入放行集合。缺失名称、缺失地址、RTP／RTSP 推流、FFmpeg 拉流、非 RTSP 地址或协议条目证据不一致均不通过。回放要求 20 个独立 stream，每个都有 TS 源和播放连接。工具不会把 ZLM 总连接数、平台会话数或其他协议条目当作达标证据；ZLM 版本不支持相应查询、现有观看者变化、子码流回退主码流、设备拒绝、SDK 错误、无数据或中途 EOF 均记录失败。适配器 health 必须明确 `simulated=false`。

2026-09-07 通过 SSH 只读核对服务器源码提交 `296dcebe8f3e81121e2c835cc9503ac5c25e2a1a`：`MediaOriginType` 中 `3=rtp_push`、`4=pull`。当时已有实时流的 TS、RTMP、fMP4、RTSP 四个协议条目实际均返回 `originType=4`、`originTypeStr="pull"`、来源协议 `rtsp` 和一致来源摘要；本次核对未创建媒体会话。JSON 的 `originEvidence` 逐条保存脱敏来源名称、协议、SHA256 和判定，`rtspPullVerified` 给出联合核对结果；不保存原始来源 URL、设备地址、用户名、密码或查询参数。中文报告同步列出来源名称、协议与判定。此证据解释并修复旧工具的编号误判，不追认前两轮未进入测量的容量结果为通过。

普通查询验收要求 P95 **严格小于 500 毫秒**，等于 500 或没有样本均不通过。查询线程及在途请求结束后，合并 `measurement` 阶段的 `query.*` 原始样本，使用最近秩法计算 P95，包含失败请求耗时；准备、登录、媒体和清理耗时不混入查询统计。JSON 明确记录 `observedQueryP95Ms`、`queryLatencyTargetMet`、`queryLatencyTargetMs`、`querySampleCount` 和 `queryFailureCount`，保留原始逐请求分类及各操作 P95。延迟达标不掩盖 HTTP 或设备错误，其他验收错误仍使整体失败。

ZLM 查询的 `data=null` 规范为无条目的空列表；TS 播放连接优先使用非空 `identifier`，兼容旧版 `id`，报告记录实际使用的 `playerIdentifierFields`。缺失、空白或重复标识仍失败，不以列表长度替代独立连接证明；不记录播放连接的 `params`、URL 或令牌。`getAllSession` 的字段与 `getMediaPlayerList` 不应混为同一次实测证据，本次兼容补丁仍需后续实机复跑核验。

finally 关闭本次 HTTPS 连接、停止媒体、注销已登录的 100 个普通用户，禁用本次全部 800 个账号（包括未登录的 700 个）并清空角色关联，最后移除随机角色并注销管理员；账号和审计记录保留。自定义 account-count 时清理覆盖该总数。响应丢失时按精确随机用户名重新查找账号，避免遗留已创建用户。清理后额外执行最多 60 秒的 ZLM 只读轮询：本次独立回放流必须在所有协议中消失；实时原先无源则要求源消失，原先有观看者则要求源、origin 摘要、读者数和连接 ID 恢复原基线，不关闭他人的流。JSON 的 `upstreamReleaseVerified` 和 `upstreamCleanup` 记录最终判定、基线、逐轮观察、残留回放标识及等待时间；超时或查询失败使整体失败，不以 HTTP DELETE 成功代替上游释放。JSON 和中文 Markdown 报告默认位于 `artifacts/v2-qa/real-capacity`，可用 `--report xxx.json` 指定。断线时远程作业仍负责超时收尾，报告保留随机标识、远程目录及清理结果，必须核查残留后再重跑。

负载进程运行在服务器内，通过候选／正式 HTTPS 入口传输，验证 API、TLS、媒体服务及真实设备的并发能力；不代表百台独立工作站的广域网带宽或硬件解码验收。错误正文只去除密码、令牌、内部密钥及 URL 查询凭据，保留服务端错误码、HTTP 状态、SDK 文本和 TraceId，不以重试成功覆盖原始失败。
