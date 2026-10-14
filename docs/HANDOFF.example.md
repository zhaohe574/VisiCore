# VisiCore（视枢）系统运维与功能开发交接文档（模板）

> **说明**：本文件是**可提交的模板**，不含任何生产口令与真实网络拓扑。
> 实际交接文档请复制为 `HANDOFF.md`（已加入 `.gitignore`，不进入版本控制）后填写。
> 凭据一律通过环境变量或 `.secrets/deploy-credentials.txt` 提供，见
> [凭据模板](凭据模板.example.txt)。

> **文档更新时间**：<填写>
> **适用版本**：VisiCore 第二版（<版本号与要点>）
> **核心环境基线**：
> - 生产应用服务器：`<生产机 IP>`（SSH 用户：`<用户>`，**口令见未跟踪凭据文件**，工作根目录：`/opt/video-platform-v2`）
>   - 核心服务端口：Nginx `443/80`，平台 API `5082`，海康驱动 `5092`，中维驱动 `5093`，ZLM `18082`，RTSP推流 `18555`
>   - 当前生产运行 Release：`/opt/video-platform-v2/releases/<版本>`
> - 纳管资产概览（**详细拓扑见未跟踪的现场台账，不写入本文档**）：
>   - 核心交换机 1 台、华为 OLT 网元 1 台、华为 NeoSight 网管网关（机机账号**口令见未跟踪凭据文件**）
>   - 中央海康录像机、车间中维工位录像机、集团录像机若干
> - 终端网络与工具基线：
>   - 跨网段通信必须绑定本地源 IP：`<源 IP>`
>   - 本地 .NET SDK 路径：`%LOCALAPPDATA%\VideoPlatform\dotnet\dotnet.exe`
>
> **⚠️ 核心工作流守则（必须严格遵守）**：
> **「本地构建测试通过后一律自动部署至正式机」——所有代码修改、协议重构、数据库迁移与排障补丁，
> 在本地构建测试通过后，必须自动部署至正式生产机并执行线上实机服务与接口健康检查验收闭环！**
>
> **部署前置检查（每次必做）**：确认本次改动未引入任何口令/密钥/令牌字面量
> （`git grep` 与 CI secret-scan 均须为空），且凭据仅通过环境变量或未跟踪文件注入。

---

## 一、近期完成的核心任务（滑动窗口：保留最近 2~3 期）

<!-- 按会话批次记录：背景与诉求 → 架构决策 → 落地与验证。示例见下。 -->

### 历史里程碑回顾（早期会话压缩摘要）
- **[日期] 多工作区架构、资产事件流与全栈全景审计**：确立 Web 端资产运维与桌面端重度多路分屏边界，废除大杂烩页面并修复更新器 1603。
- **[日期] 车间网络切换三步业务落地闭环**：ONU 场景模板别名导入、点位多协议并发探测识别、工位 NVR 纳管、中央录像机批量添加在线 IPC、全网通道物理拓扑绑定。

### 会话批次 N（<日期>：ONU 断线与掉线智能定界引擎重构与虚警清零）
1. **根本原因排查与历史设计缺陷剖析**：
   - 此前代码将 `offlineCountsByPort >= 3` 强行置于判定链首位，只要同 PON 口累计离线 >= 3 台就无脑标记为"主干光纤中断 / PON口故障"，彻底架空了后面的 Dying Gasp 掉电检测；
   - 未考虑 GPON 物理现实（同一 PON 口可有数十台设备正常在线，绝非主干割接中断），且将历史陈旧离线与实时断线混为一谈，导致大量误报。
2. **多级定界架构决策与落地闭环**（详见 [光网诊断规则与回归矩阵](光网诊断规则与回归矩阵.md)）：
   - **第一级 Dying Gasp 优先**：同箱多台近时收到遗言判定"终端箱总电源掉电"，单台判定"设备掉电"；
   - **第二级 主干光缆中断严格定界**：仅当同 PON 端口全部设备失联（在线数 = 0 且总数 >= 2）才判定，同口有其它设备在线绝不误报；
   - **第三级 新增枚举 `splitter_broken`**：同 PON 口无掉电遗言、3 分钟窗口内并发失联多台；
   - **第四级 单台离线精准区分**：网管上报 Cause 2（LOSi 物理断光）定界为"分支光纤断纤"，其余为"离线待排查"。
3. **数据库迁移与线上实机验证**：执行 `030_splitter_broken_and_root_cause.sql` 扩展并纠偏历史数据；
   生产机实测虚警清零，分类结果与 OLT 端口监控卡片恢复真实状态。

### 会话批次 N+1（<日期>：组织架构拓扑全链路按编码标识 code 自然排序重构）
1. **背景与诉求**：车间、区域与工位单元此前按中文名称拼音首字排序，导致工位单元顺序错乱、车间编码不符合图纸规约。
2. **前后端协同改造**：
   - **后端 .NET**：重构 `AdministrationService.Organization.cs`，引入 `OrganizationCodeComparer` 实现自然分段排序；
   - **前端 Web-Admin**：`src/lib/organization.ts` 导出 `compareByCode`（`localeCompare` 的 `{ numeric: true, sensitivity: 'base' }`），
     拓扑树、树形表格、级联选择器统一对齐；
3. **测试与验证**：前端单元测试全绿；发布后实机核验 `/api/v2/organization` 排序严格递增。

---

## 二、当前核心改动与架构影响

- **批量运维工具（`tools/batch_add_cameras.py`）**：
  - 自动从生产库拉取现有通道 IP、ONU SN 和终端箱拓扑映射；
  - 根据设备品牌自动适配协议与凭据（**口令一律取自环境变量**
    `VIDEO_PLATFORM_IPC_PASSWORD_JOVISION` / `VIDEO_PLATFORM_IPC_PASSWORD_HIKVISION`，源码中不保留默认口令）；
  - 自动关联终端箱 ID、ONU ID、电口号及交换机标记（`SW-P{口号}` / `LAN{口号}`）；
  - 支持 `--limit` 步进执行与 `--dry-run` 预演模拟，内置 0.15s 请求防拥塞保护与 JSON 报告导出。
- **资产与拓扑数据库状态（`video_platform_v2`）**：设备、通道与网络绑定数量见现场台账，不写入本文档。

---

## 三、踩过的坑与技术要点（经验知识库）

### 1. 海康中央存储录像机（DS-A72124R）ISAPI 远程添加通道机制
- **现象**：向海康集中存储 NVR 下发新通道时，直接使用 POST 可能报 403/409（如果该数字通道号在设备底层已被分配为默认占位）。
- **根本原因**：海康录像机底层对已预分配通道号的配置要求通过 PUT 覆盖。
- **解法**：`NvrChannelManager` 采用"POST 创建，若报错 AlreadyExist/403/400 则自动回退为 PUT 更新"策略，通道名称与协议参数一次性穿透下发生效。

### 2. 交换机下挂设备的拓扑校验与微标呈现
- **现象**：下挂交换机上的摄像头如果直接指定电口号，后端 `ValidateNetworkBindingAsync` 会校验 `port <= 4` 并抛错；若传入 `isSwitchPort = true` 则要求 ONU 必须配置 `hasSubSwitch = true`。
- **根本原因**：系统设计严格区分直连 ONU 电口（LAN1~LAN4）与交换机扩展口（SW-P1~SW-P24），必须在 `onu_devices` 显式标记 `has_sub_switch`。
- **解法**：在录入交换机摄像头前，将挂载交换机的 ONU 在数据库中标记 `has_sub_switch = true, sub_switch_ports = 24`，前端即可自动获得专属 `SW-P{口号}` 标识并解除端口范围拦截。

### 3. 多通道写入并发与数据库锁竞争
- **现象**：并发批量添加通道时可能偶发 409 或 500。
- **根本原因**：`AdministrationService.AddCameraDirectAsync` 包含 `pg_advisory_xact_lock(72002030)` 事务级互斥锁，以保证录像机通道分配与网络电口分配绝对不冲突。
- **解法**：批量脚本采用单任务线性序列循环（配合 150ms 间隔），既避免了锁竞争超时，又保证海康 ISAPI 嵌入式 Web 容器连接稳定不丢包。

### 4. PostgreSQL UNION 查询的 ORDER BY 语法约束与自然排序搭配
- **现象**：在 `UNION ALL` 语句末尾使用 `order by type, coalesce(nullif(code, ''), name), id` 会被 Postgres 抛出 `0A000: invalid UNION/INTERSECT/EXCEPT ORDER BY clause` 异常。
- **根本原因**：Postgres 的 Set Operation 要求外部 ORDER BY 只能使用输出列名称，不能使用未在 SELECT 列表直接投影的复杂函数表达式。
- **解法**：SQL 层直接使用投影列名 `order by type, code, id`，业务层在 C# 内存中利用 `OrganizationCodeComparer` 进行精确的分段自然混排与空值优先级兜底。

### 5. Element Plus 树组件受控折叠状态与静默刷新的冲突根治
- **现象**：用户在组织架构拓扑树中折叠节点或点击"全部折叠"后，过一会儿树又自动全部展开。
- **根本原因**：`<el-tree default-expand-all>` 在接收到静默刷新引起的响应式对象引用改变时，Element Plus 重建内部 node 时会根据 `defaultExpandAll` 强制将每个节点重设为 `expanded = true`；同时 `useActivePage` 监听事件过宽导致频繁静默刷新。
- **解法**：移除 `default-expand-all`；维护 `expandedKeySet` 响应式集合，通过 `@node-expand` 和 `@node-collapse` 双向追踪；在 `watch(treeData)` 中结合 `nextTick` 精准恢复每个节点的 `expanded` 状态；同时收窄 `useActivePage` 监听事件。

---

## 四、活跃问题与当前状态（Status Check）

| 事项 | 类别 | 当前状态 | 现状说明与评估 |
| :--- | :--- | :---: | :--- |
| 组织架构折叠状态自动展开 Bug 修复 | 前端交互 | 🟢 全部闭环 | 移除静态 default-expand-all，引入 expandedKeySet 记忆追踪，实测折叠不再丢失 |
| 组织架构拓扑按编码自然排序 | 架构规范 | 🟢 全部闭环 | 车间、区域、单元前后端全链路统一按 code 自然混排 |
| ONU 断线与掉线定界重构 | 故障诊断 | 🟢 全部闭环 | 四级定界机制与新枚举落地，假主干中断虚警清零 |
| 排障诊断 UI 与智能 SOP 重构 | 前端交互 | 🟢 全部闭环 | 消除概念错位与自相矛盾提示，实现结构化 SOP 与工单复制 |
| 全网通道物理拓扑绑定 | 网络拓扑 | 🟢 全部闭环 | 物理绑定建立，终端箱、ONU、电口与交换机端口精准对应 |
| 探测离线点位排查 | 现场运维 | 🟡 待现场处理 | 网络不可达点位居多（未通电/未插网线/施工中），待现场工单排查 |

---

## 五、常用运维与开发命令速查

```bash
# 凭据一律先注入环境变量，禁止写入命令行历史或源码
export VIDEO_PLATFORM_SSH_PASSWORD='<从安全位置读取>'
export VIDEO_PLATFORM_ADMIN_PASSWORD='<从安全位置读取>'

# 1. 批量添加摄像头至中央录像机（支持演练与步进）
python tools/batch_add_cameras.py --dry-run
python tools/batch_add_cameras.py --limit 10
python tools/batch_add_cameras.py

# 2. 生产机远程一键部署
python tools/v2-deploy.py --bind-ip <源 IP> --switch

# 3. 生产机健康检查与服务日志
python tools/v2-remote-checks.py health
python tools/v2-remote-checks.py logs

# 4. 手动触发录像机通道全量同步
# POST https://<生产机>/api/v2/devices/<id>/sync (Header: Authorization: Bearer <token>)
```

---

## 六、凭据与信息安全规约

1. **任何口令、密钥、令牌不得写入源码、脚本、文档、提交信息或 Git 历史。**
2. 凭据只通过环境变量或未跟踪文件（`.secrets/deploy-credentials.txt`）注入；
   模板见 [凭据模板](凭据模板.example.txt)。
3. `HANDOFF.md` 与现场拓扑台账**不进入版本控制**，仅存在于受控内网工作区。
4. 提交前必须通过 secret-scan（`gitleaks` 与 CI 检查）；发现泄露立即轮换凭据，
   **轮换优先于历史清理**。
5. 设备口令在数据库中经 DataProtection 加密保存；密钥环目录权限须为 `0700`，
   SSL 私钥文件权限须为 `0600`。
