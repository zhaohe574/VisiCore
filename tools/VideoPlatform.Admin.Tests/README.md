# 管理模块集成测试

本项目直接编译管理路由模块及 API 公共支持文件，引用正式 Infrastructure。通过真实 PostgreSQL、真实 HTTP 和正式认证／授权／媒体撤权服务验证管理业务；海康适配器与 ZLMediaKit HTTP 使用显式模拟对象，不作为实机验收。

发布回归同时直接编译 `ReleaseEndpoints.cs`，配置与正式 API 一致的 1 GB 请求及 multipart 上限、Antiforgery 服务和唯一约束冲突到 HTTP 409 的映射，使用桌面 Bearer 认证。上传路由沿用其自身的 `DisableAntiforgery()`，本测试不代替正式 API 的浏览器 CSRF 验收。

## 运行方式

需要 .NET 10 和可创建 schema 的测试 PostgreSQL 账号。测试每次创建随机 `admin_test_*` schema，执行正式迁移和初始化，结束时清理该 schema。无需创建或修改现有业务表。

```powershell
$env:ADMIN_TEST_DATABASE_URL = 'Host=127.0.0.1;Port=55484;Database=postgres;Username=postgres'
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' run --project tools/VideoPlatform.Admin.Tests/VideoPlatform.Admin.Tests.csproj
```

连接串仅用于测试进程，不写入代码或配置。未提供连接串返回退出码 2，测试失败返回 1，全部通过返回 0。

也可在当前进程已提供 `VIDEO_PLATFORM_SSH_PASSWORD` 时，通过现有协调脚本创建全新隔离数据库并运行：

```powershell
$env:PYTHONIOENCODING = 'utf-8'
python tools/run-v2-regression.py --suite admin
```

脚本先从 `artifacts/nuget-feed` 还原并构建测试，再实际执行 `dotnet run --project tools/VideoPlatform.Admin.Tests/VideoPlatform.Admin.Tests.csproj -c Release --no-build --no-restore --no-launch-profile`。不能用 `dotnet test` 的空运行替代。

## 验证内容

- 显式用户／角色范围、全部通道、空范围拒绝、多角色合并和停用角色失效。
- 仅有 `area.read` 时的授权组织树、空组织授权、同号不同设备通道隔离。
- 组织 CRUD、父级检查、子级／通道／范围引用保护、局部组织维护与移动范围检查。
- 批量通道移动的原子性、逐条审计、实际失权订阅者撤销及合法共享流保留。
- `user.manage` 与 `role.manage` 的管理员身份校验、防止自授管理员／超范围权限、管理账号密码保护。
- 最后管理员的停用、锁定、移除角色、权限降级、角色编码修改、角色删除和并发降权保护。
- 角色与权限 CRUD、关联账号撤权及事务发件箱事件。
- 个人收藏排序去重、越权写入回滚和撤权后的读取过滤。
- 个人布局与共享轮巡 CRUD、发布权限、间隔和数量验证、共享通道范围过滤。
- 正式媒体撤权流程：媒体停止、PTZ 停止、导出取消，以及其他用户共享流保留。
- 单会话幂等撤销、保留其他登录、密码重置后全部登录失效。
- 全部系统配置边界、事务审计、真实仪表盘数值、宿主 CPU／内存／磁盘采集和后台降级状态。
- HTTP 管理路由、分页、中文标准错误、时间参数、字段空值／长度／状态／编号、搜索通配符转义及未暴露用户删除接口。
- 发布迁移 003 实际执行，同版本 ZIP 后接 MSI 上传均成功，数据库与存储文件大小、SHA256 和字节一致。
- 同版本同类型重复上传，包括 `.ZIP`、`.MSI` 大写扩展名，均返回标准 409，不增加记录、文件或成功审计。
- 草稿不公开、匿名上传拒绝、双包分别发布，公开 latest 默认优先 MSI，packages 返回两包，类型筛选和版本提示正确。
- 双包匿名完整下载、普通 Range、后缀 Range 返回正确字节及 Content-Range，越界返回 416。
- MSI 撤回后 latest 回退 ZIP，双包均撤回后 latest 返回 204、匿名下载拒绝，管理账号仍能读取保留包。

## 2026-09-07 双包真实回归

执行时间：`17:28:20` 至 `17:28:41`（`+08:00`）。使用新建的 `vp_regression_e2d5f2d73c2c4b60ba12ec4e7e723b3b` 隔离数据库，PostgreSQL `18.6`，会话时区 `Etc/UTC`。测试经 SSH 隧道运行，未连接生产库执行业务或初始化。

实际控制台检查共 26 项全部通过，退出码 0：原管理检查 20 项，发布检查 6 组。六组包含多条 HTTP、数据库、文件断言，不将每条断言额外计数。明确检查正式嵌入迁移 `003_release_packages.sql` 已执行；没有修改旧迁移或 API 源码。

发布测试使用 1024／2048 字节固定测试内容，执行真实 multipart 上传、文件存储、Npgsql 和下载响应；用于隔离验证版本／类型唯一性及发布协议，不声称测试内容为可安装 MSI，也不声称覆盖百兆包吞吐或 1 GB 边界。最终 MSI／ZIP 的实物校验由独立 PackageTests 负责。

证据位于 `artifacts/v2-regression/e2d5f2d73c2c4b60ba12ec4e7e723b3b/admin.log` 和同目录 `summary.json`。汇总核对逐条“通过”日志与程序最终 26 项声明一致，并确认数据库、运行目录已清理。测试进程已退出，API 构建占用已释放。

既有 SSH 隧道脚本退出时记录 `ConnectionResetError / WinError 10054`，未影响测试或随后数据库清理。未修改共享隧道脚本，未部署。本轮覆盖范围内未发现发布端点问题。

## 接入说明

主 API 调用 `app.MapAdministrationEndpoints()` 即可完成路由映射，方法内部通过现有基础依赖构造管理服务，无需额外注册。全局认证与 CSRF（跨站请求伪造）保护仍由正式 API 宿主负责；本测试使用桌面 Bearer 令牌。

所有管理写入在事务内重新验证会话、功能权限和管理员身份。授权事务锁为 `72002010`，审计与 `access.changed` 事件随业务事务提交，媒体撤权在提交后执行且不随客户端断开取消。若后续新增其他授权写入入口，应使用同一锁保证最后管理员检查的并发一致性。

`GET /system` 返回实际采集的宿主指标。操作系统无法采集某项指标时返回 `null`，不能以零值模拟；服务心跳超时返回 `offline`，任务存在异常时返回 `degraded`，尚无后台心跳时返回 `unknown`。共享布局响应仅包含当前账号可见通道，数据库保留完整共享定义。
