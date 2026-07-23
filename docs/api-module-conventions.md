# API 模块约定

从 `0.1.3` 起，新增 API 业务域应在 `src/VisiCore.Api/Endpoints/` 中新增一个实现 `IApiEndpointModule` 的类。模块由应用自动发现并按阶段注册，因此不需要修改 `Program.cs`。

## 注册阶段

- `Bootstrap`：仅用于首次安装前也必须可用的端点，例如安装状态、初始化和恢复。
- `Configured`：平台完成配置后才注册的业务端点。

## 端点约束

- 新增或迁移的管理端点必须显式声明认证和最小系统权限，使用 `RequireSystemPermission` 或 `RequireSystemAdministrator`。
- 可预期的业务错误使用 `ApiProblems`，客户端据此读取稳定的 `code` 字段；未处理异常由全局异常处理器转换为相同的 Problem Details 结构。
- 变更类端点应通过 `WithAudit` 或保留已有的 `AuditService.WriteAsync` 记录操作人、资源类型和动作。迁移已有端点时，优先保留更完整的现有审计详情。
- 将请求和响应 DTO 放在所属业务域附近；不要为新增域在 `Program.cs` 添加端点注册或私有映射助手。

当前仍有未迁出的旧端点保留内联授权逻辑；这是渐进迁移边界，不代表全量端点已模块化。后续触及旧端点时，应在保持原有授权语义的前提下迁入模块并改用上述过滤器。

## 管理端对应约定

- 页面按业务域放入 `src/VisiCore.Admin/src/features/<domain>/`。
- 路径集中维护在 `src/VisiCore.Admin/src/routes.ts`，页面切换与浏览器历史必须由该映射驱动。
- 数据加载复用 `features/shared/use-resource.ts`，通用面板、表格和表单控件继续使用既有 `ui.tsx` 组件。
