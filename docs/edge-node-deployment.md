# 边缘节点部署

## 中心在线升级

中心使用单个 `visicore-core` 容器，升级会产生短暂维护窗口。首次启用前，管理员必须在中心宿主机安装独立的 `VisiCore Core Host Agent`，把 `VISICORE_UPGRADE_EXCHANGE_DIRECTORY` 指向 `/opt/visicore/upgrade-exchange`，并在 Compose 环境中设置 `VISICORE_CORE_UPGRADE_ENABLED=true`。业务容器不挂载 Docker Socket。

后台创建中心升级计划时，会先创建加密的 `upgrade-protection` 备份，再向 Host Agent 写入受签名的发行描述。Host Agent 只允许拉取 Docker Hub 的不可变 digest，固定执行 `docker compose pull/up`，等待 `/healthz`。目标镜像会在容器内执行 EF 数据库迁移；任一步失败时，Host Agent 会切回上一已知良好镜像并触发保护备份恢复。

VisiCore 边缘节点分为无特权的 `Edge Agent` 与可选的独立 `Edge Host Agent`。前者只访问中心控制面、设备网段和内存中的设备凭据；后者才可在验签成功后执行固定的升级或回滚动作。

## 共用安全边界

- 在管理后台创建一次性配对凭证。凭证仅显示一次，默认 15 分钟过期。
- 浏览器使用节点 RSA 公钥生成 `AgentEnvelope`。中心保存密文和元数据，不保存可恢复的设备账号或密码。
- Edge Agent 成功配对后会删除 bootstrap 文件中的注册码。不要把注册码写入镜像、Compose 环境变量、日志或版本库。
- 节点配置仅支持同步间隔与 ONVIF／Direct RTSP 开关。设备地址、设备凭据、MediaMTX 与厂商 SDK 不属于节点配置。

## 运行状态与资源策略

Windows 配置器、Linux 回环配置页和中心后台都会展示节点生命周期、最近心跳、CPU、内存、状态目录磁盘使用、有效资源策略和策略失败码。资源数据每 5 秒在本机刷新，并随既有节点心跳同步到中心；中心仅只读展示，不能远程修改节点宿主策略。

资源策略仅限制 `Edge Agent` 及其子进程，默认 CPU、内存均不限制，磁盘预警阈值为 85%。CPU 按宿主机总算力的百分比设置，内存按 MiB 设置；CPU 范围为 1-100%，内存下限为 256 MiB。Host Agent 始终不受该策略限制，确保诊断、策略更新、升级和恢复仍能执行。

Linux 保存资源策略后，Host Agent 只会原子更新固定的 `compose.resources.yaml` 并执行固定的 `docker compose up --detach --no-deps edge-node`，因此节点会短暂重建。Windows Host Agent 以 `LocalSystem` 为 `VisiCore Edge Agent` 服务进程维护命名 Job Object，CPU、内存限制动态生效，无需重启节点。未安装或不可用的 Host Agent 不会把策略标记为已生效。

## Docker 版

适用 Linux x64 与 ARM64 主机。Docker 部署不在 `.env`、镜像、Compose 或 bootstrap 文件中填写中心地址、注册码或发行密钥。正式版本使用 Docker Hub 的 `visicore/visicore-edge@sha256:...` 不可变引用，并通过仓库内受控 Compose 启动一个 `visicore-edge-node` 容器；镜像自动运行 Edge Agent 与本机配置服务。`.env` 只设置固定本机目录和配置页回环端口：

```bash
cp deploy/linux/edge-agent.env.example deploy/linux/.env
docker compose --env-file deploy/linux/.env -f deploy/linux/edge-agent.compose.yaml pull
docker compose --env-file deploy/linux/.env -f deploy/linux/edge-agent.compose.yaml up -d
```

Docker Hub Edge 镜像不包含独立 Linux Host Agent。首次 `docker compose up` 会用一次性初始化容器准备三个挂载目录的非特权写入权限；它不挂载 Docker Socket，也不保留运行权限。未单独从源码部署 Host Agent 时，节点仍可配对和执行受限的设备接入，但受控升级保持关闭。随后在宿主机浏览器访问 `http://127.0.0.1:18081`；远程维护通过 SSH 隧道访问该回环端口。

页面先测试中心 HTTPS 健康检查。已部署 Host Agent 时，页面还会测试其 Socket、PEM 和域名白名单。确认配对后，配置服务优先通过受限 Unix Socket 提交固定配置对象；Host Agent 原子写入状态目录并执行固定的 `docker compose up --detach --no-deps edge-node`。未部署 Host Agent 且配置通道目录为空时，配置服务只会原子写入本容器已挂载的 Agent 配置与一次性注册码，随后停止自身进程，由 Compose 重启无特权容器完成登记；它不能写宿主机配置、调用 Docker 或启用受控升级。若令牌或 Socket 仅部分缺失，页面会保留 `configuration_host_unavailable`，避免在 Host Agent 异常时绕过配置通道。单容器内的配置服务与 Edge Agent 均不挂载 Docker Socket。配对成功后 Agent 自动清除注册码。

可选的 Host Agent 默认不允许实际升级。只有在页面中导入 PEM、公钥标识、受信下载域名并明确开启“允许实际受控升级”后，才会启用发行下载、验签和固定 Compose 升级。Host Agent 仍只接受目标为当前 Linux 架构、未过期、RSA-PSS SHA-256 验签成功且哈希匹配的发行清单。

首次受控升级前必须存在上一已知良好制品：Docker 节点将从受保护的 `compose.release.yaml` 中导入 Docker Hub digest；Windows 节点须在初始化时保留并校验当前 MSI 基线。缺少基线时 Host Agent 会返回“需初始化”，不会尝试无法安全回退的升级。

## Windows 版

`VisiCore Edge Node.msi` 仅支持 Windows x64。安装包会注册两个服务：

- `VisiCore Edge Agent`：以 `LocalService` 运行，设备接入、配置、诊断与凭据信封只在此进程执行。
- `VisiCore Edge Host Agent`：独立宿主升级器，默认禁用执行权限；启用后仅能调用配置中的固定 `msiexec.exe`，且 MSI 必须由 Host Agent 从受信域名下载、验签清单并实算 SHA-256 后暂存。

MSI 只注册两个停止状态的服务。交互式安装完成后会打开“VisiCore 边缘节点配置”，开始菜单也保留入口。基础页先测试中心 HTTPS 健康检查，再确认写入一次性注册码、启动 Edge Agent 并等待实际登记成功；Windows 会用 DPAPI LocalMachine 加密节点私钥和工作令牌，配对成功后 bootstrap 文件会被删除。

高级页通过选择 PEM 文件导入发行信任根，配置公钥标识、受信下载域名、制品大小和超时。首次启用实际升级时，还必须选择当前正在运行版本对应的 MSI；配置工具会将它复制到受限的 `C:\ProgramData\VisiCore\EdgeHostAgent\known-good\edge-node.msi` 并实算 SHA-256，作为失败回退基线。PEM 和基线 MSI 都不会上传、写入发行清单或通过后台返回。Host Agent 默认不允许升级；只有明确启用且基线完整后才启动并允许执行受控升级。

## 升级与回滚

后台支持统一发行描述：同一 SemVer 版本同时包含中心镜像、Linux Docker Edge digest 和 Windows MSI。描述必须含目标平台、架构、不可变制品引用、SHA-256、最低 Host Agent 版本、数据库迁移模式、签发时间和有效期，并以按属性名排序、无尾随换行的紧凑 JSON 进行 RSA-PSS 验签。Host Agent 还会自行验签，不信任业务 Agent 的消息内容。

GitHub Release 作业要求配置 `EDGE_RELEASE_SIGNING_KEY_BASE64` 密钥和 `EDGE_RELEASE_SIGNING_KEY_ID` 仓库变量；缺少其中任一项会直接失败。Release 会附带 `visicore-edge-release-public-key.pem`，节点配置页导入该公钥并填写相同的标识后才能启用受控升级。清单内的 `signingPublicKeyId` 必须与后台登记值及节点 Host Agent 配置一致。

Docker 节点只执行固定 Compose `pull` 与 `up --detach --no-deps edge-node`，并复用本机受保护的 `.env` 与状态目录；Windows 节点由独立 Update Runner 执行固定参数的 MSI 安装，返回 `3010` 时只标记“等待人工重启”。回滚始终使用部署前的上一已知良好制品，且必须再次匹配 SHA-256 或 OCI digest。Host Agent 或安装器恢复失败时，先在后台停用节点，再通过本地受控部署恢复；不要删除节点状态目录来绕过身份或回滚保护。

## 非本期能力

边缘节点不部署 MediaMTX、客户端转流、厂商 SDK 或硬件容量扩展。它们必须作为独立媒体或插件部署，完成现场设备和容量验收后才能启用。
