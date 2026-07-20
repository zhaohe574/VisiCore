# 外置 MediaMTX 部署

视枢不负责启动、升级或暴露 MediaMTX。部署方必须自行维护其镜像、TLS 证书、网络和备份。核心容器只通过 Control API 动态登记受控拉流路径，并通过认证回调授予发布和内部 HLS 读取权限。

## 同机 Docker 模式

先执行 `docker compose up -d`，由 Compose 创建并标记 `visicore-network`，核心会停留在浏览器初始化引导态。MediaMTX 再以独立容器加入同一网络，不发布 Control API、RTSP 或 HLS 宿主机端口：

```powershell
docker compose up -d
docker run -d --name mediamtx --restart unless-stopped --network visicore-network `
  -v ${PWD}/deploy/linux/mediamtx.yml:/mediamtx.yml:ro `
  bluenviron/mediamtx:1.19.2
```

在浏览器初始化页面选择"同机 Docker 网络"，并填写 `http://mediamtx:9997/` 与 `http://mediamtx:8888/`。配置文件示例：

```yaml
api: yes
apiAddress: :9997
authMethod: http
authHTTPAddress: http://visicore-core:8080/internal/mediamtx/auth
authHTTPExclude:
  - action: api
  - action: metrics
  - action: pprof
rtsp: yes
rtspAddress: :8554
rtspTransports: [tcp]
hls: yes
hlsAddress: :8888
hlsVariant: lowLatency
pathDefaults:
  source: publisher
  sourceOnDemand: no
  maxReaders: 100
paths:
  all_others: {}
```

核心生成的 MediaMTX 发布与内部读取凭据不会写入该模板。MediaMTX 将所有授权请求回调至核心，核心再验证这些凭据和短期会话票据。

## 远程模式

远程 MediaMTX 的 Control API、HLS 和认证回调必须位于部署方提供的 HTTPS 反向代理之后。浏览器初始化页面只接受 HTTPS 地址，并按系统证书链校验 TLS。填写的 Control API 与 HLS 地址必须可由核心容器访问；反向代理还必须允许 MediaMTX 回调 `https://你的视枢域名/internal/mediamtx/auth`。

远程模式不得将 HTTP、私有自签名证书、用户名密码写入 URL，或把 MediaMTX API 直接暴露到公网。请在反向代理上限制源地址、配置访问认证，并确保只有核心地址可以访问 Control API。

## 验收

提交浏览器初始化前，分别从核心 Docker 网络测试 Control API 与 HLS 地址可达。引导态的 `http://127.0.0.1:8080/healthz` 证明 API 存活；提交并自动重启后，`http://127.0.0.1:8080/readyz` 与 `http://127.0.0.1:8080/stream/readyz` 才会验证完整运行态、流网关与外置 MediaMTX Control API。
