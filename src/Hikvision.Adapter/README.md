# 海康适配服务首个切片

该项目是 Linux x64 的 C# 海康适配服务。当前支持设备快照同步、PTZ 和最小回放会话：

- `NET_DVR_SetSDKInitCfg` 配置 Linux SDK 组件和 OpenSSL 路径；
- `NET_DVR_Login_V40` 登录 DS-A72124R；
- `POST /ptz`：由平台服务转发 PTZ 开始／停止命令，监听 `127.0.0.1:5090`。
- `POST /recordings/search`：按通道和起止时间检索录像文件元数据。
- `POST /playback/start`、`GET /playback/{id}`、`POST /playback/{id}/control`、`DELETE /playback/{id}`：建立、查询、控制和停止回放会话。建立时先检索并裁剪选定范围内的录像片段；响应包含完整时间线的当前时间、进度和 `timelineState`，片段空档会暂停等待。
- `POST /live/start`、`POST /live/stop`：按通道复用 ZLMediaKit RTSP 上游代理；优先使用子码流 `02`，设备不提供子码流时自动回退主码流 `01`；设备账号只在适配器内部使用。
- 读取设备摘要和 `NET_DVR_GET_IPPARACFG_V40` 通道状态；
- 通过设备 ISAPI 补齐 CVR 的完整通道清单（当前实机返回 81 条）；
- 可选建立一路 `NET_DVR_RealPlay_V40` 码流回调。
- 回放通过 `NET_DVR_PlayBackByTime_V40` 接收码流，并异步写入 `HIK_PLAYBACK_PATH` 指定目录的临时 `.ps` 文件；同时由系统 FFmpeg 转为 FLV，推送到 `HIK_ZLM_RTMP_URL`（默认 `rtmp://127.0.0.1:1935/playback`），供 ZLMediaKit 分发。
- 设置 `HIK_PLAYBACK_TIMELINE_SELF_TEST=1` 可运行时间线规范化断言检查，不需要设备凭据。

运行前设置 `LD_LIBRARY_PATH`、`HIK_SDK_DIR`、`HIK_DEVICE_IP`、`HIK_DEVICE_PORT`、`HIK_DEVICE_USER`、`HIK_DEVICE_PASSWORD`、`HIK_ADAPTER_INTERNAL_KEY`、`HIK_ZLM_API_URL` 和 `HIK_ZLM_API_SECRET`。预览测试使用 `HIK_PREVIEW_CHANNEL` 和 `HIK_PREVIEW_SECONDS`，密码不会输出。
