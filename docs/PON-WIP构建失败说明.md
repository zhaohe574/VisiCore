# PON WIP 构建失败详细说明

> 结论先行：**这不是 P0 修复引入的问题，也不是 P0 遗留的缺陷。**
> 它是一组**未提交的在建功能（WIP）**，其中测试文件已经写好并提交到工作区，
> 但它所依赖的生产代码改动**从未写入 `PonService.cs`**。
> 依赖关系：`PonService.cs`（已提交的旧版本）与 `PonDatabaseTests.cs`（未跟踪的新测试）不匹配。

---

## 一、现象

全解决方案构建报 **3 个编译错误**，全部集中在同一个文件：

```
tools/VideoPlatform.Worker.Tests/PonDatabaseTests.cs(40,56): error CS1739:
    "PonService" 的最佳重载没有名为 "timeProvider" 的参数
tools/VideoPlatform.Worker.Tests/PonDatabaseTests.cs(77,48): error CS1739:
    "PonService" 的最佳重载没有名为 "timeProvider" 的参数
tools/VideoPlatform.Worker.Tests/PonDatabaseTests.cs(87,52): error CS1739:
    "ListOnusAsync" 的最佳重载没有名为 "neDn" 的参数
```

`CS1739` 的含义是"命名参数不存在"，即**测试按名字传了一个参数，而被调用的方法没有这个参数**。

---

## 二、根因：调用方与被调用方来自两个不同时间点

### 测试期望的 API（`PonDatabaseTests.cs`，**未跟踪**）

| 行 | 代码 | 期望的签名 |
| :--- | :--- | :--- |
| 40 | `new PonService(h.Db, client, timeProvider: h.Clock)` | 第 3 个参数名 `timeProvider` |
| 77 | `new PonService(h.Db, client, timeProvider: h.Clock)` | 同上 |
| 87 | `service.ListOnusAsync(neDn: "olt-b", ponPort: "GPON 0/1/0")` | 支持 `neDn` 与 `ponPort` 命名参数 |

### 生产代码实际的 API（`PonService.cs`，**已提交**）

```csharp
// src/VideoPlatform.Infrastructure/Pon/PonService.cs:1
public sealed class PonService(Database db, EsightPonClient client, ILogger<PonService>? logger = null)
{
    // 第 3 个参数是 logger，不是 timeProvider
    // 且完全没有 TimeProvider / Clock 的概念

    public async Task<PagedResponse<OnuDeviceDto>> ListOnusAsync(
        string? search = null,
        string? status = null,
        long? terminalBoxId = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default, Actor? actor = null)
        // 没有 neDn，也没有 ponPort
}
```

**两边对不上。** 测试是按"改造完成后"的接口写的，而改造从未落到 `PonService.cs`。

---

## 三、这不是 P0 引入的 —— 三重证据

### 证据 1：文件状态（未跟踪）

```
$ git status --short tools/VideoPlatform.Worker.Tests/
?? tools/VideoPlatform.Worker.Tests/PonDatabaseTests.cs
?? tools/VideoPlatform.Worker.Tests/PonRuleTests.cs
```

`PonDatabaseTests.cs` 从未进入版本控制，不在我 P0 提交的任何 commit 中。

### 证据 2：`PonService.cs` 与历史版本逐字节相同

我在改写历史前做了备份，并对比过：

```
original commit: B9C402942A509853B4CCB225A0AD70A9
current:         B9C402942A509853B4CCB225A0AD70A9
=> IDENTICAL: rewrite did not alter PonService.cs
original 中 timeProvider 出现次数: 0
```

即：**`PonService.cs` 从来就没有 `timeProvider`**，不是被谁删掉的。

### 证据 3：在原始提交上同样失败

我把**改写前**的提交 `0d86a26` 取到独立 worktree 中构建，同样报这 3 个错误。
（P0 修复全部在 `93e7f6a` 及其后，`0d86a26` 不含任何我的改动。）

---

## 四、为什么"之前构建是通过的"

这是最容易误判的一点。时间线：

| 时间 | 事件 |
| :--- | :--- |
| 15:09–15:19 | `PonFaultClassifier.cs`、`PonDatabaseTests.cs`、`PonRuleTests.cs` 被创建（**在我本次会话之前**） |
| 会话开始 | 我第一次 `git status` 就看到这 3 个文件为 `??` |
| 我首次构建 | 报 3 个错误 —— 当时**就是失败的** |
| 我运行 Worker 测试 | 报告 35 通过 / 33 跳过 |

关键点：**"35 通过 / 33 跳过"那次运行是在我临时移开 WIP 测试文件之后**（我在验证 P0 改动是否干净时把 `Pon*.cs` 挪走过）。
所以并不存在"WIP 曾经构建成功、被我改坏了"的情况。

另外补充一个容易混淆的点：
`PonFaultClassifier.cs`（同样未跟踪）**是能编译的**，因为它是一个**纯函数**（`Evaluate(onus, now)` 把时间当参数传入）。
真正缺的不是它，而是 `PonService` 里"把 `TimeProvider` 接进来并调用它"的那部分改动。

---

## 五、这组 WIP 想做什么（从代码推断）

从 `PonFaultClassifier.cs` 和两个测试文件可以看出**一个设计良好的重构意图**：

```
现状（已提交）                      目标（WIP 想达到）
─────────────────────────────      ─────────────────────────────
PonService 内部用 DateTimeOffset    PonService 注入 TimeProvider
  .UtcNow 直接取当前时间              ↑ 让"防抖 30 秒""5 分钟新鲜度"
  → 时间不可控，无法写确定性测试         可在测试中快进

PonService 里散落判定逻辑            PonFaultClassifier 纯函数
  （与 DB/网管调用混在一起）           Evaluate / Transition / PortHealth
                                    → 可单测、已被 PonRuleTests 覆盖 24 条规则

ListOnusAsync(search, status, ...)  ListOnusAsync(neDn:, ponPort:)
                                    → 按网元精确过滤（多 OLT 证据不串联）
```

`PonRuleTests.cs` 里已经有 14 个 `[Fact]/[Theory]`（含 `GaspBoundary(15|16)`、
`FullPortOutageNeedsConcurrentDownTimes(180|181)` 等边界），
**全部只依赖纯函数，所以能独立编译运行**。而 `PonDatabaseTests.cs` 需要真实数据库
和改造后的 `PonService`，因此卡住。

配套的 `docs/光网诊断规则与回归矩阵.md` 也写明：
> 自动化覆盖：`PonRuleTests` 覆盖 R01-R03、R05-R21；`PonDatabaseTests` 覆盖 R11-R14、R22-R24。

也就是说，**测试和文档都已就位，只有 `PonService` 的接线没做**。

---

## 六、影响面

| 影响 | 说明 |
| :--- | :--- |
| 全解决方案构建 | **失败** —— `dotnet build VisiCore.slnx` 非 0 退出 |
| 运行时 | **不受影响**。这只是测试程序集编译失败，`PonService` 本身是好的 |
| 已部署服务 | **不受影响**。`src/` 下生产项目全部编译通过 |
| CI | 一旦接入 CI（P1 计划项），会立刻红灯 |
| 我的 P0 验证 | 已通过"临时移开该文件"隔离验证；P0 改动本身 0 警告 0 错误 |

---

## 七、三条处置路径

### 路径 A：补齐 `PonService` 接线（推荐，符合 WIP 原意）

改动集中在一个文件，且基础设施已具备：

1. `PonService` 主构造函数加 `TimeProvider` 参数
   （生产 DI 已有 `services.AddSingleton(TimeProvider.System)`，见 `JobRuntime.cs:38`，
   所以 DI 注册无需改动 —— `Bootstrap.cs:33` 的 `AddSingleton<PonService>()` 会自动解析）。

   ```csharp
   public sealed class PonService(Database db, EsightPonClient client,
       TimeProvider timeProvider, ILogger<PonService>? logger = null)
   ```

2. 把内部 `DateTimeOffset.UtcNow` 替换为 `timeProvider.GetUtcNow()`。

3. 接入 `PonFaultClassifier.Evaluate/Transition`，并把
   `stable_health_status / pending_health_status / pending_health_since` 落库
   （测试 R22 要求"进程重启后继续防抖"，即这 3 列必须持久化）。

4. `ListOnusAsync` 增加 `string? neDn = null, string? ponPort = null` 并加进 WHERE 条件。

> ⚠️ 注意：`ListOnusAsync` 已有一串可选参数，**新增参数必须加在 `ct` 之前**
> 或全部用具名调用，否则会静默改变现有调用方的位置参数含义。
> 当前仓库里的调用点需要一并检查。

### 路径 B：暂时移除该 WIP 测试（最快恢复绿灯）

把这 2 个未跟踪测试文件移到仓库外（例如 `.wip/`，加入 `.gitignore`），
构建即可恢复。适合"PON 改造要延后"的情况。
**但要注意**：`PonFaultClassifier.cs` 与 `docs/光网诊断规则与回归矩阵.md` 也属于同一批 WIP，
是否一并移出需要与该功能作者确认，避免把已完成的成果丢掉。

### 路径 C：保持现状（不推荐）

构建长期红灯，会掩盖后续真正的编译错误，且 CI 无法接入。
若选择这条，请在团队内明确"全解决方案构建当前不可作为门禁"。

---

## 八、需要你决定的事

我**没有改动这组 WIP**，理由：

1. 它是他人/前次会话的**在建工作**，改动方向（尤其 `ListOnusAsync` 的
   参数顺序与 `PonService` 是否该吃 `TimeProvider`）属于设计决策；
2. 补齐它需要**真实 PostgreSQL** 才能验证 `PonDatabaseTests`（本机没有，
   Docker 守护进程未启动），我无法给出"已验证"的结论；
3. 强行让它编译通过但不验证，等于制造"看着绿实际没跑"的假象 ——
   这正是本项目文档明确反对的做法。

**请告知选哪条路径。** 若选 A，我可以完成代码接线并保证编译与
`PonRuleTests` 通过，但 `PonDatabaseTests` 需要你提供隔离测试库
（`WORKER_TEST_DATABASE_URL`）才能实际验证。
