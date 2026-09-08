# 生成并接入第二版客户端

从真实 `/api/v2/openapi/v2.json` 生成 72 个操作的 Web TypeScript 类型和桌面 C# 客户端。C# 代码使用 NSwag 14.7.1 自动生成，Web 类型使用 openapi-typescript 7.13.0 自动生成，Web HTTP 调用使用 openapi-fetch 0.17.0。版本由 `package-lock.json` 固定。

## 服务端接入

在 API 组合根 `Build()` 前注册一次：

```csharp
builder.Services.AddOpenApi("v2");
builder.Services.AddApiContractMetadata();
```

`AddApiContractMetadata` 位于 `VideoPlatform.Api.ApiContractMetadata`。它集中补齐现有路由的结构化响应、分页与筛选参数、二进制下载、multipart 上传、标准错误、Cookie／Bearer 认证和 CSRF 请求头，无需修改各代理端点。所有响应类型与操作映射共用 `Contracts.ApiContractCatalog`，避免服务端与生成工具各维护一份 DTO。

`LatestReleaseDto` 包含 `packages: ReleaseDto[]`、`updateAvailable`，支持 `packageType=msi|zip`，服务端默认优先 MSI。尚未发布版本返回 204。

## 可重复生成

从仓库根目录执行：

```powershell
npm.cmd ci --prefix tools/VideoPlatform.ClientGenerator
node tools/VideoPlatform.ClientGenerator/generate.mjs --url http://127.0.0.1:5082/api/v2/openapi/v2.json --strict
```

生成器自动选择本机 VideoPlatform 的 .NET 10；也可通过 `DOTNET_EXE` 指定运行时路径。生产 URL 使用正常 TLS 证书验证，不提供跳过证书校验选项。

默认输出 `artifacts/v2/generated`，可使用 `--output` 修改。`--input` 支持从已保存的 OpenAPI JSON 离线重建。相同输入和锁定依赖重复生成的文件内容与 SHA-256 完全一致，不写入时间戳或随机路径。

VisiCore 的默认构建使用 `src/VideoPlatform.Client/Generated/VideoPlatformClient.g.cs` 中已入库的 C# 客户端。生成器输出后，用 `GeneratedClientDirectory` 指向新目录执行客户端验证，经审查再同步入库文件；不要让全新克隆依赖被忽略的 `artifacts`。

```powershell
node tools/VideoPlatform.ClientGenerator/generate.mjs --input artifacts/v2/generated/openapi.source.json --strict
node tools/VideoPlatform.ClientGenerator/verify.mjs
```

`--strict` 拒绝服务端缺失元数据的文档。普通模式可从正式 Contracts 导出缺失 schema 并生成 `metadata-report.md`，逐项列明缺失的响应、查询参数和上传定义。补充不会修改服务端文件；接入集中元数据后报告应为零项。

交付文件：`web/schema.d.ts`、`web/client.ts`、Web 包配置，`csharp/VideoPlatformClient.g.cs`，原始／SDK／NSwag 三份 OpenAPI、NSwag 配置、元数据报告及文件哈希清单 `manifest.json`。

OpenAPI 3.1 原文保留；NSwag 输入单独转为 3.0.3，保留可空字段和 .NET 数字字符串读取语义。Web 时间为 ISO 8601 字符串，C# 时间为 `DateTimeOffset`。Web 中 `int64` 使用 `number | string`，超出 JavaScript 安全整数范围时必须保留字符串。

## Web 接入

将生成的 `web` 目录作为本地包依赖，或由 Web 代理将两个生成文件接入现有工程；依赖 `openapi-fetch@0.17.0`。生成器本身不写 Web 工程。

```typescript
import { createVideoPlatformClient } from '@video-platform/client';

const { client } = createVideoPlatformClient({ baseUrl: window.location.origin });
const result = await client.GET('/api/v2/users', {
  params: { query: { page: 1, pageSize: 50, search: '' } },
});
if (result.error) throw new Error(result.error.message);
const users = result.data?.items ?? [];
```

`baseUrl` 只包含站点根地址，不能再次添加 `/api/v2`。客户端默认携带 Cookie，写请求自动获取 CSRF；登录前获取匿名令牌，登录成功、退出成功、修改密码成功后重新获取对应身份令牌。CSRF 获取并发合并，失败写请求不会自动重放。

上传使用导出的 `uploadRelease(client, {file, fileName, version, ...})`；下载传入 `parseAs: 'blob'`。普通调用返回 `{data,error,response}`，网络和取消错误正常抛出。

## 桌面接入

桌面项目引用 `src/VideoPlatform.Client/VideoPlatform.Client.csproj`。项目直接编译交付目录中的生成文件，不复制到桌面代理目录。自定义产物位置时通过 MSBuild 属性 `GeneratedClientDirectory` 指定 C# 目录。

```csharp
using VideoPlatform.Client;
using VideoPlatform.Client.Generated;

string? token = null;
using var handler = new SessionTokenHandler(_ => ValueTask.FromResult<string?>(token))
{
    InnerHandler = new HttpClientHandler()
};
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://10.37.200.74") };
var api = new VideoPlatformClient(http);
var login = await api.LoginAsync(new LoginRequest
{
    Username = username, Password = password, ClientType = "desktop", ClientVersion = "2.0.0"
}, cancellationToken: cancellationToken);
token = login.AccessToken;
var latest = await api.GetLatestReleaseOrDefaultAsync("2.0.0", PackageType.Msi, cancellationToken);
```

令牌由调用方使用 DPAPI 安全保存；`SessionTokenHandler` 每次请求读取最新令牌，客户端不关闭传入的 `HttpClient`。调用方按自己的生命周期处置 HTTP 客户端与下载返回的 `FileResponse`。所有异步方法支持 `CancellationToken`，建议使用具名 `cancellationToken:` 传入，避免与可选 CSRF 请求头参数混淆。

NSwag 对“200 有对象、204 无正文”的混合响应将 204 抛为 `ApiException`，因此升级器应使用 `GetLatestReleaseOrDefaultAsync`，无发布时返回 `null`。业务错误可捕获 `ApiException<ErrorResponse>`，包含状态码、中文 `Message`、`Code` 和 `TraceId`；认证中间件空错误响应捕获基础 `ApiException`。

会话刷新保持令牌稳定，只延长 8 小时登录有效期。媒体会话单独有效 3 分钟，调用方须在到期前调用生成的 renew 方法，不应依赖登录刷新延长媒体租约。

## 空槽与系统状态

普通布局 `ChannelIds` 为 `long?[]`，例如 `[null, 12, null, 25]`。Web 对应可空数组，C# 对应 `ICollection<long?>`。恢复窗口必须按数组索引处理，不能使用过滤或去重压缩数组。共享布局中不可访问通道返回同位置的 `null`。全空布局拒绝保存。轮巡 `kind=patrol` 仍拒绝空项，读取时过滤失权通道继续原有轮巡语义。现有 PostgreSQL `bigint[]` 支持空元素，无需数据库迁移。

系统 Worker 心跳只显示最新实例，统一命名“后台任务”，不显示随机实例标识或旧实例离线记录。`services[].reason` 提供最新实例的任务异常或心跳超时原因；状态恢复后为 `null`。

## 验证

`verify.mjs` 包含 Web 编译、非法调用类型拒绝、CSRF 生命周期、并发获取、上传／下载／取消、重复生成哈希一致性，以及正式 API 路由元数据和 C# 模拟 HTTP 调用检查。

真实数据库回归另需可创建 schema 的测试 PostgreSQL 连接串：

```powershell
$env:ADMIN_TEST_DATABASE_URL = 'Host=127.0.0.1;Port=55484;Database=postgres;Username=postgres'
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' run --project tools/VideoPlatform.ClientGenerator/Tests/VideoPlatform.ClientGenerator.Tests.csproj
```

测试创建并清理随机 `sdk_test_*` schema，通过生成 C# 客户端实际调用 HTTP 管理 API，验证空槽创建／读取／更新／删除、共享布局权限变化、全空和非法轮巡拒绝，以及旧 Worker 汇总和异常恢复。设备适配器使用明确的测试对象，不代表录像机实机验证。
