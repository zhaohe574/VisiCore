# MediaMTX 内置运行

自 `0.1.0` 后的单容器部署起，MediaMTX 已固定内置在 `visicore-core` 镜像中。平台不再支持在初始化页面填写外置 MediaMTX 地址，也不支持将外置 MediaMTX 接入核心 Docker 网络。

内置 MediaMTX 的 Control API、RTSP 和 HLS 分别绑定到核心容器的 `127.0.0.1:9997`、`127.0.0.1:8554` 和 `127.0.0.1:8888`。它们不会发布到宿主机或 Docker 网络；客户端只通过 StreamGateway 的受控代理访问 HLS。

不要额外运行 MediaMTX 容器，也不要公开上述端口。需要扩展媒体容量或采用独立媒体集群时，应作为新的部署架构重新设计，不与当前单容器安装混用。
