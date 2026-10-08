# VisiCore（视枢）系统运维与功能开发交接文档

> **文档更新时间**：2026-10-08 12:30
> **适用版本**：VisiCore 第二版（`v2.1.6-202610081230`，PC-only Web、契约收敛与运行版本统一版）
> **核心环境基线**：
> - 生产应用服务器：`10.37.200.74`（SSH 用户：`liteware`，凭据仅通过受保护的运行环境注入，工作根目录：`/opt/video-platform-v2`）
>   - 核心服务端口：Nginx `443/80`，平台 API `5082`，海康驱动 `5092`，中维驱动 `5093`，ZLM `18082`，RTSP推流 `18555`
>   - 当前生产运行 Release：`/opt/video-platform-v2/releases/2.1.6-202610081230`
> - 全网核心网络设施与资产（全部真实在线纳管 🟢）：
>   - **核心交换机**：`10.37.4.1`（H3C S5560X-30F-EI，总控机房核心机架，纳管 28 个电/光口，承担全网各 VLAN 路由与汇聚）
>   - **华为 OLT 网元**：`10.37.5.3`（POL 全光网汇聚设备，接入 199 台在线 ONU 终端）
>   - **华为 NeoSight 网管**：`10.37.5.2:32102`（官方 OpenAPI 网关，机机专用账号 `visicore`）
>   - **DNS / 核心服务网元**：`132.147.160.51`（DNS 服务器，已挂载专属万兆光口及虚拟机 `132.147.160.52`）
>   - **全厂空间数字孪生**：路由 `/app/twin`，纳管全厂宏观总览与各车间 2.5D 微立体底图与光缆脉冲流光
>   - **网络终端箱（物理枢纽工作舱）**：路由 `/app/terminal-boxes`，纳管全厂 30+ 终端箱、箱内 ONU、光衰、下挂摄像头与工位电脑
>   - **集中存储与 NVR**：1 号机集中存储 `10.37.200.53:8000`（74/75 在线）、2 号机海康 NVR `10.37.200.51:8000`（14/19 在线）
>   - **中维录像机（4 台稳定推流）**：ID 20 (`10.37.24.50`)、ID 21 (`10.37.24.30`)、ID 22 (`10.37.24.31`)、ID 23 (`10.37.24.49`)
> - 终端网络与工具基线：
>   - 跨网段通信必须绑定本地源 IP：`10.37.6.210`
>   - 本地 .NET SDK 路径：`C:\Users\57477\AppData\Local\VideoPlatform\dotnet\dotnet.exe`
>
> **⚠️ 核心工作流守则（必须严格遵守）**：
> **「以后都自动部署到正式机」——所有代码修改、协议重构、数据库迁移与排障补丁，在本地构建测试通过后，必须一律自动部署至正式生产机（10.37.200.74）并执行线上实机服务与接口健康检查验收闭环！**

---

## 一、近期完成的核心任务（滑动窗口：保留最近 2~3 期）

### 历史里程碑回顾（早期会话压缩摘要）
- **[2026-10-06] 资产台账与大网段 IPAM 落地**：工位电脑台账、`/22` 大网段切片色块矩阵、全光 ONU 双 IP/Wi-Fi 配对、物理端口自由数量批量生成器。

### 会话批次 N-1 (2026-10-07 下午前期：全厂空间数字孪生 2.5D 微立体底座正式投产)
1. **数字孪生等轴测底座与资产聚合引擎**：上线 `026_digital_twin_map.sql`，内置全厂 5 大场景与 6 大光缆脉冲路径，CAD 智能轴网吸附算法精准定位制管车间 42 节点。
2. **调度大屏与实机上线**：大屏画布支持缩放拖拽标定、4 大微浮标组件、光衰健康联动，生产机发布 Release `2.1.5-20261007035455`。

### 会话批次 N (本轮会话 - 2026-10-07 下午：管理后台深度重构、录像机 IP 全自动抓取与物理一致性下发、终端箱一站式工作舱)
1. **侧边栏菜单深度精简与清晰分权（9 个分散入口收拢为 5+2 核心业务）**：
   - 合并「组织架构」入「组织与通道」(`/app/organization`)，左侧组织树支持节点就地增删改重命名；
   - 彻底取消全光网络的多 Tab 聚合设计：将「网络终端箱」(`/app/terminal-boxes`) 确立为独立的物理中枢核心入口；「全光网络」(`/app/pon`) 纯粹聚焦 OLT 拓扑与全网光衰健康大屏；
   - 路由平滑兼容：旧路由 `/app/organization-structure` 自动重定向。
2. **摄像头 IP 录像机全自动抓取与物理严格一致下发**：
   - **海康 ISAPI/SDK 自动提取 IP**：升级 `HikvisionDevice.Discovery.cs` 提取 `<sourceInputPortDescriptor.ipAddress>` 与 HCNetSDK IP，`DeviceService.SyncAsync` 自动持久化至 `channels.ip`，彻底免除人工逐一录入 IP；
   - **强制严格物理一致性**：通道编辑 IP/凭据/别名时，严格调用海康 ISAPI PUT `/ISAPI/ContentMgmt/InputProxy/channels/{channelId}`，物理录像机生效后方可入库，通信失败立即终止报错阻断。
3. **网络终端箱“一站式装备舱”工作台 (`TerminalBoxWorkbenchDrawer.vue`)**：
   - 点击任意终端箱卡片，滑出一体化抽屉，一览箱体归属、ONU 光衰、各端口挂载的摄像头与电脑；
   - 支持就地【+ 快速接入摄像头】（空闲端口自动填好、支持直接从录像机已有通道挑选中）与【+ 登记工位电脑】（空闲端口与车间 IP 池自动推荐）。
4. **四项运维简化能力闭环**：
   - 跨资产一键穿透（通道表/电脑表点击箱体/ONU 标签滑出工作舱，IP 池点击直达资产）；
   - 录像机未建档通道一键批量认领；
   - 工位电脑登记 IP 防冲突实时校验与空闲推荐；
   - 通道列表多选一键批量指定终端箱与 ONU。
5. **正式机自动化构建与部署验收**：
   - 前端 83 个测试全部通过，发布 Release `2.1.5-20261007070753`，正式机三大服务健康运行。

---

## 二、当前核心改动与架构影响

- **驱动与适配层 (`src/Hikvision.Adapter`)**：
  - `HikvisionDevice.Discovery.cs`：ISAPI XML 解析提取 `sourceInputPortDescriptor.ipAddress`，填入 `ChannelSummary.Ip`；
  - `Contracts.cs`：`ChannelSummary` 增加 `string? Ip = null`。
- **服务与业务层 (`src/VideoPlatform.Infrastructure` / `src/VideoPlatform.Contracts`)**：
  - `DeviceService.cs`：在 `SyncAsync` 中原子将 `channel.Ip` 写入 `channels(ip)`；
  - `INvrChannelManager.cs` & `NvrChannelManager.cs`：新增 `UpdateIpcChannelAsync`，下发 ISAPI PUT 更新数字通道；
  - `AdministrationService.Organization.cs`：重构 `UpdateChannelAsync`，完善部分更新增量语义（防止误清空凭据/网络绑定），触发物理 NVR 配置更新；新增 `BatchSetNetworkBindingsAsync` 与 `BatchClaimChannelsAsync`。
- **前端页面与组件 (`web-admin`)**：
  - `WorkspaceLayout.vue` & `router.ts`：更新侧边栏结构与路由重定向；
  - `TerminalBoxWorkbenchDrawer.vue`：实现一站式工作舱抽屉，集成箱内 ONU、光衰、摄像头与电脑穿透管理；
  - `panes/ChannelTablePane.vue` & `OrganizationPage.vue`：集成组织节点就地维护、批量认领与批量网络承载划拨；
  - `ComputerDialog.vue`：接入 IP 防冲突实时校验与车间空闲 IP 推荐。

---

## 三、踩过的坑与技术要点（经验知识库）

### 1. 通道增量更新（Partial Update）中的数据清空陷阱
- **现象**：在通道编辑弹窗修改名称或在终端箱解绑某摄像头时，摄像头的 IP、访问密码或所属网络终端箱被意外置空，甚至误触发对录像机的 ISAPI PUT 报错 502。
- **根本原因**：`UpdateChannelAsync` 之前将请求中未携带的属性（`null`）直接当作置空更新，且未区分“解绑网络”与“修改属性”。
- **规避与解法**：显式遵循增量更新原则——入参为 `null` 时严格保留 `before` 中的旧值；密码字段空字符串表示“不修改沿用旧密码”；网络解绑通过专用 `ClearNetworkBinding` 标识或专用批量网络接口操作；仅在 IP/密码/别名确实被用户改动时才向物理录像机触发 ISAPI PUT。

### 2. 海康 ISAPI 通道修改规范
- **现象**：向海康 NVR 修改已存在的数字通道时，直接调用 POST 会返回 `400/403 networkSourceIpAlreadyExist`。
- **根本原因**：海康 ISAPI 规范对已分配通道的属性更新必须使用 `PUT /ISAPI/ContentMgmt/InputProxy/channels/{channelId}`，而集合 POST 仅用于首次分配。
- **解法**：在 `NvrChannelManager` 中拆分 `AddIpcChannelAsync`（POST+PUT）与 `UpdateIpcChannelAsync`（直接 PUT），并在 XML 中严格转义 XML 字符。

### 3. 跨网段通信与自动化脚本的源 IP 绑定
- **现象**：Windows 宿主机直连生产机 `10.37.200.74` 时，Paramiko SSH 或 Socket 连接常报 `TimeoutError`。
- **规避**：自动化脚本必须显式通过 `s.bind(("10.37.6.210", 0))` 绑定本地源 IP 后再发起 TCP 连接。

---

## 四、活跃问题与当前状态（Status Check）

| 事项 | 类别 | 当前状态 | 现状说明与评估 |
| :--- | :--- | :---: | :--- |
| **管理后台重构与录像机 IP 全自动抓取** | 全栈架构 | 🟢 **已上线投产** | 已上线 Release `2.1.5-20261007070753`，侧边栏精简、终端箱工作舱、海康 IP 提取与严格物理下发均已就位 |
| **全厂空间数字孪生 2.5D 底图与大屏** | 数字孪生 | 🟢 **已上线投产** | 全厂总览与制管车间 42 节点吸附对齐完成，微立体沙盘与光缆流光稳定运行 |
| **现场物理录像机实机通道联动验证** | 现场验证 | 🟡 **具备验证条件** | 代码与接口均部署于生产机，待现场工程师在管理后台点击海康录像机【同步】实测自动提取 IP |

---

## 五、后续维护与下一步开发计划

1. **现场实机录像机同步实测**：
   - 在平台【录像机管理】点击 1 号机集中存储（`10.37.200.53`）与 2 号机海康 NVR（`10.37.200.51`）的【同步】，确认 70+ 路摄像头的 IP 地址一次性自动入库展现。
2. **热镀与螺旋车间终端箱现场建档**：
   - 施工人员可直接在【网络终端箱】看板通过工作舱抽屉，就地录入热镀与螺旋车间的终端箱、ONU 及下挂摄像头与电脑。
3. **数字孪生其余车间底图补充**：
   - 参照制管车间模式，为热镀与螺旋车间制作等轴测底图并在工作台完成轴网吸附。

---

## 六、常用运维与开发命令速查

```bash
# 1. 前端管理后台测试与打包
cd "d:\2026-09-08 VisiCore\web-admin"
npm test
npm run build

# 2. 后端核心服务编译与打包
cd "d:\2026-09-08 VisiCore"
& "$env:LOCALAPPDATA\VideoPlatform\dotnet\dotnet.exe" build VideoPlatform.sln -c Release
& "$env:LOCALAPPDATA\VideoPlatform\dotnet\dotnet.exe" publish src/VideoPlatform.Api/VideoPlatform.Api.csproj -c Release -r linux-x64 --self-contained true -o artifacts/v2/api

# 3. 生产机远程一键全量部署（秒级生效并热切换）
设置 `VIDEO_PLATFORM_SSH_PASSWORD` 环境变量后运行 `python tools/v2-deploy.py --switch`；跨网段主机按现场网络要求设置 `VIDEO_PLATFORM_BIND_IP`。

# 4. 生产机服务日志排查（SSH连接）
sudo journalctl -u video-platform-v2-api.service -n 50 --no-pager
```
