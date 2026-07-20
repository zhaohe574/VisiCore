# 边缘节点部署

VisiCore 边缘节点分为无特权的 `Edge Agent` 与可选的独立 `Edge Host Agent`。前者只访问中心控制面、设备网段和内存中的设备凭据；后者才可在验签成功后执行固定的升级或回滚动作。

## 共用安全边界

- 在管理后台创建一次性配对凭证。凭证仅显示一次，默认 15 分钟过期。
- 浏览器使用节点 RSA 公钥生成 `AgentEnvelope`。中心保存密文和元数据，不保存可恢复的设备账号或密码。
- Edge Agent 成功配对后会删除 bootstrap 文件中的注册码。不要把注册码写入镜像、Compose 环境变量、日志或版本库。
- 节点配置仅支持同步间隔与 ONVIF／Direct RTSP 开关。设备地址、设备凭据、MediaMTX 与厂商 SDK 不属于节点配置。

## Docker 版

适用 Linux x64 与 ARM64 主机。Docker 部署不在 `.env`、镜像、Compose 或 bootstrap 文件中填写中心地址、注册码或发行密钥。正式部署使用 Docker Hub 的 `visicore/visicore-edge:0.1.0`，并通过仓库内受控 Compose 启动一个 `visicore-edge-node` 容器；镜像自动运行 Edge Agent 与本机配置服务。`.env` 只设置固定本机目录和配置页回环端口：

```bash
cp deploy/linux/edge-agent.env.example deploy/linux/.env
docker compose --env-file deploy/linux/.env -f deploy/linux/edge-agent.compose.yaml pull
docker compose --env-file deploy/linux/.env -f deploy/linux/edge-agent.compose.yaml up -d
```

Docker Hub 镜像不包含独立 Linux Host Agent。未单独从源码部署 Host Agent 时，节点仍可配对和执行受限的设备接入，但受控升级保持关闭。随后在宿主机浏览器访问 `http://127.0.0.1:18081`；远程维护通过 SSH 隧道访问该回环端口。

页面先测试中心 HTTPS 健康检查。已部署 Host Agent 时，页面还会测试其 Socket、PEM 和域名白名单。确认配对后，配置服务仅通过受限 Unix Socket 提交固定配置对象；Host Agent 原子写入状态目录并执行固定的 `docker compose up --detach --no-deps edge-node`。单容器内的配置服务与 Edge Agent 均不挂载 Docker Socket。配对成功后 Agent 自动清除注册码。

可选的 Host Agent 默认不允许实际升级。只有在页面中导入 PEM、公钥标识、受信下载域名并明确开启“允许实际受控升级”后，才会启用发行下载、验签和固定 Compose 升级。Host Agent 仍只接受目标为当前 Linux 架构、未过期、RSA-PSS SHA-256 验签成功且哈希匹配的发行清单。

## Windows 版

`VisiCore Edge Node.msi` 仅支持 Windows x64。安装包会注册两个服务：

- `VisiCore Edge Agent`：以 `LocalService` 运行，设备接入、配置、诊断与凭据信封只在此进程执行。
- `VisiCore Edge Host Agent`：独立宿主升级器，默认禁用执行权限；启用后仅能调用配置中的固定 `msiexec.exe`，且 MSI 必须由 Host Agent 从受信域名下载、验签清单并实算 SHA-256 后暂存。

MSI 只注册两个停止状态的服务。交互式安装完成后会打开“VisiCore 边缘节点配置”，开始菜单也保留入口。基础页先测试中心 HTTPS 健康检查，再确认写入一次性注册码、启动 Edge Agent 并等待实际登记成功；Windows 会用 DPAPI LocalMachine 加密节点私钥和工作令牌，配对成功后 bootstrap 文件会被删除。

高级页通过选择 PEM 文件导入发行信任根，配置公钥标识、受信下载域名、制品大小和超时。PEM 仅复制到受限 `C:\ProgramData\VisiCore\EdgeHostAgent`，不会保存或显示其内容。Host Agent 默认不允许升级；只有明确启用后才启动并允许执行受控升级。

## 升级与回滚

后台只接受含有目标平台、架构、不可变 HTTPS 制品地址、SHA-256、最低 Host Agent 版本、签发时间和过期时间的签名发行清单。Host Agent 还会自行验签，不信任业务 Agent 的消息内容。

GitHub Release 作业要求配置 `EDGE_RELEASE_SIGNING_KEY_BASE64` 密钥和 `EDGE_RELEASE_SIGNING_KEY_ID` 仓库变量；缺少其中任一项会直接失败。Release 会附带 `visicore-edge-release-public-key.pem`，节点配置页导入该公钥并填写相同的标识后才能启用受控升级。清单内的 `signingPublicKeyId` 必须与后台登记值及节点 Host Agent 配置一致。

Docker 节点只执行固定 Compose `pull` 与 `up --detach --remove-orphans`；Windows 节点只执行固定参数的 MSI 安装。成功发布后才可创建回滚操作，且必须存在本机验证回执、暂存制品和再次匹配的 SHA-256。Host Agent 或安装器恢复失败时，先在后台停用节点，再通过本地受控部署恢复；不要删除节点状态目录来绕过身份或回滚保护。

## 非本期能力

边缘节点不部署 MediaMTX、客户端转流、厂商 SDK 或硬件容量扩展。它们必须作为独立媒体或插件部署，完成现场设备和容量验收后才能启用。
