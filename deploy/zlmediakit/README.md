# ZLMediaKit 部署

运行 Gitee 源码构建，不依赖 Docker 镜像。源码和子模块位于服务器 `/home/liteware/ZLMediaKit`。

## 配置来源（重要）

生产配置**不由本目录的文件提供**，而是由 `deploy/v2-install.py` 生成
`/var/lib/video-platform/v2/zlm/config.ini`（权限 `0600`），其中会写入随机 `secret`，
并把 hook 指向 `127.0.0.1:5082` 且附带 `?key=<adapterKey>`。

- `config/config.ini.example` 仅是字段说明模板，**不可直接用于生产**。
- 不要手工编辑生产 `config.ini`：下次部署会被 `v2-install.py` 覆盖。

## 端口与访问控制

| 用途 | 端口 | 暴露范围 |
| --- | --- | --- |
| HTTP 播放（经 Nginx 反代） | 18082 | 仅 Nginx 与回环 |
| RTSP | 18555 | 受防火墙限制 |
| 管理 API | 18082 | `allow_ip_range=127.0.0.1` |
| RTMP / RTC / SRT / RTP | 关闭 | — |

管理接口、HTTP 播放与 RTSP 均使用非默认端口，仍需由 Nginx 和防火墙限制访问来源。

## 播放鉴权

平台 API 实现 `on_play` 令牌回调：hook URL **必须携带 `?key=`**，与
`HIK_ADAPTER_INTERNAL_KEY` 一致。缺少 `key` 时平台一律返回 `code:-1`，
表现为无法播放；**不要**通过关闭 `on_play` 来"修好"它，那会变成未授权可取流。

适配器通过 `ZLM_API_SECRET` 调用管理接口创建和删除上游代理；平台接口为客户端返回
短期 RTSP、HTTP-FLV 地址。

## 运维命令

```bash
systemctl status zlmediakit
# 读取真实 secret：见 /home/liteware/.config/video-platform/v2.env 的 ZLM_API_SECRET
curl "http://127.0.0.1:18082/index/api/getServerConfig?secret=${ZLM_API_SECRET}"
```

真实 `secret` 只存在于服务端环境文件与生成的 `config.ini` 中，不提交到仓库。
