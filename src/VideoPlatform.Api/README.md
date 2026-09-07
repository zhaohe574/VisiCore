# 平台 API 首个切片

当前 API 提供最小认证闭环和适配服务快照读取：

- `GET /health`：健康检查，不需要密钥；
- `GET /api/devices`：读取设备摘要；
- `GET /api/channels`：读取通道状态。
- `POST /api/auth/login`、`POST /api/auth/logout`、`GET /api/auth/me`、`PUT /api/auth/profile`、`PUT /api/auth/password`：本地账号会话、本人资料和密码修改；注销会撤销当前令牌。
- `GET /api/workshops`、`GET /api/areas`、`GET /api/units`：业务树读取。
- `GET /api/channels/unassigned`、`PUT /api/channels/{id}/unit`：通道分配。
- `GET /api/alarms`、`POST /api/alarms/{id}/ack`：报警历史和确认。
- `POST /api/channels/{id}/ptz/start`、`POST /api/channels/{id}/ptz/stop`：云台控制。
- `POST /api/recordings/search`：按通道和时间范围查询录像文件，不暴露设备源地址。
- `POST /api/playback-sessions`、`GET /api/playback-sessions/{id}`、`POST /api/playback-sessions/{id}/control`、`DELETE /api/playback-sessions/{id}`：按当前用户权限建立和控制回放会话；建立和查询响应包含短期令牌保护的 RTSP、HTTP-FLV、HLS 播放地址。
- `POST /api/live-sessions`、`GET /api/live-sessions/{id}`、`DELETE /api/live-sessions/{id}`：按当前用户权限建立和释放实时预览会话，并返回短期媒体令牌地址。
- `GET /api/access-scopes/{user|role}/{id}`、`PUT /api/access-scopes/{user|role}/{id}`：管理员配置用户或角色的数据范围；范围类型为 `workshop`、`area`、`unit`、`channel`，未配置范围表示不限制。
- `POST /api/roles`、`PUT /api/roles/{id}`、`DELETE /api/roles/{id}`：管理员新增、编辑和物理删除角色；仍绑定账号时拒绝删除。
- `PUT /api/users/{id}`：管理员编辑账号用户名、姓名、手机号、状态和可选密码。
- `GET /api/desktop-releases`、`POST /api/desktop-releases`、`POST /api/desktop-releases/{id}/publish`、`POST /api/desktop-releases/{id}/revoke`：管理员管理桌面端安装包；`GET /api/desktop-releases/latest` 公开返回最新版本，`GET /api/desktop-releases/{id}/download` 公开下载已发布包并累计次数。
- `GET /api/system-stats`：读取当前服务器主机、负载、内存、磁盘、运行时间和 API 进程统计，需要 `statistics.read` 权限。
- `GET /api/device-stats`：读取当前录像机、通道在线状态和报警统计，需要 `statistics.read` 权限。

内部同步请求使用 `X-Platform-Key`；用户请求使用 `Authorization: Bearer <token>`。数据库连接由 `PLATFORM_DATABASE_URL` 配置，首次部署可用 `PLATFORM_BOOTSTRAP_USERNAME` 和 `PLATFORM_BOOTSTRAP_PASSWORD` 创建管理员。
