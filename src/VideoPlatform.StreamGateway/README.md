# Windows 流网关

`VideoPlatform.StreamGateway` 与 Windows Device Worker、MediaMTX 部署在同一受控主机。它使用 Device Worker 令牌读取本机分配的录像机、摄像头路由和 DPAPI LocalMachine 密文，只在本机内存中解密设备凭据，并通过回环地址的 MediaMTX Control API 配置按需 RTSP 源。

## 运行链路

1. 路由同步 Worker 定期读取录像机分配，为每个摄像头生成 `live/{cameraId}/main|sub` 路径。
2. MediaMTX 仅在首个读者到达时连接 NVR，最后一个读者离开后按配置关闭上游。
3. 原生客户端访问中心签发的 `https://网关/hls/{会话}/{票据}/...` 地址；StreamGateway 先鉴权，再仅通过回环 HLS 地址代理给 MediaMTX。
4. 网关首次看到票据时调用中心原子消费，后续仅缓存票据哈希、租约和稳定的代理连接标识。
5. 中心主动撤销或巡检发现租约失效时，网关立即删除本地授权映射；下一次播放列表、分片或低延迟 Part 请求会在访问上游前收到 `401`。
6. 对 `LiveTranscode__ValidatedRecorderIds` 中的录像机，主码流改为 `internal/live-source/{cameraId}/main` 私有原始路径；首个已消费票据的主码流会话启动共享 FFmpeg，将 H.265 转为 H.264 后发布回原公开路径。最后一个会话退出并经过空闲宽限期后，进程树会被回收。

## 必需配置

- `Gateway__GatewayName`：必须与中心 `StreamGateway__GatewayName` 一致。
- `Gateway__CenterBaseUri`：生产环境必须为 HTTPS。
- `Gateway__CenterControlToken`：对应中心网关控制令牌。
- `Gateway__DeviceWorkerAccessToken`：对应当前主机的 Device Worker 令牌。
- `Gateway__CommandToken`：对应中心主动撤销命令令牌，且不得与控制令牌复用。
- `MediaMtx__ApiBaseUri`：推荐固定为 `http://127.0.0.1:9997/`，禁止暴露到业务网段。
- `MediaMtx__HlsBaseUri`：必须为 MediaMTX 本机回环 HLS 地址，例如 `http://127.0.0.1:8888/`。
- `MediaMtx__HlsReaderUsername` 与 `MediaMtx__HlsReaderPassword`：StreamGateway 访问回环 HLS 的独立内部凭据，不得与回放发布者凭据复用。
- `LiveTranscode__Enabled`：仅在真实设备主码流无法被 MediaMTX 稳定封装时启用。
- `LiveTranscode__FfmpegExecutablePath`：必须是存在的受控绝对路径，且 FFmpeg 包含 `libopenh264`。
- `LiveTranscode__MediaMtxRtspBaseUri`：必须与 MediaMTX 的回环 RTSP 监听完全一致，且不得包含凭据或查询参数。
- `LiveTranscode__ValidatedRecorderIds`：只允许填写已经完成真机主码流、撤销和容量验收的录像机编号。
- `LiveTranscode__MaxConcurrentRelays`：软件转码并发上限；未完成多路压测前保持为 `1`。

生产 HLS TLS 由 StreamGateway 的 Kestrel 入口终止，中心的 `StreamGateway__PublicBaseUri` 必须指向该入口。Docker Compose 使用 `deploy/linux/mediamtx.yml`，其 API 和 HLS 仅在 Compose 内网可达，业务网段不得直连 MediaMTX。

MediaMTX 的 `paths` 必须保留 `all_others: {}` 兜底规则，不能替换为 `paths: {}`；前者使动态发布路径进入回环鉴权，后者会在 RTSP 发布阶段将未预声明路径直接拒绝。回放发布仍要求独立凭据和严格格式的 `playback/<32位会话ID>`。实时兼容中继不携带设备或长期发布凭据，FFmpeg 使用每进程随机短期凭据读取精确内部 raw 路径并发布到精确 `live/{cameraId}/main`，停止或路由替换后立即失效；公开主码流路径同时设置 `overridePublisher: false`。HlsProxy 使用另一组独立内部凭据读取公开实时与回放 HLS。MediaMTX 的 `read` 和 `publish` 都不得加入 `authHTTPExclude`，该列表必须且只能包含 `api`、`metrics`、`pprof`。使用 `hlsVariant: lowLatency` 时，`hlsSegmentCount` 必须不少于 `7`，否则 MediaMTX 会在创建 HLS muxer 时失败。

MediaMTX v1.19.2 首次请求多码率播放列表时会返回同路径的 `cookieCheck=1` 重定向。HlsProxy 禁止 HttpClient 自动重定向与 Cookie 复用，只允许手动跟随一次同协议、同主机、同端口且同路径的重定向，并在第二跳重新附加内部 Basic 凭据；MediaMTX 随后使用会话查询参数关联播放列表与分片。跨来源、改路径或循环重定向统一返回 `502`。

HLS 票据位于客户端 URL 路径中，网关会关闭 ASP.NET Hosting 的完整请求 URL 日志。业务日志不得记录 `Request.Path`、票据、设备 RTSP URI、FFmpeg 完整参数或标准错误原文；实时中继日志只记录公开流键、退出码和标准错误字符数。

MediaMTX 服务必须先于 Windows 流网关启动。主动撤销与票据消费按会话串行化；MediaMTX v1.19.2 不提供 HLS 会话 kick API，因此即时控制点由网关代理承担，撤销请求不会再访问不存在的 MediaMTX 会话接口。

对于存在 POC／DTS 异常的 H.265 主码流，MediaMTX 的直接 HLS 封装可能失败；可启用 FFmpeg 完整解码并使用 `libopenh264` 生成 H.264 fMP4-HLS。该路径需要按目标设备完成单路、多路、断流恢复和容量验收。

## 当前边界

该服务实现实时预览控制链路，并可对严格格式的 `playback/<32位会话ID>` HLS 路径执行一次票据鉴权；中心返回的完整流键仍是最终判定，动态路径本身不构成授权。录像回放由 ONVIF 或已签名外部插件在边缘侧以 FFmpeg 中继，不能将设备原始数据直接配置为 MediaMTX 实时源。MediaMTX RTSP publisher 只允许在受控网络监听，必须经流网关 HTTP 鉴权验证独立发布者凭据和精确回放路径。
