using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using VisiCore.StreamGateway;

var builder = WebApplication.CreateBuilder(args);
// HLS 票据位于路径中，禁止 ASP.NET 默认请求日志记录完整 URL。
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", _ => false);
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "VisiCore Stream Gateway");
}
var gatewayOptions = builder.Configuration.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();
var centerBaseUri = gatewayOptions.ValidateAndGetCenterBaseUri();
var trustedForwardedProxyAddresses = gatewayOptions.GetTrustedForwardedProxyAddresses();
if (trustedForwardedProxyAddresses.Count > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in trustedForwardedProxyAddresses)
        {
            options.KnownProxies.Add(address);
        }
    });
}
var mediaMtxOptions = builder.Configuration.GetSection("MediaMtx").Get<MediaMtxOptions>() ?? new MediaMtxOptions();
var mediaMtxBaseUri = mediaMtxOptions.ValidateAndGetApiBaseUri();
var mediaMtxHlsBaseUri = mediaMtxOptions.ValidateAndGetHlsBaseUri();
mediaMtxOptions.ValidatePublisherCredentials();
mediaMtxOptions.ValidateHlsReaderCredentials();
var liveTranscodeOptions = builder.Configuration.GetSection("LiveTranscode").Get<LiveTranscodeOptions>() ?? new LiveTranscodeOptions();
liveTranscodeOptions.Validate();

builder.Services.AddSingleton(gatewayOptions);
builder.Services.AddSingleton(mediaMtxOptions);
builder.Services.AddSingleton(liveTranscodeOptions);
builder.Services.AddSingleton<GatewayPathRegistry>();
builder.Services.AddSingleton<DpapiGatewayCredentialResolver>();
builder.Services.AddSingleton<ILiveTranscodeProcessFactory, FfmpegLiveTranscodeProcessFactory>();
builder.Services.AddSingleton<LiveTranscodeRelayManager>();
builder.Services.AddSingleton<ILiveTranscodeRelayManager>(provider => provider.GetRequiredService<LiveTranscodeRelayManager>());
builder.Services.AddSingleton<IStreamSessionLifecycle>(provider => provider.GetRequiredService<LiveTranscodeRelayManager>());
builder.Services.AddSingleton<StreamAuthorizationStore>();
builder.Services.AddHttpClient<HlsProxyService>(client =>
{
    client.BaseAddress = mediaMtxHlsBaseUri;
    client.Timeout = TimeSpan.FromSeconds(mediaMtxOptions.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{mediaMtxOptions.HlsReaderUsername}:{mediaMtxOptions.HlsReaderPassword}")));
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // MediaMTX 的 cookieCheck 重定向由代理校验后手动跟随，避免自动重定向清除内部 Basic。
        AllowAutoRedirect = false,
        UseCookies = false
    });
builder.Services.AddHttpClient<GatewayControlPlaneClient>(client =>
{
    client.BaseAddress = centerBaseUri;
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<IMediaMtxClient, MediaMtxClient>(client =>
{
    client.BaseAddress = mediaMtxBaseUri;
    client.Timeout = TimeSpan.FromSeconds(mediaMtxOptions.RequestTimeoutSeconds);
});
builder.Services.AddHostedService(provider => provider.GetRequiredService<LiveTranscodeRelayManager>());
builder.Services.AddHostedService<GatewayAssignmentWorker>();
builder.Services.AddHostedService<StreamAuthorizationMonitor>();
builder.Services.AddHealthChecks();

var app = builder.Build();
if (trustedForwardedProxyAddresses.Count > 0)
{
    app.UseForwardedHeaders();
}
app.Use(async (context, next) =>
{
    var isTrustedMediaMtxCallback =
        context.Request.Path.Equals("/internal/mediamtx/auth", StringComparison.OrdinalIgnoreCase) &&
        mediaMtxOptions.IsTrustedAuthCallbackAddress(context.Connection.RemoteIpAddress);
    if (!context.Request.IsHttps && !IsLoopback(context.Connection.RemoteIpAddress) && !isTrustedMediaMtxCallback)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { message = "流网关外部控制接口必须使用 HTTPS。" });
        return;
    }
    await next(context);
});

app.MapHealthChecks("/healthz");
app.MapPost("/internal/mediamtx/auth", async (
    MediaMtxAuthRequest request,
    HttpContext context,
    StreamAuthorizationStore authorizationStore,
    GatewayPathRegistry pathRegistry,
    ILiveTranscodeRelayManager liveTranscodeRelayManager,
    CancellationToken cancellationToken) =>
{
    if (!mediaMtxOptions.IsTrustedAuthCallbackAddress(context.Connection.RemoteIpAddress))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
    if (string.Equals(request.Action, "publish", StringComparison.OrdinalIgnoreCase))
    {
        var playbackPublisher = mediaMtxOptions.IsPublisherCredentialsValid(request.User, request.Password) &&
            string.Equals(request.Protocol, "rtsp", StringComparison.OrdinalIgnoreCase) &&
            IsPlaybackPublisherPath(request.Path);
        return playbackPublisher || liveTranscodeRelayManager.AuthorizePublisher(request)
            ? Results.NoContent()
            : Results.Unauthorized();
    }
    if (string.Equals(request.Action, "read", StringComparison.OrdinalIgnoreCase))
    {
        var internalRelayReader = liveTranscodeRelayManager.AuthorizeReader(request);
        var internalHlsReader =
            mediaMtxOptions.IsHlsReaderCredentialsValid(request.User, request.Password) &&
            string.Equals(request.Protocol, "hls", StringComparison.OrdinalIgnoreCase) &&
            mediaMtxOptions.IsTrustedHlsReaderAddress(request.Ip) &&
            request.Path is not null &&
            (pathRegistry.IsAvailable(request.Path) || IsPlaybackPublisherPath(request.Path));
        return internalRelayReader || internalHlsReader
            ? Results.NoContent()
            : Results.Unauthorized();
    }
    return await authorizationStore.AuthorizeAsync(request, cancellationToken)
        ? Results.NoContent()
        : Results.Unauthorized();
});

app.MapPost("/v1/control/sessions/{sessionId:guid}/revoke", async (
    Guid sessionId,
    StreamGatewayRevocationCommand command,
    HttpRequest request,
    StreamAuthorizationStore authorizationStore,
    CancellationToken cancellationToken) =>
{
    if (!gatewayOptions.IsCommandTokenValid(request.Headers["X-Stream-Gateway-Command-Token"].ToString()))
    {
        return Results.Unauthorized();
    }
    if (command.CommandId == Guid.Empty || command.SessionId != sessionId || string.IsNullOrWhiteSpace(command.Reason))
    {
        return Results.BadRequest(new { message = "撤销命令字段无效。" });
    }
    await authorizationStore.RevokeAsync(sessionId, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/v1/runtime", (
    HttpContext context,
    GatewayPathRegistry pathRegistry,
    StreamAuthorizationStore authorizationStore,
    ILiveTranscodeRelayManager liveTranscodeRelayManager) =>
    IsLoopback(context.Connection.RemoteIpAddress)
        ? Results.Ok(new
        {
            gatewayOptions.GatewayName,
            configuredPaths = pathRegistry.SnapshotNames().Count,
            activeSessions = authorizationStore.Count,
            liveTranscode = liveTranscodeRelayManager.Snapshot()
        })
        : Results.StatusCode(StatusCodes.Status403Forbidden));

app.MapMethods("/hls/{**mediaPath}", ["GET", "HEAD"], async (
    HttpContext context,
    HlsProxyService hlsProxyService) => await hlsProxyService.ProxyAsync(context));

await app.RunAsync();

static bool IsLoopback(IPAddress? address) =>
    address is not null &&
    (IPAddress.IsLoopback(address) ||
     (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));

static bool IsPlaybackPublisherPath(string? path) =>
    path is { Length: 41 } &&
    path.StartsWith("playback/", StringComparison.Ordinal) &&
    Guid.TryParseExact(path["playback/".Length..], "N", out _);

public sealed record StreamGatewayRevocationCommand(
    Guid CommandId,
    Guid SessionId,
    string Reason,
    DateTimeOffset RevokedAt);
