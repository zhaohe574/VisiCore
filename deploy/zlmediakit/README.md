# ZLMediaKit 部署

当前使用 Gitee 源码构建，不依赖 Docker 镜像。源码和子模块位于服务器 `/home/liteware/ZLMediaKit`，Release 产物安装到 `/opt/video-platform/zlm`。

管理接口、HTTP 播放、RTSP 和 RTC 端口使用非默认端口，正式开放前仍需由 Nginx 和防火墙限制访问来源。

平台 API 已实现 `on_play` 令牌回调。适配器通过 `HIK_ZLM_API_SECRET` 调用管理接口创建和删除海康 RTSP 上游代理；平台接口为客户端返回短期 RTSP、HTTP-FLV 和 HLS 地址。

服务安装后：

```bash
systemctl status zlmediakit
curl 'http://127.0.0.1:18080/index/api/getServerConfig?secret=<服务器上的 ZLM secret>'
```

服务器实际 `secret` 只保存在 `/opt/video-platform/zlm/config.ini` 和适配器环境文件中，不提交到仓库。
