using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using VideoPlatform.Api;
using VideoPlatform.Api.Modules;
using VideoPlatform.Api.Realtime;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["PLATFORM_API_URL"] ?? "http://127.0.0.1:5082");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1073741824);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 1073741824);
builder.Services.AddPlatform(builder.Configuration);
builder.Services.AddAuthentication("platform").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("platform", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "__Secure-VideoPlatform-CSRF";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    options.ForwardLimit = 1;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) => await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("auth.rateLimited", "登录请求过于频繁，请一分钟后重试", context.HttpContext.TraceIdentifier), ct);
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(ApiSupport.Ip(context) ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSignalR(options => { options.EnableDetailedErrors = false; options.MaximumReceiveMessageSize = 16384; });
builder.Services.AddSingleton<EventConnections>();
builder.Services.AddHostedService<EventDispatcher>();
builder.Services.AddOpenApi("v2", options => options.AddDocumentTransformer((document, context, ct) =>
{
    document.Info.Title = "VisiCore（视枢）平台 API";
    document.Info.Description = "视枢视频管理与值守平台第二版接口。";
    return Task.CompletedTask;
}));
builder.Services.AddApiContractMetadata();
var app = builder.Build();
await Bootstrap.InitializeAsync(app.Services);

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception ex)
    {
        if (context.Response.HasStarted) throw;
        var (status, code, message) = ex switch
        {
            PlatformException problem => (problem.Status, problem.Code, problem.Message),
            AntiforgeryValidationException => (400, "auth.csrf", "请求验证已过期，请刷新页面后重试"),
            PostgresException postgres when postgres.SqlState == "23505" => (409, "data.duplicate", "名称、编号或记录已存在"),
            PostgresException postgres when postgres.SqlState == "23503" => (409, "data.referenced", "记录仍被其他数据引用，不能删除或关联不存在的记录"),
            PostgresException postgres when postgres.SqlState == "23514" => (400, "data.constraint", "提交的数据不符合约束"),
            BadHttpRequestException => (400, "request.invalid", "请求参数格式不正确"),
            JsonException => (400, "request.json", "请求数据格式不正确"),
            _ => (500, "server.error", "服务处理失败，请联系管理员并提供请求编号")
        };
        if (status >= 500) app.Logger.LogError(ex, "请求失败：{TraceId} {Path}", context.TraceIdentifier, context.Request.Path);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(code, message, context.TraceIdentifier));
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v2") &&
        (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method)))
    {
        var bearer = context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var desktopLogin = false;
        if (context.Request.Path == "/api/v2/auth/login" && context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Request.EnableBuffering();
            var request = await JsonSerializer.DeserializeAsync<LoginRequest>(context.Request.Body, JsonDefaults.Options, context.RequestAborted);
            context.Request.Body.Position = 0;
            desktopLogin = request?.ClientType == "desktop";
        }
        if (!bearer && !desktopLogin)
        {
            Rules.Require(context.Request.IsHttps, "浏览器写入请求必须通过 HTTPS", "auth.https");
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
    }
    await next();
});
app.UseAuthorization();
app.MapGet("/health", async (Database db) =>
{
    await db.OneAsync("select 1 as ok");
    return Results.Ok(new { status = "ok", service = "video-platform-api", version = "2.0.0" });
}).WithName("Health");
app.MapOpenApi("/api/v2/openapi/{documentName}.json");
app.MapAuthEndpoints();
app.MapDeviceEndpoints();
app.MapMediaEndpoints();
app.MapAlarmEndpoints();
app.MapExportEndpoints();
app.MapReleaseEndpoints();
app.MapAdministrationEndpoints();
app.MapHub<EventHub>("/hubs/v2/events").RequireAuthorization();
app.Run();

public partial class Program;
