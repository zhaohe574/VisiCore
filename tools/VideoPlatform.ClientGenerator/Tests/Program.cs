using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using VideoPlatform.Api;
using VideoPlatform.Api.Modules;
using VideoPlatform.Client;
using VideoPlatform.Client.Generated;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.ClientGenerator.Tests;

internal static class TestProgram
{
    private static int passed;
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "VisiCore.slnx"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("无法找到项目根目录。");
            Directory.SetCurrentDirectory(root.FullName);
            await VerifyMetadataAsync(args);
            await VerifyClientAsync();
            if (Environment.GetEnvironmentVariable("ADMIN_TEST_DATABASE_URL") is { Length: > 0 } databaseUrl)
                passed += await DatabaseRegression.RunAsync(databaseUrl);
            else Console.WriteLine("未设置 ADMIN_TEST_DATABASE_URL，本次未运行数据库空槽与后台心跳回归。");
            Console.WriteLine($"客户端与 OpenAPI 集成检查通过：{passed} 项。");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"客户端验证失败：{error}"); return 1; }
    }

    private static async Task VerifyMetadataAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PLATFORM_DATABASE_URL"] = "Host=127.0.0.1;Port=1;Database=metadata;Username=metadata;Timeout=1",
            ["PLATFORM_DATA_PATH"] = Path.GetFullPath("tools/VideoPlatform.ClientGenerator/.runtime/metadata"),
            ["HIK_ADAPTER_INTERNAL_KEY"] = "仅用于元数据检查"
        });
        builder.Services.AddPlatform(builder.Configuration);
        builder.Services.AddAuthentication("session").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("session", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();
        builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("login", limits => limits.PermitLimit = 20));
        builder.Services.AddOpenApi("v2");
        builder.Services.AddApiContractMetadata();
        await using var app = builder.Build();
        app.MapAuthEndpoints(); app.MapDeviceEndpoints(); app.MapMediaEndpoints(); app.MapAlarmEndpoints();
        app.MapExportEndpoints(); app.MapReleaseEndpoints(); app.MapAdministrationEndpoints();
        app.MapOpenApi("/api/v2/openapi/{documentName}.json");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            var document = (await http.GetFromJsonAsync<JsonObject>("/api/v2/openapi/v2.json"))!;
            JsonObject Operation(string route, string method) => document["paths"]![route]![method]!.AsObject();
            JsonObject Resolve(JsonNode node)
            {
                if (node["$ref"] is null) return node.AsObject();
                JsonNode result = document;
                foreach (var part in node["$ref"]!.ToString()[2..].Split('/')) result = result[part.Replace("~1", "/").Replace("~0", "~")]!;
                return result.AsObject();
            }
            var live = Resolve(Operation("/api/v2/live-sessions", "post")["responses"]!["200"]!["content"]!["application/json"]!["schema"]!);
            Check(live["properties"]?["channelId"] is not null && live["properties"]?["httpFlvUrl"] is not null, "正式媒体路由生成结构化响应");
            var latest = Operation("/api/v2/public/releases/latest", "get");
            var release = Resolve(latest["responses"]!["200"]!["content"]!["application/json"]!["schema"]!);
            Check(release["properties"]?["packages"] is not null && latest["responses"]?["204"] is not null, "最新版本包含 packages 和 204 响应");
            Check(latest["parameters"]!.AsArray().Any(p => p!["name"]!.ToString() == "packageType" && p["schema"]?["enum"]?.AsArray().Count == 2), "安装包筛选限定 msi 与 zip");
            Check(Operation("/api/v2/releases", "post")["requestBody"]?["content"]?["multipart/form-data"]?["schema"]?["properties"]?["file"]?["format"]?.ToString() == "binary", "上传文件被声明为 multipart 二进制");
            Check(Operation("/api/v2/exports", "post")["responses"]?["202"] is not null, "导出创建声明实际 202 状态");
            Check(Operation("/api/v2/devices/{id}", "put")["responses"]?["204"] is not null, "无正文写入声明实际 204 状态");
            var audit = Operation("/api/v2/audit", "get");
            Check(audit["parameters"]!.AsArray().Any(p => p!["name"]!.ToString() == "pageSize") && audit["parameters"]!.AsArray().Any(p => p!["name"]!.ToString() == "from"), "从 HttpContext 读取的查询参数完整公开");
            Check(Operation("/api/v2/users", "get")["responses"]?["403"]?["content"]?["application/json"] is not null, "中文错误响应具有结构化定义");
            Check(Operation("/api/v2/users", "post")["parameters"]!.AsArray().Any(p => p!["name"]!.ToString() == "X-CSRF-Token"), "浏览器 CSRF 请求头公开且不要求桌面传递");
            Check(document["components"]?["securitySchemes"]?["DesktopBearer"] is not null && document["components"]?["securitySchemes"]?["WebCookie"] is not null, "两种认证方式具有正式元数据");
            var output = args.Length == 2 && args[0] == "--metadata-output" ? args[1] : "tools/VideoPlatform.ClientGenerator/.runtime/metadata-openapi.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"实际 API 路由生成的 OpenAPI 已导出：{Path.GetFullPath(output)}");
        }
        finally { await app.StopAsync(); }
    }

    private static async Task VerifyClientAsync()
    {
        var handler = new StubHandler();
        string? token = "desktop-one";
        using var auth = new SessionTokenHandler(_ => ValueTask.FromResult<string?>(token)) { InnerHandler = handler };
        using var http = new HttpClient(auth) { BaseAddress = new Uri("https://platform.test") };
        var client = new VideoPlatformClient(http);
        handler.Response = () => Json("""
            {"id":"42","username":"reader","displayName":null,"phone":null,"status":"active","permissions":["channel.read"],"roleIds":["7"]}
            """);
        var user = await client.GetCurrentUserAsync();
        Check(user.Id == 42 && user.RoleIds.Single() == 7, "生成 C# 客户端读取数字字符串与可空字段");
        Check(handler.LastAuthorization == "Bearer desktop-one", "桌面请求读取当前令牌");
        token = "desktop-two";
        await client.GetCurrentUserAsync();
        Check(handler.LastAuthorization == "Bearer desktop-two", "令牌变化无需重建客户端");
        token = null;
        handler.Response = () => new(HttpStatusCode.NoContent);
        var absent = await client.GetLatestReleaseOrDefaultAsync("2.0.0", PackageType.Zip);
        Check(absent is null && handler.LastUri!.Query.Contains("packageType=zip"), "204 最新版本返回可空结果并正确编码包类型");
        handler.Response = () => Json("""
            {"id":5,"version":"2.0.0","fileName":"client.msi","sha256":"hash","fileSize":10,"releaseNotes":"说明","minimumVersion":null,"forceUpdate":false,"status":"published","publishedAt":"2026-09-07T00:00:00Z","downloadCount":0,"downloadUrl":"https://platform.test/package","createdAt":"2026-09-07T00:00:00Z","updateAvailable":true,
             "packages":[{"id":6,"version":"2.0.0","fileName":"client.zip","sha256":"hash","fileSize":20,"releaseNotes":"说明","minimumVersion":null,"forceUpdate":false,"status":"published","publishedAt":"2026-09-07T00:00:00Z","downloadCount":0,"downloadUrl":"https://platform.test/zip","createdAt":"2026-09-07T00:00:00Z"}]}
            """);
        var release = await client.GetLatestReleaseOrDefaultAsync();
        Check(release!.Packages.Single().FileName == "client.zip", "生成 C# 客户端反序列化同版本安装包数组");
        handler.Response = () => new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"code\":\"access.denied\",\"message\":\"当前账号没有此操作权限\",\"traceId\":\"trace-sdk\"}", Encoding.UTF8, "application/json") };
        try { await client.GetCurrentUserAsync(); throw new InvalidOperationException("未抛出预期错误"); }
        catch (ApiException<ErrorResponse> error) { Check(error.StatusCode == 403 && error.Result.Code == "access.denied" && error.Result.TraceId == "trace-sdk", "生成异常保留中文错误、状态码与追踪编号"); }
        handler.Response = () => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        using var file = await client.DownloadReleaseAsync(5);
        using var buffer = new MemoryStream();
        await file.Stream.CopyToAsync(buffer);
        Check(buffer.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4 }), "下载使用流响应并允许调用方释放资源");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await client.GetCurrentUserAsync(cancellation.Token); throw new InvalidOperationException("已取消请求仍执行"); }
        catch (OperationCanceledException) { Check(true, "生成客户端传递取消信号"); }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        passed++; Console.WriteLine($"通过：{message}");
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    public Func<HttpResponseMessage> Response { get; set; } = () => new(HttpStatusCode.NoContent);
    public string? LastAuthorization { get; private set; }
    public Uri? LastUri { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastAuthorization = request.Headers.Authorization?.ToString(); LastUri = request.RequestUri;
        return Task.FromResult(Response());
    }
}
