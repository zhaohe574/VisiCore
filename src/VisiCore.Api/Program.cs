using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;

static string? ReadCommandLineOption(string[] arguments, string optionName)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (string.Equals(arguments[index], optionName, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new InvalidOperationException($"{optionName} 必须提供非空路径。");
            }
            return arguments[index + 1];
        }
    }
    return null;
}

var configuredWebRootPath = ReadCommandLineOption(args, "--webroot") ?? Environment.GetEnvironmentVariable("WebRootPath");
if (!string.IsNullOrWhiteSpace(configuredWebRootPath) && !Path.IsPathFullyQualified(configuredWebRootPath))
{
    throw new InvalidOperationException("WebRootPath 必须是绝对路径。");
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = string.IsNullOrWhiteSpace(configuredWebRootPath) ? null : Path.GetFullPath(configuredWebRootPath)
});
var runAsWindowsService = args.Any(argument => string.Equals(argument, "--service", StringComparison.OrdinalIgnoreCase));
if (runAsWindowsService)
{
    builder.Host.UseWindowsService(options => options.ServiceName = "VisiCoreCenterApi");
}
else
{
    // 直接从终端启动时不使用 Event Log，避免普通本地开发账户因事件源权限导致宿主崩溃。
    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
    builder.Logging.AddDebug();
}
var connectionString = builder.Configuration.GetConnectionString("Platform");
var healthChecks = builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.AddPolicy("stream-session-create", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirstValue(ClaimTypes.Sid) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 128,
                TokensPerPeriod = 64,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    rateLimiterOptions.AddPolicy("recording-search-create", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirstValue(ClaimTypes.Sid) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 12,
                TokensPerPeriod = 6,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    rateLimiterOptions.AddPolicy("playback-session-create", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirstValue(ClaimTypes.Sid) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 12,
                TokensPerPeriod = 6,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    rateLimiterOptions.AddPolicy("playback-transport-control", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirstValue(ClaimTypes.Sid) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 48,
                TokensPerPeriod = 24,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    rateLimiterOptions.AddPolicy("account-password-change", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue(ClaimTypes.Sid) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    rateLimiterOptions.AddPolicy("notification-channel-test", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue(ClaimTypes.Sid) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    rateLimiterOptions.AddPolicy("public-offline-devices", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueLimit = 0
            }));
});

if (string.IsNullOrWhiteSpace(connectionString))
{
    healthChecks.AddCheck<DatabaseConfigurationMissingHealthCheck>("platform_database", HealthStatus.Unhealthy);
    builder.Services.AddSingleton<DatabaseConfigurationMissing>();
}
else
{
    builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
    builder.Services.AddSingleton<NotificationWebhookAddressProtector>();
    healthChecks.AddCheck<DatabaseReadinessHealthCheck>("platform_database", HealthStatus.Unhealthy);
    builder.Services.AddSingleton<Argon2PasswordHasher>();
    builder.Services.AddScoped<AccountPasswordService>();
    builder.Services.AddScoped<PlatformAccessService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddScoped<DevicePluginService>();
    builder.Services.Configure<DevicePluginTrustOptions>(builder.Configuration.GetSection("DevicePlugins"));
    builder.Services.AddScoped<DevicePluginCapabilityService>();
    builder.Services.AddScoped<DeviceWorkerAccessService>();
    builder.Services.AddScoped<DeviceWorkerSyncService>();
    builder.Services.AddScoped<LinuxEdgeAgentControlService>();
    builder.Services.AddScoped<PublicOfflineDeviceService>();
    var clockMonitoringOptions = builder.Configuration.GetSection("ClockMonitoring").Get<ClockMonitoringOptions>() ?? new ClockMonitoringOptions();
    clockMonitoringOptions.Validate();
    builder.Services.Configure<ClockMonitoringOptions>(builder.Configuration.GetSection("ClockMonitoring"));
    var edgeOperationReadinessOptions = builder.Configuration.GetSection("EdgeOperationReadiness").Get<EdgeOperationReadinessOptions>() ?? new EdgeOperationReadinessOptions();
    edgeOperationReadinessOptions.Validate();
    builder.Services.Configure<EdgeOperationReadinessOptions>(builder.Configuration.GetSection("EdgeOperationReadiness"));
    builder.Services.AddScoped<EdgeCommandControlService>();
    builder.Services.AddScoped<DeviceWorkerOperationStatusService>();
    builder.Services.AddScoped<EdgeOperationReadinessService>();
    builder.Services.AddScoped<PluginOperationEligibilityService>();
    builder.Services.AddScoped<RecorderOperationRoutingService>();
    builder.Services.AddScoped<RecordingSearchService>();
    builder.Services.AddScoped<PlaybackExportService>();
    builder.Services.Configure<ExportArtifactStorageOptions>(builder.Configuration.GetSection("ExportArtifactStorage"));
    builder.Services.AddSingleton<LocalExportArtifactStore>();
    builder.Services.AddScoped<PlaybackExportArtifactService>();
    builder.Services.AddScoped<PtzControlService>();
    builder.Services.Configure<StreamGatewayOptions>(builder.Configuration.GetSection("StreamGateway"));
    builder.Services.AddHttpClient(StreamGatewayRevocationWorker.HttpClientName, client =>
        client.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddScoped<StreamSessionOrchestrator>();
    builder.Services.AddHostedService<PlatformBootstrapService>();
    builder.Services.AddHostedService<StreamSessionCleanupService>();
    builder.Services.AddHostedService<StreamGatewayRevocationWorker>();
    builder.Services.AddHostedService<RecordingSearchCleanupService>();
    builder.Services.AddHostedService<ExportArtifactCleanupService>();
    builder.Services.AddHostedService<RecorderClockObservationCleanupService>();
    builder.Services
        .AddAuthentication(PlatformSessionAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, PlatformSessionAuthenticationHandler>(PlatformSessionAuthenticationHandler.SchemeName, _ => { });
    builder.Services.AddAuthorization();
}

var app = builder.Build();
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (feature?.Error is not null)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("VisiCore.Api.ExceptionHandler");
            logger.LogError(feature.Error, "中心 API 请求处理失败。");
        }

        var statusCode = feature?.Error is BadHttpRequestException
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        var title = statusCode == StatusCodes.Status400BadRequest ? "请求格式无效" : "请求处理失败";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title,
            status = statusCode
        }));
    });
});
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            var headers = context.Context.Response.Headers;
            headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Cache-Control"] = string.Equals(Path.GetFileName(context.File.PhysicalPath), "index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-store"
                : "public, max-age=31536000, immutable";
        }
    });
}
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = _ => true });

if (string.IsNullOrWhiteSpace(connectionString))
{
    app.MapMethods("/api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], () => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "平台数据库尚未配置",
        detail: "配置 ConnectionStrings__Platform 后才能启用业务 API。"));
}
else
{
    app.UseAuthentication();
    app.Use(async (context, next) =>
    {
        var passwordChangeRequired = context.User.HasClaim(
            PlatformSessionAuthenticationHandler.PasswordChangeRequiredClaim,
            bool.TrueString);
        var allowedPath = context.Request.Path.Equals("/api/v1/auth/password", StringComparison.OrdinalIgnoreCase) ||
                          context.Request.Path.Equals("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase);
        if (passwordChangeRequired && !allowedPath)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "password_change_required",
                message = "首次登录或密码重置后必须先修改密码。"
            });
            return;
        }

        await next(context);
    });
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapPost("/api/v1/auth/login", async (
        LoginRequest request,
        PlatformDbContext dbContext,
        Argon2PasswordHasher passwordHasher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Username == request.Username && item.DisabledAt == null, cancellationToken);
            if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            dbContext.UserSessions.Add(new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(12)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new LoginResponse(
                token,
                DateTimeOffset.UtcNow.AddHours(12),
                user.Username,
                user.RequiresPasswordChange));
        }
        catch (DbException exception)
        {
            loggerFactory.CreateLogger("VisiCore.Api.Login").LogError(exception, "登录时无法访问平台数据库。");
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "中心账号服务暂不可用",
                detail: "平台数据库连接失败，请稍后重试。");
        }
    });

    app.MapPost("/api/v1/auth/logout", async (
        ClaimsPrincipal principal,
        StreamSessionOrchestrator orchestrator,
        PtzControlService ptzControlService,
        CancellationToken cancellationToken) =>
    {
        var sessionId = principal.FindFirstValue(ClaimTypes.Sid);
        if (!Guid.TryParse(sessionId, out var parsedSessionId))
        {
            return Results.Unauthorized();
        }

        try
        {
            await ptzControlService.RevokeForUserSessionAsync(parsedSessionId, "client_logout", cancellationToken);
            await orchestrator.RevokeLoginSessionAsync(parsedSessionId, "client_logout", cancellationToken);
            return Results.NoContent();
        }
        catch (StreamSessionConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }).RequireAuthorization();

    app.MapPut("/api/v1/auth/password", async (
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        AccountPasswordService passwordService,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await passwordService.ChangeAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
        if (result == AccountPasswordChangeResult.UserUnavailable)
        {
            return Results.Unauthorized();
        }
        if (result == AccountPasswordChangeResult.CurrentPasswordIncorrect)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["currentPassword"] = ["当前密码不正确。"]
            });
        }
        if (result == AccountPasswordChangeResult.NewPasswordInvalid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newPassword"] = ["新密码长度必须为 12 至 256 位。"]
            });
        }
        if (result == AccountPasswordChangeResult.PasswordUnchanged)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newPassword"] = ["新密码不能与当前密码相同。"]
            });
        }

        var cleanupErrors = new List<Exception>();
        async Task AttemptCleanupAsync(Func<IServiceProvider, Task> action)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await action(scope.ServiceProvider);
            }
            catch (Exception exception)
            {
                cleanupErrors.Add(exception);
            }
        }

        await AttemptCleanupAsync(services => services.GetRequiredService<StreamSessionOrchestrator>()
            .RevokeForUserAsync(userId, "password_changed", CancellationToken.None));
        await AttemptCleanupAsync(services => services.GetRequiredService<PtzControlService>()
            .RevokeForUserAsync(userId, "password_changed", CancellationToken.None));
        await AttemptCleanupAsync(services => CancelUnauthorizedExportsForUsersAsync(
            [userId],
            true,
            services.GetRequiredService<PlatformDbContext>(),
            services.GetRequiredService<PlatformAccessService>(),
            services.GetRequiredService<EdgeCommandControlService>(),
            CancellationToken.None));
        await AttemptCleanupAsync(services => services.GetRequiredService<AuditService>().WriteAsync(
            principal,
            "user.password.change",
            "user",
            userId,
            new { CleanupIncomplete = cleanupErrors.Count > 0 },
            CancellationToken.None));
        if (cleanupErrors.Count > 0)
        {
            loggerFactory.CreateLogger("VisiCore.Api.AccountPasswordChange").LogError(
                new AggregateException(cleanupErrors),
                "账号 {UserId} 密码已修改，但后续安全清理存在失败。",
                userId);
        }
        return Results.NoContent();
    }).RequireAuthorization().RequireRateLimiting("account-password-change");

    app.MapGet("/api/v1/public/offline-devices", async (
        string? region,
        string? name,
        string? deviceType,
        int? page,
        int? pageSize,
        HttpResponse response,
        PublicOfflineDeviceService offlineDeviceService,
        CancellationToken cancellationToken) =>
    {
        if (!PublicOfflineDeviceQuery.TryCreate(region, name, deviceType, page, pageSize, out var query, out var validationError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = [validationError] });
        }

        response.Headers.CacheControl = "no-store";
        return Results.Ok(await offlineDeviceService.ListAsync(query, cancellationToken));
    }).AllowAnonymous().RequireRateLimiting("public-offline-devices");

    var cameras = app.MapGroup("/api/v1/cameras").RequireAuthorization();
    cameras.MapGet("/", async (ClaimsPrincipal principal, PlatformAccessService accessService, DevicePluginCapabilityService capabilityService, CancellationToken cancellationToken) =>
    {
        var liveViewCameras = await accessService.GetCamerasAsync(principal, CameraPermission.LiveView, cancellationToken);
        var playbackCameras = await accessService.GetCamerasAsync(principal, CameraPermission.Playback, cancellationToken);
        var ptzCameras = await accessService.GetCamerasAsync(principal, CameraPermission.PtzControl, cancellationToken);
        var visibleCameras = liveViewCameras
            .Concat(playbackCameras)
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.Code)
            .ToList();
        var recorderIds = visibleCameras.Select(item => item.RecorderId).Distinct().ToArray();
        var liveRecorders = await capabilityService.GetSupportedRecorderIdsAsync(recorderIds, DevicePluginCapabilityKind.LiveView, cancellationToken);
        var playbackRecorders = await capabilityService.GetSupportedRecorderIdsAsync(recorderIds, DevicePluginCapabilityKind.Playback, cancellationToken);
        var ptzRecorders = await capabilityService.GetSupportedRecorderIdsAsync(recorderIds, DevicePluginCapabilityKind.Ptz, cancellationToken);
        var liveViewCameraIds = liveViewCameras.Where(item => liveRecorders.Contains(item.RecorderId)).Select(item => item.Id).ToHashSet();
        var playbackCameraIds = playbackCameras.Where(item => playbackRecorders.Contains(item.RecorderId)).Select(item => item.Id).ToHashSet();
        var ptzCameraIds = ptzCameras.Where(item => ptzRecorders.Contains(item.RecorderId)).Select(item => item.Id).ToHashSet();
        return Results.Ok(visibleCameras
            .Where(item => liveViewCameraIds.Contains(item.Id) || playbackCameraIds.Contains(item.Id))
            .Select(item => new CameraResponse(
            item.Id,
            item.Code,
            item.Alias,
            item.RegionId,
            item.SupportsPtz,
            item.Connectivity,
            liveViewCameraIds.Contains(item.Id),
            playbackCameraIds.Contains(item.Id),
            item.SupportsPtz && ptzCameraIds.Contains(item.Id))));
    });

    cameras.MapGet("/{cameraId:guid}/status", async (Guid cameraId, ClaimsPrincipal principal, PlatformAccessService accessService, CancellationToken cancellationToken) =>
    {
        var camera = await accessService.FindAuthorizedCameraAsync(principal, cameraId, CameraPermission.LiveView, cancellationToken);
        return camera is null ? Results.NotFound() : Results.Ok(new { camera.Id, camera.Connectivity, camera.LastVerifiedAt });
    });

    cameras.MapPost("/{cameraId:guid}/sessions", async (Guid cameraId, StreamSessionRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, StreamSessionOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        if (request.Operation != CameraPermission.LiveView)
        {
            return Results.BadRequest(new { message = "当前端点只允许申请实时预览会话；录像回放使用独立中继端点。" });
        }

        var requiredPermission = request.Operation;

        var camera = await accessService.FindAuthorizedCameraAsync(principal, cameraId, requiredPermission, cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }

        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }
        try
        {
            var session = await orchestrator.IssueAsync(
                user.Id, userSessionId, camera.Id, request.Operation, request.Profile,
                request.SlotNumber, request.ClientRequestId, cancellationToken);
            return Results.Ok(new StreamSessionResponse(
                session.Id, session.GatewayUri, session.TicketExpiresAt,
                session.LeaseExpiresAt, session.RenewAfterSeconds));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (StreamGatewayUnavailableException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "流会话网关不可用", detail: exception.Message);
        }
        catch (StreamSessionQuotaExceededException exception)
        {
            return Results.Json(new { message = exception.Message, quotaType = exception.QuotaType, limit = exception.Limit }, statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (StreamSessionConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (StreamSessionDuplicateRequestException exception)
        {
            return Results.Conflict(new { message = exception.Message, sessionId = exception.SessionId });
        }
        catch (StreamSessionInactiveException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }).RequireRateLimiting("stream-session-create");

    cameras.MapPost("/sessions/{sessionId:guid}/renew", async (Guid sessionId, ClaimsPrincipal principal, PlatformAccessService accessService, StreamSessionOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }
        try
        {
            var renewed = await orchestrator.RenewAsync(sessionId, user.Id, userSessionId, cancellationToken);
            return Results.Ok(new StreamSessionRenewalResponse(renewed.Id, renewed.LeaseExpiresAt, renewed.RenewAfterSeconds));
        }
        catch (StreamSessionInactiveException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (StreamGatewayUnavailableException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "流会话网关不可用", detail: exception.Message);
        }
    });

    cameras.MapPost("/sessions/{sessionId:guid}/tickets", async (Guid sessionId, ClaimsPrincipal principal, PlatformAccessService accessService, StreamSessionOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }
        try
        {
            var session = await orchestrator.IssueReconnectTicketAsync(sessionId, user.Id, userSessionId, cancellationToken);
            return Results.Ok(new StreamSessionResponse(
                session.Id, session.GatewayUri, session.TicketExpiresAt,
                session.LeaseExpiresAt, session.RenewAfterSeconds));
        }
        catch (StreamSessionInactiveException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (PlaybackRelayNotReadyException exception)
        {
            return Results.Conflict(new { message = exception.Message, status = exception.Status.ToString(), failureKind = exception.FailureKind });
        }
        catch (StreamGatewayUnavailableException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "流会话网关不可用", detail: exception.Message);
        }
    }).RequireRateLimiting("stream-session-create");

    cameras.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal principal, PlatformAccessService accessService, StreamSessionOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }
        try
        {
            var revoked = await orchestrator.RevokeAsync(sessionId, user.Id, userSessionId, user.IsSystemAdministrator, "client_closed", cancellationToken);
            return revoked ? Results.NoContent() : Results.NotFound();
        }
        catch (StreamSessionConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    });

    cameras.MapPost("/{cameraId:guid}/recording-searches", async (
        Guid cameraId,
        CreateRecordingSearchRequest request,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        RecordingSearchService recordingSearchService,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        var camera = await accessService.FindAuthorizedCameraAsync(
            principal,
            cameraId,
            CameraPermission.Playback,
            cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var creation = await recordingSearchService.CreateAsync(
                user.Id,
                userSessionId,
                camera,
                request,
                cancellationToken);
            if (creation.Created)
            {
                await auditService.WriteAsync(
                    principal,
                    "recording_search.create",
                    "recording_search",
                    creation.Search.Id,
                    new { cameraId, request.StartedAt, request.EndedAt, request.MaxResults },
                    cancellationToken);
            }
            return Results.Accepted(
                $"/api/v1/recording-searches/{creation.Search.Id:N}",
                ToRecordingSearchResponse(creation.Search));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (RecordingSearchUnavailableException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "录像检索边缘服务不可用",
                detail: exception.Message);
        }
    }).RequireRateLimiting("recording-search-create");

    cameras.MapPost("/{cameraId:guid}/playback-sessions", async (
        Guid cameraId,
        PlaybackSessionRequest request,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        StreamSessionOrchestrator orchestrator,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        var camera = await accessService.FindAuthorizedCameraAsync(
            principal,
            cameraId,
            CameraPermission.Playback,
            cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }
        try
        {
            var creation = await orchestrator.IssuePlaybackAsync(user.Id, userSessionId, camera, request, cancellationToken);
            await auditService.WriteAsync(
                principal,
                "playback_session.create",
                "stream_session",
                creation.Session.Id,
                new { cameraId, request.StartedAt, request.EndedAt, request.SlotNumber },
                cancellationToken);
            return Results.Accepted(
                $"/api/v1/playback-sessions/{creation.Session.Id:N}",
                ToPlaybackSessionResponse(
                    creation.Session,
                    creation.RelayState,
                    await orchestrator.GetPlaybackTransportStateAsync(creation.Session, cancellationToken)));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (PlaybackRelayUnavailableException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "回放中继边缘服务不可用",
                detail: exception.Message);
        }
        catch (StreamSessionQuotaExceededException exception)
        {
            return Results.Json(new { message = exception.Message, quotaType = exception.QuotaType, limit = exception.Limit }, statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (StreamSessionConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (StreamSessionDuplicateRequestException exception)
        {
            return Results.Conflict(new { message = exception.Message, sessionId = exception.SessionId });
        }
        catch (StreamSessionInactiveException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }).RequireRateLimiting("playback-session-create");

    cameras.MapPost("/{cameraId:guid}/ptz/leases", async (
        Guid cameraId,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PtzControlService ptzControlService,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        var camera = await accessService.FindAuthorizedCameraAsync(principal, cameraId, CameraPermission.PtzControl, cancellationToken);
        if (camera is null) return Results.NotFound();
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId)) return Results.Unauthorized();
        try
        {
            var grant = await ptzControlService.AcquireAsync(user.Id, userSessionId, camera, cancellationToken);
            await auditService.WriteAsync(principal, "ptz.lease.acquire", "ptz_control_lease", grant.Lease.Id, new { cameraId }, cancellationToken);
            return Results.Ok(new PtzLeaseResponse(grant.Lease.Id, grant.Lease.ExpiresAt, grant.Lease.LastSequence, grant.LeaseToken));
        }
        catch (PtzUnavailableException exception) { return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "云台控制不可用", detail: exception.Message); }
        catch (PtzLeaseConflictException exception) { return Results.Conflict(new { message = exception.Message }); }
    }).RequireRateLimiting("playback-session-create");

    cameras.MapPost("/{cameraId:guid}/ptz/commands", async (
        Guid cameraId,
        PtzCommandRequest request,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PtzControlService ptzControlService,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        var camera = await accessService.FindAuthorizedCameraAsync(principal, cameraId, CameraPermission.PtzControl, cancellationToken);
        if (camera is null) return Results.NotFound();
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId)) return Results.Unauthorized();
        try
        {
            var accepted = await ptzControlService.QueueCommandAsync(user.Id, userSessionId, camera, request.LeaseId, request.LeaseToken,
                new PtzControlRequest(request.Action, request.Motion, request.Speed, request.Sequence), cancellationToken);
            await auditService.WriteAsync(principal, "ptz.command.queue", "ptz_control_lease", accepted.Lease.Id,
                new { cameraId, request.Action, request.Motion, request.Speed, request.Sequence, accepted.CommandId }, cancellationToken);
            return Results.Accepted($"/api/v1/cameras/{cameraId:N}/ptz/commands/{accepted.CommandId:N}",
                new PtzCommandResponse(accepted.CommandId, accepted.Lease.ExpiresAt, accepted.Lease.LastSequence));
        }
        catch (ArgumentOutOfRangeException exception) { return Results.BadRequest(new { message = exception.Message }); }
        catch (PtzLeaseInvalidException exception) { return Results.Conflict(new { message = exception.Message }); }
        catch (PtzSequenceConflictException exception) { return Results.Conflict(new { message = exception.Message, lastSequence = exception.LastSequence }); }
        catch (PtzUnavailableException exception) { return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "云台控制不可用", detail: exception.Message); }
    }).RequireRateLimiting("playback-session-create");

    app.MapGet("/api/v1/recording-searches/{searchId:guid}", async (
        Guid searchId,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PlatformDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        var search = await dbContext.RecordingSearches.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == searchId,
            cancellationToken);
        if (search is null)
        {
            return Results.NotFound();
        }
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!user.IsSystemAdministrator && search.UserId != user.Id)
        {
            return Results.NotFound();
        }
        var camera = await accessService.FindAuthorizedCameraAsync(
            principal,
            search.CameraId,
            CameraPermission.Playback,
            cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToRecordingSearchResponse(search));
    }).RequireAuthorization();

    app.MapGet("/api/v1/playback-sessions/{sessionId:guid}", async (
        Guid sessionId,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        StreamSessionOrchestrator orchestrator,
        PlatformDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        var session = await dbContext.StreamSessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == sessionId && item.Operation == CameraPermission.Playback,
            cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!user.IsSystemAdministrator && session.UserId != user.Id)
        {
            return Results.NotFound();
        }
        var camera = await accessService.FindAuthorizedCameraAsync(
            principal,
            session.CameraId,
            CameraPermission.Playback,
            cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }
        var state = await orchestrator.GetPlaybackRelayStateAsync(session.Id, cancellationToken);
        var transport = await orchestrator.GetPlaybackTransportStateAsync(session, cancellationToken);
        return Results.Ok(ToPlaybackSessionResponse(session, state, transport));
    }).RequireAuthorization();

    app.MapPost("/api/v1/playback-sessions/{sessionId:guid}/controls", async (
        Guid sessionId,
        PlaybackTransportRequest request,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        StreamSessionOrchestrator orchestrator,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var userSessionId))
        {
            return Results.Unauthorized();
        }
        try
        {
            var queued = await orchestrator.QueuePlaybackTransportCommandAsync(
                sessionId,
                user.Id,
                userSessionId,
                request,
                cancellationToken);
            var relayState = new PlaybackRelaySessionState(PlaybackRelayStatus.Ready, null, null, 0);
            var transport = new PlaybackTransportState(
                PlaybackTransportStatus.Pending,
                false,
                queued.Session.PlaybackStartedAt!.Value,
                1.0,
                true,
                true,
                true,
                null,
                queued.CommandId);
            try
            {
                await auditService.WriteAsync(
                    principal,
                    "playback_session.control",
                    "stream_session",
                    sessionId,
                    new { request.Action, request.Position, request.Speed, request.ClientRequestId, queued.CommandId },
                    cancellationToken);
            }
            catch (Exception exception)
            {
                app.Logger.LogError(exception, "回放控制命令已入队，但审计记录失败：会话 {SessionId}，命令 {CommandId}。", sessionId, queued.CommandId);
            }
            return Results.Accepted(
                $"/api/v1/playback-sessions/{sessionId:N}",
                ToPlaybackSessionResponse(queued.Session, relayState, transport));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (PlaybackRelayNotReadyException exception)
        {
            return Results.Conflict(new { message = "回放中继尚未就绪，请稍后重试。", failureKind = exception.FailureKind });
        }
        catch (StreamSessionInactiveException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (StreamSessionConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }).RequireAuthorization().RequireRateLimiting("playback-transport-control");

    var streamGateway = app.MapGroup("/api/v1/stream-gateway");
    streamGateway.MapPost("/sessions/{sessionId:guid}/consume", async (Guid sessionId, GatewayTicketConsumeRequest request, HttpRequest httpRequest, StreamSessionOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        if (!orchestrator.IsGatewayControlTokenValid(httpRequest.Headers["X-Stream-Gateway-Token"].ToString()))
        {
            return Results.Unauthorized();
        }
        try
        {
            return Results.Ok(await orchestrator.ConsumeTicketAsync(sessionId, request.Ticket, request.GatewayName, cancellationToken));
        }
        catch (StreamTicketInvalidException exception)
        {
            return Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (StreamSessionInactiveException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (StreamGatewayUnavailableException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "流会话网关不可用", detail: exception.Message);
        }
    });

    streamGateway.MapPost("/sessions/status", async (GatewaySessionStatusRequest request, HttpRequest httpRequest, StreamSessionOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        if (!orchestrator.IsGatewayControlTokenValid(httpRequest.Headers["X-Stream-Gateway-Token"].ToString()))
        {
            return Results.Unauthorized();
        }
        if (request.SessionIds is null)
        {
            return Results.BadRequest(new { message = "会话编号列表不能为空。" });
        }
        try
        {
            return Results.Ok(await orchestrator.InspectForGatewayAsync(request.SessionIds.Distinct().ToList(), cancellationToken));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    app.MapGet("/api/v1/regions", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var liveViewCameras = await accessService.GetCamerasAsync(principal, CameraPermission.LiveView, cancellationToken);
        var playbackCameras = await accessService.GetCamerasAsync(principal, CameraPermission.Playback, cancellationToken);
        var visibleCameras = liveViewCameras.Concat(playbackCameras).DistinctBy(item => item.Id);
        var allRegions = await dbContext.Regions.AsNoTracking().ToListAsync(cancellationToken);
        var byId = allRegions.ToDictionary(item => item.Id);
        var visibleRegionIds = new HashSet<Guid>();
        foreach (var camera in visibleCameras)
        {
            var currentId = camera.RegionId;
            while (visibleRegionIds.Add(currentId) && byId.TryGetValue(currentId, out var current) && current.ParentId is not null)
            {
                currentId = current.ParentId.Value;
            }
        }
        return Results.Ok(allRegions.Where(item => visibleRegionIds.Contains(item.Id)).OrderBy(item => item.Code));
    }).RequireAuthorization();

    var deviceWorker = app.MapGroup("/api/v1/device-worker");
    deviceWorker.MapGet("/assignments", async (HttpRequest request, DeviceWorkerAccessService workerAccess, CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        try
        {
            return Results.Ok(await workerAccess.GetAssignmentsAsync(worker.Id, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "设备 Worker 分配配置无效", detail: exception.Message);
        }
    });

    deviceWorker.MapPost("/inventory", async (WorkerInventoryReport report, HttpRequest request, DeviceWorkerAccessService workerAccess, DeviceWorkerSyncService syncService, CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        if (!await workerAccess.IsAssignedAsync(worker.Id, report.RecorderId, cancellationToken))
        {
            return Results.Forbid();
        }

        await syncService.ApplyInventoryAsync(report, cancellationToken);
        return Results.NoContent();
    });

    deviceWorker.MapPost("/health", async (WorkerHealthReport report, HttpRequest request, DeviceWorkerAccessService workerAccess, DeviceWorkerSyncService syncService, CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        if (!await workerAccess.IsAssignedAsync(worker.Id, report.RecorderId, cancellationToken))
        {
            return Results.Forbid();
        }

        await syncService.ApplyHealthAsync(report, cancellationToken);
        return Results.NoContent();
    });

    deviceWorker.MapPost("/clock", async (WorkerClockReport report, HttpRequest request, DeviceWorkerAccessService workerAccess, DeviceWorkerSyncService syncService, IOptions<ClockMonitoringOptions> clockOptions, CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        if (!await workerAccess.IsAssignedAsync(worker.Id, report.RecorderId, cancellationToken))
        {
            return Results.Forbid();
        }

        await syncService.ApplyClockAsync(report, clockOptions.Value, cancellationToken);
        return Results.NoContent();
    });

    deviceWorker.MapPut("/operation-statuses", async (
        WorkerOperationStatusReport report,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        DeviceWorkerOperationStatusService operationStatusService,
        CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        try
        {
            await operationStatusService.ApplyAsync(worker.Id, report, cancellationToken);
            return Results.NoContent();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { message = "边缘运行态上报无效。" });
        }
    });

    deviceWorker.MapPost("/commands/claim", async (
        [FromQuery] int? limit,
        [FromQuery] string[]? commandTypes,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        EdgeCommandControlService commandService,
        CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        if (commandTypes is not null && commandTypes.Any(item => !EdgeCommandTypes.KnownTypes.Contains(item)))
        {
            return Results.BadRequest(new { message = "命令类型筛选包含未登记类型。" });
        }
        return Results.Ok(await commandService.ClaimAsync(worker.Id, limit ?? 10, commandTypes, cancellationToken));
    });

    deviceWorker.MapPost("/commands/{commandId:guid}/complete", async (
        Guid commandId,
        WorkerEdgeCommandCompletion completion,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        EdgeCommandControlService commandService,
        CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        return await commandService.CompleteAsync(worker.Id, commandId, completion, cancellationToken) switch
        {
            EdgeCommandCompletionResult.Invalid => Results.BadRequest(new { message = "命令完成结果无效。" }),
            EdgeCommandCompletionResult.NotFound => Results.NotFound(),
            EdgeCommandCompletionResult.StaleDelivery => Results.Conflict(new { message = "命令领取已过期或已被其他实例接管。" }),
            EdgeCommandCompletionResult.AlreadyCompleted => Results.NoContent(),
            EdgeCommandCompletionResult.Completed => Results.NoContent(),
            EdgeCommandCompletionResult.RetryScheduled => Results.Accepted(),
            EdgeCommandCompletionResult.DeadLettered => Results.Accepted(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    });

    deviceWorker.MapPost("/playback-relays/{playbackSessionId:guid}/authorize-start", async (
        Guid playbackSessionId,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        EdgeCommandControlService commandService,
        CancellationToken cancellationToken) =>
    {
        if (!Guid.TryParse(request.Headers["X-Edge-Command-Id"], out var commandId) ||
            string.IsNullOrWhiteSpace(request.Headers["X-Edge-Command-Delivery"]))
        {
            return Results.BadRequest(new { message = "回放启动授权头无效。" });
        }
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        var allowed = await commandService.CanStartPlaybackRelayAsync(
            worker.Id,
            commandId,
            playbackSessionId,
            request.Headers["X-Edge-Command-Delivery"].ToString(),
            cancellationToken);
        return allowed ? Results.NoContent() : Results.Conflict(new { message = "回放会话已失效或启动命令已撤销。" });
    });

    deviceWorker.MapPost("/playback-relays/{playbackSessionId:guid}/authorize-continue", async (
        Guid playbackSessionId,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        EdgeCommandControlService commandService,
        CancellationToken cancellationToken) =>
    {
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        var allowed = await commandService.CanContinuePlaybackRelayAsync(
            worker.Id,
            playbackSessionId,
            cancellationToken);
        return allowed ? Results.NoContent() : Results.Conflict(new { message = "回放会话已撤销、过期或不属于当前边缘 Worker。" });
    });

    deviceWorker.MapPost("/playback-exports/{playbackExportId:guid}/authorize-continue", async (
        Guid playbackExportId,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        EdgeCommandControlService commandService,
        CancellationToken cancellationToken) =>
    {
        if (!Guid.TryParse(request.Headers["X-Edge-Command-Id"], out var commandId) ||
            string.IsNullOrWhiteSpace(request.Headers["X-Edge-Command-Delivery"]))
        {
            return Results.BadRequest(new { message = "录像导出授权头无效。" });
        }
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        var allowed = await commandService.CanContinuePlaybackExportAsync(
            worker.Id,
            commandId,
            playbackExportId,
            request.Headers["X-Edge-Command-Delivery"].ToString(),
            cancellationToken);
        return allowed ? Results.NoContent() : Results.Conflict(new { message = "录像导出已取消、失效或命令已撤销。" });
    });

    deviceWorker.MapPut("/playback-exports/{playbackExportId:guid}/artifact", async (
        Guid playbackExportId,
        HttpContext context,
        HttpRequest request,
        DeviceWorkerAccessService workerAccess,
        LocalExportArtifactStore artifactStore,
        PlaybackExportArtifactService artifactService,
        CancellationToken cancellationToken) =>
    {
        if (!Guid.TryParse(request.Headers["X-Edge-Command-Id"], out var commandId) ||
            !long.TryParse(request.Headers["X-Export-Size"], out var declaredSizeBytes) ||
            string.IsNullOrWhiteSpace(request.Headers["X-Edge-Command-Delivery"]) ||
            string.IsNullOrWhiteSpace(request.Headers["X-Export-Sha256"]) ||
            !string.Equals(request.ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "导出工件上传头或内容类型无效。" });
        }
        ExportArtifactStorageSettings settings;
        try
        {
            settings = artifactStore.GetSettings();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "导出归档存储不可用", detail: exception.Message);
        }
        if (declaredSizeBytes > settings.MaxUploadBytes ||
            (request.ContentLength is not null && request.ContentLength > settings.MaxUploadBytes))
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var bodySizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = settings.MaxUploadBytes;
        }
        var worker = await workerAccess.AuthenticateAsync(request, cancellationToken);
        if (worker is null)
        {
            return Results.Unauthorized();
        }
        try
        {
            var result = await artifactService.AcceptUploadAsync(
                worker.Id,
                playbackExportId,
                new ExportArtifactUploadRequest(
                    commandId,
                    request.Headers["X-Edge-Command-Delivery"].ToString(),
                    declaredSizeBytes,
                    request.Headers["X-Export-Sha256"].ToString(),
                    request.Body),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "导出工件上传被拒绝", detail: exception.Message);
        }
    });

    var edgeAgents = app.MapGroup("/api/v1/edge-agents");
    edgeAgents.MapPost("/enroll", async (
        EnrollEdgeAgentRequest request,
        LinuxEdgeAgentControlService edgeAgentControl,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await edgeAgentControl.EnrollAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "设备插件签名不受信任", detail: exception.Message);
        }
    });

    edgeAgents.MapPost("/{agentId:guid}/heartbeat", async (
        Guid agentId,
        EdgeAgentHeartbeatRequest request,
        HttpRequest httpRequest,
        LinuxEdgeAgentControlService edgeAgentControl,
        CancellationToken cancellationToken) =>
    {
        var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
        if (agent is null) return Results.Unauthorized();
        try
        {
            await edgeAgentControl.HeartbeatAsync(agent, request, cancellationToken);
            return Results.NoContent();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    edgeAgents.MapGet("/{agentId:guid}/configuration", async (
        Guid agentId,
        HttpRequest httpRequest,
        LinuxEdgeAgentControlService edgeAgentControl,
        CancellationToken cancellationToken) =>
    {
        var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
        return agent is null ? Results.Unauthorized() : Results.Ok(await edgeAgentControl.GetConfigurationAsync(agent, cancellationToken));
    });

    edgeAgents.MapGet("/{agentId:guid}/credentials", async (
        Guid agentId,
        HttpRequest httpRequest,
        LinuxEdgeAgentControlService edgeAgentControl,
        CancellationToken cancellationToken) =>
    {
        var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
        return agent is null ? Results.Unauthorized() : Results.Ok(await edgeAgentControl.GetCredentialEnvelopesAsync(agent, cancellationToken));
    });

    edgeAgents.MapGet("/{agentId:guid}/operations", async (
        Guid agentId,
        HttpRequest httpRequest,
        LinuxEdgeAgentControlService edgeAgentControl,
        CancellationToken cancellationToken) =>
    {
        var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
        return agent is null ? Results.Unauthorized() : Results.Ok(await edgeAgentControl.GetPendingOperationsAsync(agent, cancellationToken));
    });

    edgeAgents.MapPost("/{agentId:guid}/diagnostics", async (
        Guid agentId,
        EdgeAgentDiagnosticReport report,
        HttpRequest httpRequest,
        LinuxEdgeAgentControlService edgeAgentControl,
        CancellationToken cancellationToken) =>
    {
        var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
        if (agent is null) return Results.Unauthorized();
        try
        {
            await edgeAgentControl.ReportDiagnosticAsync(agent, report, cancellationToken);
            return Results.NoContent();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    var admin = app.MapGroup("/api/v1/admin").RequireAuthorization();

    admin.MapGet("/edge-agents", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        var agents = await dbContext.EdgeAgents.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var agentIds = agents.Select(item => item.Id).ToList();
        var envelopeCounts = await dbContext.DeviceCredentialEnvelopes.AsNoTracking()
            .Where(item => agentIds.Contains(item.EdgeAgentId))
            .GroupBy(item => item.EdgeAgentId)
            .Select(group => new { AgentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.AgentId, item => item.Count, cancellationToken);
        var assignments = await dbContext.DeviceWorkerAssignments.AsNoTracking()
            .Where(item => agents.Select(agent => agent.DeviceWorkerId).Contains(item.WorkerId))
            .GroupBy(item => item.WorkerId)
            .Select(group => new { WorkerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.WorkerId, item => item.Count, cancellationToken);
        var latestConfigurations = (await dbContext.EdgeAgentConfigurations.AsNoTracking()
            .Where(item => agentIds.Contains(item.EdgeAgentId))
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken))
            .GroupBy(item => item.EdgeAgentId)
            .ToDictionary(group => group.Key, group => group.First());
        return Results.Ok(agents.Select(agent => new
        {
            agent.Id,
            agent.Name,
            agent.Platform,
            agent.AgentVersion,
            agent.ConfigurationVersion,
            agent.CapabilitiesJson,
            agent.ServiceStatusJson,
            agent.CreatedAt,
            agent.LastSeenAt,
            agent.LastDiagnosticAt,
            agent.LastDiagnosticSucceeded,
            agent.LastDiagnosticMessage,
            agent.DisabledAt,
            credentialCount = envelopeCounts.GetValueOrDefault(agent.Id),
            assignmentCount = assignments.GetValueOrDefault(agent.DeviceWorkerId),
            publicKey = new EdgeAgentPublicKeyRequest(agent.Id, agent.PublicKeyId, AgentCredentialEnvelopeAlgorithms.RsaOaepSha256, agent.SubjectPublicKeyInfoBase64)
        }));
    });

    admin.MapPost("/edge-agents/enrollments", async (CreateEdgeAgentEnrollmentRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, LinuxEdgeAgentControlService edgeAgentControl, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        try
        {
            var created = await edgeAgentControl.CreateEnrollmentAsync(request.Name, request.LifetimeMinutes, cancellationToken);
            await auditService.WriteAsync(principal, "edge_agent.enrollment.create", "edge_agent_enrollment", created.Id,
                new { created.Name, created.ExpiresAt }, cancellationToken);
            return Results.Created($"/api/v1/admin/edge-agents/enrollments/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    admin.MapPatch("/edge-agents/{agentId:guid}/status", async (Guid agentId, SetEdgeAgentStatusRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        var agent = await dbContext.EdgeAgents.SingleOrDefaultAsync(item => item.Id == agentId, cancellationToken);
        if (agent is null) return Results.NotFound();
        agent.DisabledAt = request.Disabled ? DateTimeOffset.UtcNow : null;
        var worker = await dbContext.DeviceWorkers.SingleAsync(item => item.Id == agent.DeviceWorkerId, cancellationToken);
        worker.DisabledAt = agent.DisabledAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, request.Disabled ? "edge_agent.disable" : "edge_agent.enable", "edge_agent", agent.Id,
            new { agent.Name }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPost("/edge-agents/{agentId:guid}/diagnostics", async (Guid agentId, RequestEdgeAgentDiagnosticRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, LinuxEdgeAgentControlService edgeAgentControl, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        try
        {
            var operation = await edgeAgentControl.ScheduleDiagnosticAsync(agentId, request.Kind, cancellationToken);
            await auditService.WriteAsync(principal, "edge_agent.diagnostic.request", "edge_agent", agentId,
                new { request.Kind, operation.Id }, cancellationToken);
            return Results.Accepted($"/api/v1/admin/platform-operations/deployments/{operation.Id}", ToPlatformOperationResponse(operation));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    admin.MapPut("/edge-agents/{agentId:guid}/configuration", async (Guid agentId, UpdateEdgeAgentConfigurationRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!TryNormalizeJson(request.ConfigurationJson, out var configurationJson))
        {
            return Results.BadRequest(new { message = "边缘节点配置必须是合法 JSON。" });
        }
        var agent = await dbContext.EdgeAgents.SingleOrDefaultAsync(item => item.Id == agentId && item.DisabledAt == null, cancellationToken);
        if (agent is null) return Results.NotFound();
        var nextVersion = agent.ConfigurationVersion + 1;
        var configuration = new EdgeAgentConfigurationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = agent.Id,
            Version = nextVersion,
            ConfigurationJson = configurationJson!,
            Status = "published",
            PublishedAt = DateTimeOffset.UtcNow
        };
        agent.ConfigurationVersion = nextVersion;
        dbContext.EdgeAgentConfigurations.Add(configuration);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "edge_agent.configuration.publish", "edge_agent", agent.Id,
            new { configuration.Version }, cancellationToken);
        return Results.Accepted($"/api/v1/edge-agents/{agent.Id}/configuration", new EdgeAgentConfigurationResponse(configuration.Version, configuration.ConfigurationJson, configuration.Status));
    });

    admin.MapGet("/platform-operations/overview", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        var serviceStates = await dbContext.EdgeAgents.AsNoTracking().ToListAsync(cancellationToken);
        var recentOperations = await dbContext.PlatformOperations.AsNoTracking().OrderByDescending(item => item.RequestedAt).Take(10).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            edgeAgentCount = serviceStates.Count,
            onlineEdgeAgentCount = serviceStates.Count(item => item.DisabledAt is null && item.LastSeenAt >= DateTimeOffset.UtcNow.AddMinutes(-2)),
            unhealthyEdgeAgentCount = serviceStates.Count(item => item.DisabledAt is null && item.LastDiagnosticSucceeded == false),
            pendingOperationCount = recentOperations.Count(item => item.Status == "pending"),
            recentOperations = recentOperations.Select(ToPlatformOperationResponse)
        });
    });

    admin.MapGet("/platform-operations/deployments", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        var operations = await dbContext.PlatformOperations.AsNoTracking().OrderByDescending(item => item.RequestedAt).Take(100).ToListAsync(cancellationToken);
        return Results.Ok(operations.Select(ToPlatformOperationResponse));
    });

    admin.MapPost("/platform-operations/deployments", async (RequestPlatformDeploymentRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.ReleaseId) || request.ReleaseId.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(request.PublicKeyId) || request.PublicKeyId.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(request.SignatureBase64) || request.SignatureBase64.Length > 32_768 ||
            !TryNormalizeJson(request.ManifestJson, out var manifestJson) ||
            !TryDecodeCiphertext(request.SignatureBase64, out _))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["deployment"] = ["发行版本、签名或部署清单无效。"] });
        }
        if (!await dbContext.EdgeAgents.AnyAsync(item => item.Id == request.EdgeAgentId && item.DisabledAt == null, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["edgeAgentId"] = ["目标边缘节点不存在或已停用。"] });
        }
        var operation = new PlatformOperationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = request.EdgeAgentId,
            OperationType = "deployment",
            Status = "pending",
            Summary = $"待发布 Linux 发行版本：{request.ReleaseId.Trim()}",
            DetailsJson = JsonSerializer.Serialize(new { releaseId = request.ReleaseId.Trim(), manifestJson, signatureBase64 = request.SignatureBase64, publicKeyId = request.PublicKeyId.Trim() }),
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.PlatformOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "platform.deployment.request", "edge_agent", request.EdgeAgentId,
            new { operation.Id, request.ReleaseId, request.PublicKeyId }, cancellationToken);
        return Results.Accepted($"/api/v1/admin/platform-operations/deployments/{operation.Id}", ToPlatformOperationResponse(operation));
    });

    admin.MapPost("/platform-operations/{operationId:guid}/rollback", async (Guid operationId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        var source = await dbContext.PlatformOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (source is null) return Results.NotFound();
        if (source.EdgeAgentId is null || source.OperationType != "deployment" || source.Status != "succeeded")
        {
            return Results.Conflict(new { message = "只有已成功完成的部署任务可以创建回滚任务。" });
        }
        var rollback = new PlatformOperationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = source.EdgeAgentId,
            OperationType = "rollback",
            Status = "pending",
            Summary = $"回滚部署任务：{source.Id:N}",
            DetailsJson = JsonSerializer.Serialize(new { sourceOperationId = source.Id }),
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.PlatformOperations.Add(rollback);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "platform.deployment.rollback.request", "edge_agent", source.EdgeAgentId.Value,
            new { sourceOperationId = source.Id, rollback.Id }, cancellationToken);
        return Results.Accepted($"/api/v1/admin/platform-operations/deployments/{rollback.Id}", ToPlatformOperationResponse(rollback));
    });

    admin.MapPost("/platform-operations/diagnostics", async (RequestPlatformDiagnosticRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, LinuxEdgeAgentControlService edgeAgentControl, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        try
        {
            var operation = await edgeAgentControl.ScheduleDiagnosticAsync(request.EdgeAgentId, request.Kind, cancellationToken);
            await auditService.WriteAsync(principal, "platform.diagnostic.request", "edge_agent", request.EdgeAgentId,
                new { request.Kind, operation.Id }, cancellationToken);
            return Results.Accepted($"/api/v1/admin/platform-operations/deployments/{operation.Id}", ToPlatformOperationResponse(operation));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    });

    admin.MapPost("/camera-preflights", async (DirectCameraPreflightRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, LinuxEdgeAgentControlService edgeAgentControl, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!DirectCameraAddressPolicy.TryNormalize(request.MainStreamUrl, request.SubStreamUrl, out var address, out var addressError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["mainStreamUrl"] = [addressError] });
        }
        try
        {
            var operation = await edgeAgentControl.ScheduleDirectRtspProbeAsync(
                request.EdgeAgentId,
                request.CredentialId,
                address!.MainUrl,
                address.SubUrl,
                cancellationToken);
            await auditService.WriteAsync(principal, "camera.direct.preflight", "edge_agent", request.EdgeAgentId,
                new { request.CredentialId, operation.Id }, cancellationToken);
            return Results.Accepted($"/api/v1/admin/platform-operations/deployments/{operation.Id}", ToPlatformOperationResponse(operation));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["preflight"] = [exception.Message] });
        }
    });

    admin.MapGet("/playback-exports/cameras", async (
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PluginOperationEligibilityService eligibilityService,
        CancellationToken cancellationToken) =>
    {
        if (!await accessService.HasSystemPermissionAsync(principal, SystemPermission.ManageExports, cancellationToken))
        {
            return Results.Forbid();
        }

        var cameras = await accessService.GetCamerasAsync(principal, CameraPermission.Export, cancellationToken);
        var eligibleRecorderIds = await eligibilityService.GetExportEligibleRecorderIdsAsync(
            cameras.Select(item => item.RecorderId),
            cancellationToken);
        return Results.Ok(cameras
            .Where(item => eligibleRecorderIds.Contains(item.RecorderId))
            .Select(item => new ExportCameraResponse(item.Id, item.Code, item.Alias, item.RegionId, item.Connectivity)));
    });

    admin.MapGet("/playback-exports", async (
        int? limit,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PlatformDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        if (!await accessService.HasSystemPermissionAsync(principal, SystemPermission.ManageExports, cancellationToken))
        {
            return Results.Forbid();
        }
        var visibleCameraIds = (await accessService.GetCamerasAsync(principal, CameraPermission.Export, cancellationToken))
            .Select(item => item.Id)
            .ToHashSet();
        var exports = await dbContext.PlaybackExports.AsNoTracking()
            .Where(item => visibleCameraIds.Contains(item.CameraId))
            .OrderByDescending(item => item.RequestedAt)
            .Take(Math.Clamp(limit ?? 100, 1, 500))
            .ToListAsync(cancellationToken);
        var exportIds = exports.Select(item => item.Id).ToList();
        var artifacts = await dbContext.ExportArtifacts.AsNoTracking()
            .Where(item => exportIds.Contains(item.PlaybackExportId) && item.DeletedAt == null)
            .ToDictionaryAsync(item => item.PlaybackExportId, cancellationToken);
        return Results.Ok(exports.Select(item => new PlaybackExportResponse(
            item.Id, item.CameraId, item.Status.ToString(), item.StartedAt, item.EndedAt, item.Container, item.RequestedAt,
            artifacts.TryGetValue(item.Id, out var artifact)
                ? new ExportArtifactResponse(artifact.Id, artifact.FileName, artifact.SizeBytes, artifact.Sha256, artifact.ExpiresAt)
                : null,
            item.FailureCode)));
    });

    admin.MapPost("/playback-exports", async (
        CreatePlaybackExportAdminRequest request,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PlaybackExportService playbackExportService,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        if (!await accessService.HasSystemPermissionAsync(principal, SystemPermission.ManageExports, cancellationToken))
        {
            return Results.Forbid();
        }
        var camera = await accessService.FindAuthorizedCameraAsync(principal, request.CameraId, CameraPermission.Export, cancellationToken);
        if (camera is null) return Results.NotFound();
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        try
        {
            var export = await playbackExportService.CreateAsync(user.Id, camera,
                new CreatePlaybackExportRequest(request.StartedAt, request.EndedAt, request.Container), cancellationToken);
            await auditService.WriteAsync(principal, "playback_export.create", "playback_export", export.Id,
                new { request.CameraId, request.StartedAt, request.EndedAt, request.Container }, cancellationToken);
            return Results.Accepted($"/api/v1/admin/playback-exports/{export.Id:N}",
                new PlaybackExportResponse(export.Id, export.CameraId, export.Status.ToString(), export.StartedAt, export.EndedAt, export.Container, export.RequestedAt, null, null));
        }
        catch (ArgumentOutOfRangeException exception) { return Results.BadRequest(new { message = exception.Message }); }
        catch (PlaybackExportUnavailableException exception) { return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "导出边缘服务不可用", detail: exception.Message); }
    });

    admin.MapPost("/playback-exports/{playbackExportId:guid}/cancel", async (
        Guid playbackExportId,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PlatformDbContext dbContext,
        EdgeCommandControlService commandService,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        if (!await accessService.HasSystemPermissionAsync(principal, SystemPermission.ManageExports, cancellationToken))
        {
            return Results.Forbid();
        }
        var export = await dbContext.PlaybackExports.AsNoTracking().SingleOrDefaultAsync(item => item.Id == playbackExportId, cancellationToken);
        if (export is null)
        {
            return Results.NotFound();
        }
        var camera = await accessService.FindAuthorizedCameraAsync(principal, export.CameraId, CameraPermission.Export, cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }
        var result = await commandService.CancelPlaybackExportAsync(playbackExportId, cancellationToken);
        if (result == PlaybackExportCancellationResult.NotFound)
        {
            return Results.NotFound();
        }
        if (result == PlaybackExportCancellationResult.NotCancellable)
        {
            return Results.Conflict(new { message = "仅等待执行或正在执行的录像导出可以取消。" });
        }
        await auditService.WriteAsync(principal, "playback_export.cancel", "playback_export", playbackExportId, new { export.CameraId }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapGet("/playback-exports/{playbackExportId:guid}/artifact", async (
        Guid playbackExportId,
        HttpContext context,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PlaybackExportArtifactService artifactService,
        PlatformDbContext dbContext,
        ILogger<AuditedExportDownloadResult> logger,
        CancellationToken cancellationToken) =>
    {
        if (!await accessService.HasSystemPermissionAsync(principal, SystemPermission.ManageExports, cancellationToken))
        {
            return Results.Forbid();
        }
        var download = await artifactService.FindDownloadAsync(playbackExportId, cancellationToken);
        if (download is null)
        {
            return Results.NotFound();
        }
        if (download.Artifact.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Results.StatusCode(StatusCodes.Status410Gone);
        }
        var camera = await accessService.FindAuthorizedCameraAsync(principal, download.Export.CameraId, CameraPermission.Export, cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }
        var stream = await artifactService.OpenReadAsync(download.Artifact, cancellationToken);
        if (stream is null)
        {
            return Results.NotFound();
        }
        var user = await accessService.FindUserAsync(principal, cancellationToken);
        var sessionId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var parsedSessionId) ? parsedSessionId : (Guid?)null;
        var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var audit = new ExportDownloadAuditEntity
        {
            Id = Guid.NewGuid(),
            ExportArtifactId = download.Artifact.Id,
            UserId = user.Id,
            UserSessionId = sessionId,
            ClientAddressHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientAddress))),
            Result = ExportDownloadResult.Started,
            StartedAt = DateTimeOffset.UtcNow
        };
        dbContext.ExportDownloadAudits.Add(audit);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new AuditedExportDownloadResult(
            stream,
            download.Artifact.ContentType,
            download.Artifact.FileName,
            audit,
            dbContext,
            logger);
    });
    admin.MapGet("/regions", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }

        return Results.Ok(await dbContext.Regions.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken));
    });

    admin.MapPost("/regions", async (CreateRegionRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["region"] = ["区域编号和名称不能为空。"] });
        }
        if (request.ParentId is not null && !await dbContext.Regions.AnyAsync(item => item.Id == request.ParentId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["parentId"] = ["上级区域不存在。"] });
        }
        if (await dbContext.Regions.AnyAsync(item => item.Code == request.Code, cancellationToken))
        {
            return Results.Conflict(new { message = "区域编号已存在。" });
        }

        var region = new RegionEntity { Id = Guid.NewGuid(), ParentId = request.ParentId, Code = request.Code.Trim(), Name = request.Name.Trim() };
        dbContext.Regions.Add(region);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "region.create", "region", region.Id, new { region.Code, region.Name, region.ParentId }, cancellationToken);
        return Results.Created($"/api/v1/admin/regions/{region.Id}", region);
    });

    admin.MapPatch("/regions/{regionId:guid}", async (Guid regionId, UpdateRegionRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, EdgeCommandControlService commandService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || request.ParentId == regionId)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["region"] = ["区域编号、名称或上级区域无效。"] });
        }
        var region = await dbContext.Regions.SingleOrDefaultAsync(item => item.Id == regionId, cancellationToken);
        if (region is null)
        {
            return Results.NotFound();
        }
        if (request.ParentId is not null && !await dbContext.Regions.AnyAsync(item => item.Id == request.ParentId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["parentId"] = ["上级区域不存在。"] });
        }
        if (request.ParentId is not null && await WouldCreateRegionCycleAsync(dbContext, regionId, request.ParentId.Value, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["parentId"] = ["不能将区域移动到自身的下级区域。"] });
        }
        if (await dbContext.Regions.AnyAsync(item => item.Id != regionId && item.Code == request.Code.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "区域编号已存在。" });
        }
        var parentChanged = region.ParentId != request.ParentId;
        region.Code = request.Code.Trim();
        region.Name = request.Name.Trim();
        region.ParentId = request.ParentId;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (parentChanged)
        {
            var cameraIds = await GetCameraIdsInRegionSubtreeAsync(dbContext, region.Id, cancellationToken);
            await RevokeUnauthorizedCameraResourcesAsync(cameraIds, dbContext, accessService, orchestrator, ptzControlService, commandService, cancellationToken);
        }
        await auditService.WriteAsync(principal, "region.update", "region", region.Id, new { region.Code, region.Name, region.ParentId }, cancellationToken);
        return Results.Ok(region);
    });

    admin.MapGet("/device-plugins", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        var plugins = await dbContext.DevicePlugins.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var usages = await dbContext.Recorders.AsNoTracking()
            .Where(item => item.DevicePluginId != null)
            .GroupBy(item => item.DevicePluginId!.Value)
            .Select(group => new { PluginId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.PluginId, item => item.Count, cancellationToken);
        return Results.Ok(plugins.Select(item => ToDevicePluginResponse(item, usages.GetValueOrDefault(item.Id))));
    });

    admin.MapPost("/device-plugins/install", async (InstallDevicePluginRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, DevicePluginService pluginService, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        try
        {
            var plugin = await pluginService.InstallAsync(request.Manifest, cancellationToken);
            await auditService.WriteAsync(principal, "device-plugin.install", "device_plugin", plugin.Id,
                new { plugin.Key, plugin.Version, plugin.ProtocolType, plugin.RuntimeType, plugin.PackageHash }, cancellationToken);
            return Results.Ok(ToDevicePluginResponse(plugin, 0));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["manifest"] = [exception.Message] });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    });

    admin.MapPatch("/device-plugins/{pluginId:guid}/status", async (Guid pluginId, SetDevicePluginStatusRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, DevicePluginService pluginService, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        try
        {
            var plugin = await pluginService.SetEnabledAsync(pluginId, request.Enabled, cancellationToken);
            await auditService.WriteAsync(principal, request.Enabled ? "device-plugin.enable" : "device-plugin.disable", "device_plugin", plugin.Id,
                new { plugin.Key, plugin.Version }, cancellationToken);
            return Results.Ok(ToDevicePluginResponse(plugin, 0));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    });

    admin.MapDelete("/device-plugins/{pluginId:guid}", async (Guid pluginId, ClaimsPrincipal principal, PlatformAccessService accessService, DevicePluginService pluginService, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        try
        {
            await pluginService.RemoveAsync(pluginId, cancellationToken);
            await auditService.WriteAsync(principal, "device-plugin.remove", "device_plugin", pluginId, new { pluginId }, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    });

    admin.MapGet("/recorders", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }

        var recorders = await dbContext.Recorders.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var endpoints = await dbContext.RecorderEndpoints.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(recorders.Select(item => new RecorderResponse(item, endpoints.Where(endpoint => endpoint.RecorderId == item.Id).ToList())));
    });

    admin.MapPost("/recorders", async (CreateRecorderRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.TimeZoneId) || request.Endpoints is not { Count: > 0 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["device"] = ["编号、名称、时区和至少一个端点不能为空。"] });
        }
        if (!IsValidTimeZoneId(request.TimeZoneId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = ["录像机时区标识无效。"] });
        }
        var deviceKind = NormalizeDeviceKind(request.DeviceKind);
        if (deviceKind is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["deviceKind"] = ["设备类型无效。"] });
        }
        DevicePluginEntity? plugin = null;
        DevicePluginManifest? manifest = null;
        if (request.DevicePluginId is { } pluginId)
        {
            plugin = await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Id == pluginId && item.Enabled, cancellationToken);
            if (plugin is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["协议插件不存在或未启用。"] });
            }
            manifest = DevicePluginService.ParseManifest(plugin);
            if (!manifest.SupportedDeviceKinds.Contains(deviceKind, StringComparer.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["deviceKind"] = ["协议插件不支持该设备类型。"] });
            }
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["必须选择协议插件。"] });
        }

        var endpointRegistrations = request.Endpoints.Select(item => new RecorderEndpointRegistration(
                    item.Protocol,
                    item.Host,
                    item.Port,
                    item.CredentialReference,
                    item.UseTls,
                    item.CertificateThumbprint)).ToList();
        if (!RecorderRegistrationValidator.TryValidateForPlugin(manifest!, endpointRegistrations, out var endpointValidationError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["endpoints"] = [endpointValidationError] });
        }
        if (!TryNormalizeJsonObject(request.ConfigurationJson, out var configurationJson, out var configurationError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["configurationJson"] = [configurationError] });
        }
        if (await dbContext.Recorders.AnyAsync(item => item.Code == request.Code, cancellationToken))
        {
            return Results.Conflict(new { message = "录像机编号已存在。" });
        }

        var recorder = new RecorderEntity
        {
            Id = Guid.NewGuid(),
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Vendor = NullIfWhiteSpace(request.Vendor) ?? manifest?.Vendor ?? "未指定",
            Model = NullIfWhiteSpace(request.Model),
            AdapterType = plugin?.AdapterType ?? request.AdapterType!.Trim(),
            DevicePluginId = plugin?.Id,
            DeviceKind = deviceKind,
            SerialNumber = NullIfWhiteSpace(request.SerialNumber),
            FirmwareVersion = NullIfWhiteSpace(request.FirmwareVersion),
            Description = NullIfWhiteSpace(request.Description),
            ConfigurationJson = configurationJson,
            TimeZoneId = request.TimeZoneId.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Recorders.Add(recorder);
        foreach (var endpoint in request.Endpoints)
        {
            dbContext.RecorderEndpoints.Add(new RecorderEndpointEntity
            {
                Id = Guid.NewGuid(), RecorderId = recorder.Id, Protocol = endpoint.Protocol, Host = endpoint.Host.Trim(), Port = endpoint.Port, UseTls = endpoint.UseTls, CertificateThumbprint = DeviceCertificatePolicy.NormalizeSha256ThumbprintOrNull(endpoint.CertificateThumbprint), CredentialReference = endpoint.CredentialReference.Trim()
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "device.create", "recorder", recorder.Id,
            new { recorder.Code, recorder.Vendor, recorder.DeviceKind, pluginKey = plugin?.Key, endpointProtocols = request.Endpoints.Select(item => item.Protocol) }, cancellationToken);
        return Results.Created($"/api/v1/admin/recorders/{recorder.Id}", new RecorderResponse(recorder, await dbContext.RecorderEndpoints.Where(item => item.RecorderId == recorder.Id).ToListAsync(cancellationToken)));
    });

    admin.MapPatch("/recorders/{recorderId:guid}", async (Guid recorderId, UpdateRecorderRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["recorder"] = ["录像机编号、名称和时区不能为空。"] });
        }
        if (!IsValidTimeZoneId(request.TimeZoneId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = ["录像机时区标识无效。"] });
        }
        var recorder = await dbContext.Recorders.SingleOrDefaultAsync(item => item.Id == recorderId, cancellationToken);
        if (recorder is null)
        {
            return Results.NotFound();
        }
        if (await dbContext.Recorders.AnyAsync(item => item.Id != recorderId && item.Code == request.Code.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "录像机编号已存在。" });
        }
        var deviceKind = NormalizeDeviceKind(request.DeviceKind ?? recorder.DeviceKind);
        if (deviceKind is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["deviceKind"] = ["设备类型无效。"] });
        }
        var pluginId = request.DevicePluginId ?? recorder.DevicePluginId;
        DevicePluginEntity? plugin = null;
        DevicePluginManifest? manifest = null;
        if (pluginId is not null)
        {
            plugin = await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Id == pluginId, cancellationToken);
            if (plugin is null || (!plugin.Enabled && plugin.Id != recorder.DevicePluginId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["协议插件不存在或未启用。"] });
            }
            manifest = DevicePluginService.ParseManifest(plugin);
            if (!manifest.SupportedDeviceKinds.Contains(deviceKind, StringComparer.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["deviceKind"] = ["协议插件不支持该设备类型。"] });
            }
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["必须选择协议插件。"] });
        }
        var currentEndpoints = await dbContext.RecorderEndpoints.Where(item => item.RecorderId == recorder.Id).ToListAsync(cancellationToken);
        var ownsDirectCamera = await dbContext.Cameras.AnyAsync(
            item => item.RecorderId == recorder.Id && item.SourceType == CameraSourceTypes.Direct,
            cancellationToken);
        if (ownsDirectCamera && (request.Endpoints is { Count: > 0 } || pluginId != recorder.DevicePluginId || deviceKind != DeviceKinds.Camera))
        {
            return Results.Conflict(new { message = "直连摄像头的协议和地址必须在摄像头编辑入口修改。" });
        }
        var requestedEndpoints = request.Endpoints is { Count: > 0 }
            ? request.Endpoints
            : currentEndpoints.Select(item => new CreateRecorderEndpointRequest(item.Protocol, item.Host, item.Port, item.UseTls, item.CertificateThumbprint, item.CredentialReference)).ToList();
        var endpointRegistrations = requestedEndpoints.Select(item => new RecorderEndpointRegistration(
            item.Protocol, item.Host, item.Port, item.CredentialReference, item.UseTls, item.CertificateThumbprint)).ToList();
        var vendor = NullIfWhiteSpace(request.Vendor) ?? recorder.Vendor;
        var adapterType = plugin?.AdapterType ?? recorder.AdapterType;
        if (!RecorderRegistrationValidator.TryValidateForPlugin(manifest!, endpointRegistrations, out var endpointValidationError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["endpoints"] = [endpointValidationError] });
        }
        if (!TryNormalizeJsonObject(request.ConfigurationJson ?? recorder.ConfigurationJson, out var configurationJson, out var configurationError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["configurationJson"] = [configurationError] });
        }

        var routeChanged = recorder.DevicePluginId != pluginId || recorder.AdapterType != adapterType ||
            request.Endpoints is { Count: > 0 };
        recorder.Code = request.Code.Trim();
        recorder.Name = request.Name.Trim();
        recorder.Vendor = vendor;
        recorder.Model = NullIfWhiteSpace(request.Model);
        recorder.AdapterType = adapterType;
        recorder.DevicePluginId = pluginId;
        recorder.DeviceKind = deviceKind;
        recorder.SerialNumber = NullIfWhiteSpace(request.SerialNumber);
        recorder.FirmwareVersion = NullIfWhiteSpace(request.FirmwareVersion);
        recorder.Description = NullIfWhiteSpace(request.Description);
        recorder.ConfigurationJson = configurationJson;
        recorder.TimeZoneId = request.TimeZoneId.Trim();
        if (request.Endpoints is { Count: > 0 })
        {
            dbContext.RecorderEndpoints.RemoveRange(currentEndpoints);
            foreach (var endpoint in requestedEndpoints)
            {
                dbContext.RecorderEndpoints.Add(new RecorderEndpointEntity
                {
                    Id = Guid.NewGuid(), RecorderId = recorder.Id, Protocol = endpoint.Protocol,
                    Host = endpoint.Host.Trim(), Port = endpoint.Port, UseTls = endpoint.UseTls,
                    CertificateThumbprint = DeviceCertificatePolicy.NormalizeSha256ThumbprintOrNull(endpoint.CertificateThumbprint),
                    CredentialReference = endpoint.CredentialReference.Trim()
                });
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (routeChanged)
        {
            var cameraIds = await dbContext.Cameras.AsNoTracking().Where(item => item.RecorderId == recorder.Id).Select(item => item.Id).ToListAsync(cancellationToken);
            await orchestrator.RevokeForCamerasAsync(cameraIds, "device_route_changed", cancellationToken);
            await ptzControlService.RevokeForCamerasAsync(cameraIds, "device_route_changed", cancellationToken);
        }
        await auditService.WriteAsync(principal, "device.update", "recorder", recorder.Id,
            new { recorder.Code, recorder.Name, recorder.Vendor, recorder.Model, recorder.DeviceKind, pluginKey = plugin?.Key, recorder.TimeZoneId, routeChanged }, cancellationToken);
        var endpoints = await dbContext.RecorderEndpoints.AsNoTracking().Where(item => item.RecorderId == recorder.Id).ToListAsync(cancellationToken);
        return Results.Ok(new RecorderResponse(recorder, endpoints));
    });

    admin.MapGet("/cameras", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        var result = await dbContext.Cameras.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var recorderIds = result.Select(item => item.RecorderId).Distinct().ToList();
        var recordersById = await dbContext.Recorders.AsNoTracking().Where(item => recorderIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var rtspEndpointsByRecorder = (await dbContext.RecorderEndpoints.AsNoTracking()
                .Where(item => recorderIds.Contains(item.RecorderId) && item.Protocol == RecorderEndpointProtocol.Rtsp)
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.RecorderId);
        var workersByRecorder = (await dbContext.DeviceWorkerAssignments.AsNoTracking()
                .Where(item => recorderIds.Contains(item.RecorderId))
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.RecorderId, item => (Guid?)item.WorkerId);
        var workerIds = workersByRecorder.Values.Where(item => item is not null).Select(item => item!.Value).Distinct().ToList();
        var agentsByWorker = (await dbContext.EdgeAgents.AsNoTracking()
                .Where(item => workerIds.Contains(item.DeviceWorkerId))
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.DeviceWorkerId, item => (Guid?)item.Id);
        return Results.Ok(result.Select(item => ToAdminCameraResponse(
            item,
            recordersById[item.RecorderId],
            rtspEndpointsByRecorder.GetValueOrDefault(item.RecorderId),
            workersByRecorder.GetValueOrDefault(item.RecorderId),
            workersByRecorder.GetValueOrDefault(item.RecorderId) is { } workerId ? agentsByWorker.GetValueOrDefault(workerId) : null)));
    });

    admin.MapPost("/cameras", async (CreateCameraRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, LinuxEdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Alias))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["camera"] = ["摄像头编号和别名不能为空。"] });
        }
        if (!await dbContext.Regions.AnyAsync(item => item.Id == request.RegionId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["regionId"] = ["区域不存在。"] });
        }
        if (await dbContext.Cameras.AnyAsync(item => item.Code == request.Code.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "摄像头编号已存在。" });
        }

        var sourceType = string.Equals(request.SourceType, CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase)
            ? CameraSourceTypes.Direct
            : CameraSourceTypes.RecorderChannel;
        if (sourceType == CameraSourceTypes.Direct)
        {
            if (request.DevicePluginId is not { } pluginId)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["directCamera"] = ["直连摄像头必须选择协议插件。"] });
            }
            var plugin = await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Id == pluginId && item.Enabled, cancellationToken);
            if (plugin is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["协议插件不存在或未启用。"] });
            }
            var manifest = DevicePluginService.ParseManifest(plugin);
            if (!manifest.RuntimeType.Equals(DevicePluginRuntimeTypes.DirectRtsp, StringComparison.OrdinalIgnoreCase) ||
                !manifest.SupportedDeviceKinds.Contains(DeviceKinds.Camera, StringComparer.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["直连摄像头必须使用支持 camera 的 direct-rtsp 插件。"] });
            }
            Guid workerId;
            Guid? credentialId = null;
            Guid? edgeAgentId = null;
            string credentialReference;
            if (request.EdgeAgentId is not null || request.CredentialId is not null)
            {
                if (request.EdgeAgentId is not { } agentId || request.CredentialId is not { } requestedCredentialId)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["directCamera"] = ["Linux 直连摄像头必须同时选择边缘节点和已验证的凭据。"] });
                }
                var binding = await edgeAgentControl.ResolveCredentialBindingAsync(agentId, requestedCredentialId, cancellationToken);
                if (binding is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentialId"] = ["所选凭据未为目标边缘节点生成有效加密信封。"] });
                }
                workerId = binding.WorkerId;
                credentialId = binding.CredentialId;
                credentialReference = binding.CredentialName;
                edgeAgentId = agentId;
            }
            else
            {
                if (request.WorkerId is not { } legacyWorkerId || string.IsNullOrWhiteSpace(request.CredentialReference))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["directCamera"] = ["直连摄像头必须选择边缘节点与凭据，或提供旧兼容 Worker 和凭据引用。"] });
                }
                if (!await dbContext.DeviceWorkers.AnyAsync(item => item.Id == legacyWorkerId && item.DisabledAt == null, cancellationToken))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["workerId"] = ["Device Worker 不存在或已停用。"] });
                }
                var legacyCredential = await dbContext.DeviceCredentials.AsNoTracking().SingleOrDefaultAsync(
                    item => item.Name == request.CredentialReference.Trim() && item.DisabledAt == null,
                    cancellationToken);
                if (legacyCredential is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentialReference"] = ["凭据引用尚未登记或已停用。"] });
                }
                workerId = legacyWorkerId;
                credentialId = legacyCredential.Id;
                credentialReference = legacyCredential.Name;
            }
            if (!DirectCameraAddressPolicy.TryNormalize(request.MainStreamUrl, request.SubStreamUrl, out var directAddress, out var addressError))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mainStreamUrl"] = [addressError] });
            }
            var directTimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "Asia/Shanghai" : request.TimeZoneId.Trim();
            if (!IsValidTimeZoneId(directTimeZoneId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = ["设备时区标识无效。"] });
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var device = new RecorderEntity
            {
                Id = Guid.NewGuid(),
                Code = await CreateDirectDeviceCodeAsync(dbContext, request.Code.Trim(), cancellationToken),
                Name = request.Alias.Trim(),
                Vendor = NullIfWhiteSpace(request.Manufacturer) ?? manifest.Vendor ?? "未指定",
                Model = NullIfWhiteSpace(request.Model),
                AdapterType = plugin.AdapterType,
                DevicePluginId = plugin.Id,
                DeviceKind = DeviceKinds.Camera,
                SerialNumber = NullIfWhiteSpace(request.SerialNumber),
                Description = NullIfWhiteSpace(request.Description),
                TimeZoneId = directTimeZoneId,
                CreatedAt = now
            };
            var endpoint = new RecorderEndpointEntity
            {
                Id = Guid.NewGuid(),
                RecorderId = device.Id,
                Protocol = RecorderEndpointProtocol.Rtsp,
                Host = directAddress!.Host,
                Port = directAddress.Port,
                UseTls = directAddress.UseTls,
                CredentialReference = credentialReference,
                CredentialId = credentialId
            };
            var directCamera = new CameraEntity
            {
                Id = Guid.NewGuid(), RecorderId = device.Id, RegionId = request.RegionId,
                Code = request.Code.Trim(), Alias = request.Alias.Trim(), InputChannelNumber = 1,
                StreamingChannelMap = directAddress.StreamingChannelMap,
                SourceType = CameraSourceTypes.Direct, ProvisioningMode = CameraProvisioningModes.Manual,
                Manufacturer = NullIfWhiteSpace(request.Manufacturer), Model = NullIfWhiteSpace(request.Model),
                SerialNumber = NullIfWhiteSpace(request.SerialNumber), Description = NullIfWhiteSpace(request.Description),
                SupportsPtz = false, Connectivity = CameraConnectivity.Unknown, CreatedAt = now
            };
            dbContext.Recorders.Add(device);
            dbContext.RecorderEndpoints.Add(endpoint);
            dbContext.Cameras.Add(directCamera);
            dbContext.DeviceWorkerAssignments.Add(new DeviceWorkerAssignmentEntity
            {
                Id = Guid.NewGuid(), WorkerId = workerId, RecorderId = device.Id, DefaultRegionId = request.RegionId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await auditService.WriteAsync(principal, "camera.direct.create", "camera", directCamera.Id,
                new { directCamera.Code, directCamera.RegionId, workerId, edgeAgentId, pluginKey = plugin.Key, transport = directAddress.UseTls ? "rtsps" : "rtsp" }, cancellationToken);
            return Results.Created($"/api/v1/admin/cameras/{directCamera.Id}", ToAdminCameraResponse(directCamera, device, endpoint, workerId, edgeAgentId));
        }

        if (request.RecorderId is not { } recorderId || request.InputChannelNumber <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["camera"] = ["设备通道摄像头必须选择接入设备并填写有效通道。"] });
        }
        var recorder = await dbContext.Recorders.SingleOrDefaultAsync(item => item.Id == recorderId, cancellationToken);
        if (recorder is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["recorderId"] = ["接入设备不存在。"] });
        }
        if (await dbContext.Cameras.AnyAsync(item => item.RecorderId == recorderId && item.InputChannelNumber == request.InputChannelNumber, cancellationToken))
        {
            return Results.Conflict(new { message = "接入设备通道已存在。" });
        }
        var requestedMap = request.StreamingChannelMap;
        if (string.IsNullOrWhiteSpace(requestedMap) || requestedMap.Trim() == "{}")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["streamingChannelMap"] = ["必须填写主、子码流映射。"] });
        }
        var rtspEndpoint = await dbContext.RecorderEndpoints.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RecorderId == recorder.Id && item.Protocol == RecorderEndpointProtocol.Rtsp, cancellationToken);
        if (rtspEndpoint is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["recorderId"] = ["接入设备缺少 RTSP 端点。"] });
        }
        var rtspRegistration = new RecorderEndpointRegistration(
            rtspEndpoint.Protocol,
            rtspEndpoint.Host,
            rtspEndpoint.Port,
            rtspEndpoint.CredentialReference,
            rtspEndpoint.UseTls,
            rtspEndpoint.CertificateThumbprint);
        if (!DirectCameraAddressPolicy.TryValidateStreamingMap(requestedMap, rtspRegistration, out var streamingMap, out var mapError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["streamingChannelMap"] = [mapError] });
        }

        var camera = new CameraEntity
        {
            Id = Guid.NewGuid(), RecorderId = recorderId, RegionId = request.RegionId,
            Code = request.Code.Trim(), Alias = request.Alias.Trim(), InputChannelNumber = request.InputChannelNumber,
            StreamingChannelMap = streamingMap, SourceType = CameraSourceTypes.RecorderChannel,
            ProvisioningMode = CameraProvisioningModes.Manual,
            Manufacturer = NullIfWhiteSpace(request.Manufacturer), Model = NullIfWhiteSpace(request.Model),
            SerialNumber = NullIfWhiteSpace(request.SerialNumber), Description = NullIfWhiteSpace(request.Description),
            SupportsPtz = request.SupportsPtz, Connectivity = CameraConnectivity.Unknown, CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Cameras.Add(camera);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "camera.create", "camera", camera.Id,
            new { camera.Code, camera.RecorderId, camera.RegionId, camera.InputChannelNumber, camera.SourceType }, cancellationToken);
        return Results.Created($"/api/v1/admin/cameras/{camera.Id}", ToAdminCameraResponse(camera, recorder, rtspEndpoint, null));
    });

    admin.MapPatch("/cameras/{cameraId:guid}", async (Guid cameraId, UpdateCameraRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, EdgeCommandControlService commandService, LinuxEdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageAssets, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Alias) ||
            !await dbContext.Regions.AnyAsync(item => item.Id == request.RegionId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["camera"] = ["摄像头编号、别名或区域无效。"] });
        }
        var camera = await dbContext.Cameras.SingleOrDefaultAsync(item => item.Id == cameraId, cancellationToken);
        if (camera is null)
        {
            return Results.NotFound();
        }
        if (await dbContext.Cameras.AnyAsync(item => item.Id != cameraId && item.Code == request.Code.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "摄像头编号已存在。" });
        }
        var recorder = await dbContext.Recorders.SingleAsync(item => item.Id == camera.RecorderId, cancellationToken);
        var rtspEndpoint = await dbContext.RecorderEndpoints.SingleOrDefaultAsync(
            item => item.RecorderId == recorder.Id && item.Protocol == RecorderEndpointProtocol.Rtsp,
            cancellationToken);
        var workerAssignment = await dbContext.DeviceWorkerAssignments.SingleOrDefaultAsync(
            item => item.RecorderId == recorder.Id,
            cancellationToken);
        var regionChanged = camera.RegionId != request.RegionId;
        var previousChannel = camera.InputChannelNumber;
        var previousMap = camera.StreamingChannelMap;
        var previousPluginId = recorder.DevicePluginId;
        var previousWorkerId = workerAssignment?.WorkerId;
        var previousEndpoint = rtspEndpoint is null
            ? null
            : $"{rtspEndpoint.Host}:{rtspEndpoint.Port}:{rtspEndpoint.UseTls}:{rtspEndpoint.CredentialReference}:{rtspEndpoint.CredentialId}";
        camera.Code = request.Code.Trim();
        camera.Alias = request.Alias.Trim();
        camera.RegionId = request.RegionId;
        camera.Manufacturer = NullIfWhiteSpace(request.Manufacturer);
        camera.Model = NullIfWhiteSpace(request.Model);
        camera.SerialNumber = NullIfWhiteSpace(request.SerialNumber);
        camera.Description = NullIfWhiteSpace(request.Description);
        camera.ProvisioningMode = CameraProvisioningModes.Manual;
        Guid? selectedEdgeAgentId = null;

        if (camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase))
        {
            if (rtspEndpoint is null || workerAssignment is null || string.IsNullOrWhiteSpace(request.MainStreamUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mainStreamUrl"] = ["直连摄像头必须保留有效的码流端点和 Worker 分配。"] });
            }
            if (!DirectCameraAddressPolicy.TryNormalize(request.MainStreamUrl, request.SubStreamUrl, out var directAddress, out var addressError))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mainStreamUrl"] = [addressError] });
            }
            string credentialReference;
            Guid? credentialId;
            Guid workerId;
            if (request.EdgeAgentId is not null || request.CredentialId is not null)
            {
                if (request.EdgeAgentId is not { } agentId || request.CredentialId is not { } requestedCredentialId)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["directCamera"] = ["Linux 直连摄像头必须同时选择边缘节点和已验证的凭据。"] });
                }
                var binding = await edgeAgentControl.ResolveCredentialBindingAsync(agentId, requestedCredentialId, cancellationToken);
                if (binding is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentialId"] = ["所选凭据未为目标边缘节点生成有效加密信封。"] });
                }
                credentialReference = binding.CredentialName;
                credentialId = binding.CredentialId;
                workerId = binding.WorkerId;
                selectedEdgeAgentId = agentId;
            }
            else
            {
                credentialReference = NullIfWhiteSpace(request.CredentialReference) ?? rtspEndpoint.CredentialReference;
                var legacyCredential = await dbContext.DeviceCredentials.SingleOrDefaultAsync(
                    item => item.Name == credentialReference && item.DisabledAt == null,
                    cancellationToken);
                if (legacyCredential is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentialReference"] = ["凭据引用尚未登记或已停用。"] });
                }
                credentialId = legacyCredential.Id;
                workerId = request.WorkerId ?? workerAssignment.WorkerId;
                if (workerId != workerAssignment.WorkerId &&
                    !await dbContext.DeviceWorkers.AnyAsync(item => item.Id == workerId && item.DisabledAt == null, cancellationToken))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["workerId"] = ["目标 Device Worker 不存在或已停用。"] });
                }
            }
            var pluginId = request.DevicePluginId ?? recorder.DevicePluginId;
            var plugin = pluginId is null
                ? null
                : await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Id == pluginId && item.Enabled, cancellationToken);
            if (plugin is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["直连摄像头必须使用已启用的协议插件。"] });
            }
            var manifest = DevicePluginService.ParseManifest(plugin);
            if (!manifest.RuntimeType.Equals(DevicePluginRuntimeTypes.DirectRtsp, StringComparison.OrdinalIgnoreCase) ||
                !manifest.SupportedDeviceKinds.Contains(DeviceKinds.Camera, StringComparer.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["devicePluginId"] = ["所选插件不支持直连摄像头。"] });
            }
            var timeZoneId = NullIfWhiteSpace(request.TimeZoneId) ?? recorder.TimeZoneId;
            if (!IsValidTimeZoneId(timeZoneId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = ["设备时区标识无效。"] });
            }
            rtspEndpoint.Host = directAddress!.Host;
            rtspEndpoint.Port = directAddress.Port;
            rtspEndpoint.UseTls = directAddress.UseTls;
            rtspEndpoint.CredentialReference = credentialReference;
            rtspEndpoint.CredentialId = credentialId;
            camera.StreamingChannelMap = directAddress.StreamingChannelMap;
            camera.SupportsPtz = false;
            recorder.Name = camera.Alias;
            recorder.Vendor = camera.Manufacturer ?? manifest.Vendor ?? "未指定";
            recorder.Model = camera.Model;
            recorder.SerialNumber = camera.SerialNumber;
            recorder.Description = camera.Description;
            recorder.DevicePluginId = plugin.Id;
            recorder.AdapterType = plugin.AdapterType;
            recorder.TimeZoneId = timeZoneId;
            workerAssignment.WorkerId = workerId;
            workerAssignment.DefaultRegionId = request.RegionId;
        }
        else
        {
            var channel = request.InputChannelNumber ?? camera.InputChannelNumber;
            if (channel <= 0 || await dbContext.Cameras.AnyAsync(
                    item => item.Id != camera.Id && item.RecorderId == camera.RecorderId && item.InputChannelNumber == channel,
                    cancellationToken))
            {
                return Results.Conflict(new { message = "接入设备通道无效或已存在。" });
            }
            var rtspRegistration = rtspEndpoint is null
                ? null
                : new RecorderEndpointRegistration(
                    rtspEndpoint.Protocol,
                    rtspEndpoint.Host,
                    rtspEndpoint.Port,
                    rtspEndpoint.CredentialReference,
                    rtspEndpoint.UseTls,
                    rtspEndpoint.CertificateThumbprint);
            if (rtspRegistration is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["recorderId"] = ["接入设备缺少 RTSP 端点。"] });
            }
            if (!DirectCameraAddressPolicy.TryValidateStreamingMap(
                    request.StreamingChannelMap ?? camera.StreamingChannelMap,
                    rtspRegistration,
                    out var streamingMap,
                    out var mapError))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["streamingChannelMap"] = [mapError] });
            }
            camera.InputChannelNumber = channel;
            camera.StreamingChannelMap = streamingMap;
            camera.SupportsPtz = request.SupportsPtz ?? camera.SupportsPtz;
        }

        var currentEndpoint = rtspEndpoint is null
            ? null
            : $"{rtspEndpoint.Host}:{rtspEndpoint.Port}:{rtspEndpoint.UseTls}:{rtspEndpoint.CredentialReference}:{rtspEndpoint.CredentialId}";
        var routeChanged = previousChannel != camera.InputChannelNumber || previousMap != camera.StreamingChannelMap ||
            previousEndpoint != currentEndpoint || previousPluginId != recorder.DevicePluginId || previousWorkerId != workerAssignment?.WorkerId;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (regionChanged)
        {
            await RevokeUnauthorizedCameraResourcesAsync([camera.Id], dbContext, accessService, orchestrator, ptzControlService, commandService, cancellationToken);
        }
        if (routeChanged)
        {
            await orchestrator.RevokeForCamerasAsync([camera.Id], "camera_route_changed", cancellationToken);
            await ptzControlService.RevokeForCamerasAsync([camera.Id], "camera_route_changed", cancellationToken);
        }
        await auditService.WriteAsync(principal, "camera.update", "camera", camera.Id,
            new { camera.Code, camera.Alias, camera.RegionId, camera.InputChannelNumber, camera.SupportsPtz, routeChanged }, cancellationToken);
        return Results.Ok(ToAdminCameraResponse(camera, recorder, rtspEndpoint, workerAssignment?.WorkerId, selectedEdgeAgentId));
    });

    admin.MapGet("/roles", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }

        var roles = await dbContext.Roles.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var scopes = await dbContext.RoleCameraScopes.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(roles.Select(role => new RoleResponse(role.Id, role.Code, role.Name, role.SystemPermissions,
            scopes.Where(scope => scope.RoleId == role.Id).Select(ToScopeResponse).ToList())));
    });

    admin.MapPost("/roles", async (CreateRoleRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) ||
            !IsValidSystemPermissions(request.SystemPermissions))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["角色编号、名称或系统权限无效。"] });
        }
        if (await dbContext.Roles.AnyAsync(item => item.Code == request.Code.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "角色编号已存在。" });
        }

        var role = new RoleEntity { Id = Guid.NewGuid(), Code = request.Code.Trim(), Name = request.Name.Trim(), SystemPermissions = request.SystemPermissions };
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "role.create", "role", role.Id, new { role.Code, role.Name, role.SystemPermissions }, cancellationToken);
        return Results.Created($"/api/v1/admin/roles/{role.Id}", new RoleResponse(role.Id, role.Code, role.Name, role.SystemPermissions, []));
    });

    admin.MapPatch("/roles/{roleId:guid}/system-permissions", async (
        Guid roleId,
        UpdateRoleSystemPermissionsRequest request,
        ClaimsPrincipal principal,
        PlatformAccessService accessService,
        PlatformDbContext dbContext,
        AuditService auditService,
        CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!IsValidSystemPermissions(request.SystemPermissions))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["systemPermissions"] = ["系统权限包含未定义的标志。"] });
        }
        var role = await dbContext.Roles.SingleOrDefaultAsync(item => item.Id == roleId, cancellationToken);
        if (role is null)
        {
            return Results.NotFound();
        }

        role.SystemPermissions = request.SystemPermissions;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "role.system_permissions.update", "role", role.Id,
            new { role.Code, role.SystemPermissions }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPut("/roles/{roleId:guid}/camera-scopes", async (Guid roleId, ReplaceRoleCameraScopesRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, EdgeCommandControlService commandService, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!await dbContext.Roles.AnyAsync(item => item.Id == roleId, cancellationToken))
        {
            return Results.NotFound();
        }
        if (request.Scopes is null || request.Scopes.Any(scope => !IsValidScope(scope)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["scopes"] = ["每项范围必须且只能指定一个区域或摄像头，并配置有效权限。"] });
        }

        var regionIds = request.Scopes.Where(item => item.RegionId is not null).Select(item => item.RegionId!.Value).Distinct().ToList();
        var cameraIds = request.Scopes.Where(item => item.CameraId is not null).Select(item => item.CameraId!.Value).Distinct().ToList();
        if (await dbContext.Regions.CountAsync(item => regionIds.Contains(item.Id), cancellationToken) != regionIds.Count ||
            await dbContext.Cameras.CountAsync(item => cameraIds.Contains(item.Id), cancellationToken) != cameraIds.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["scopes"] = ["授权范围中存在不存在的区域或摄像头。"] });
        }

        var existing = await dbContext.RoleCameraScopes.Where(item => item.RoleId == roleId).ToListAsync(cancellationToken);
        var affectedUserIds = await dbContext.UserRoles.AsNoTracking()
            .Where(item => item.RoleId == roleId)
            .Join(dbContext.Users.AsNoTracking().Where(item => !item.IsSystemAdministrator), item => item.UserId, user => user.Id, (item, user) => user.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
        dbContext.RoleCameraScopes.RemoveRange(existing);
        dbContext.RoleCameraScopes.AddRange(request.Scopes.Select(scope => new RoleCameraScopeEntity
        {
            Id = Guid.NewGuid(), RoleId = roleId, RegionId = scope.RegionId, CameraId = scope.CameraId, Permissions = scope.Permissions
        }));
        foreach (var affectedUserId in affectedUserIds)
        {
            await RevokeUserSessionsAsync(dbContext, affectedUserId, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var affectedUserId in affectedUserIds)
        {
            await orchestrator.RevokeForUserAsync(affectedUserId, "role_scope_changed", cancellationToken);
            await ptzControlService.RevokeForUserAsync(affectedUserId, "role_scope_changed", cancellationToken);
        }
        await CancelUnauthorizedExportsForUsersAsync(affectedUserIds, false, dbContext, accessService, commandService, cancellationToken);
        await auditService.WriteAsync(principal, "role.camera_scope.replace", "role", roleId, new { count = request.Scopes.Count }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapGet("/users", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }

        var users = await dbContext.Users.AsNoTracking().OrderBy(item => item.Username).ToListAsync(cancellationToken);
        var userRoles = await dbContext.UserRoles.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(users.Select(user => new UserResponse(
            user.Id,
            user.Username,
            user.IsSystemAdministrator,
            user.DisabledAt,
            user.RequiresPasswordChange,
            userRoles.Where(item => item.UserId == user.Id).Select(item => item.RoleId).ToList())));
    });

    admin.MapPost("/users", async (CreateUserRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, Argon2PasswordHasher passwordHasher, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Trim().Length > 64)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["用户名长度必须为 1 至 64 位。"] });
        }
        var username = request.Username.Trim();
        if (await dbContext.Users.AnyAsync(item => item.Username == username, cancellationToken))
        {
            return Results.Conflict(new { message = "用户名已存在。" });
        }

        var roleIds = request.RoleIds?.Distinct().ToList() ?? [];
        if (await dbContext.Roles.CountAsync(item => roleIds.Contains(item.Id), cancellationToken) != roleIds.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["roleIds"] = ["存在不存在的角色。"] });
        }

        var user = new UserEntity
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = passwordHasher.Hash(username),
            RequiresPasswordChange = true, IsSystemAdministrator = request.IsSystemAdministrator, CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(user);
        dbContext.UserRoles.AddRange(roleIds.Select(roleId => new UserRoleEntity { UserId = user.Id, RoleId = roleId }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "user.create", "user", user.Id, new { user.Username, user.IsSystemAdministrator, roleIds }, cancellationToken);
        return Results.Created($"/api/v1/admin/users/{user.Id}", new UserResponse(
            user.Id, user.Username, user.IsSystemAdministrator, user.DisabledAt, user.RequiresPasswordChange, roleIds));
    });

    admin.MapPut("/users/{userId:guid}/roles", async (Guid userId, ReplaceUserRolesRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, EdgeCommandControlService commandService, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!await dbContext.Users.AnyAsync(item => item.Id == userId, cancellationToken))
        {
            return Results.NotFound();
        }

        var roleIds = request.RoleIds?.Distinct().ToList() ?? [];
        if (await dbContext.Roles.CountAsync(item => roleIds.Contains(item.Id), cancellationToken) != roleIds.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["roleIds"] = ["存在不存在的角色。"] });
        }

        dbContext.UserRoles.RemoveRange(await dbContext.UserRoles.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        dbContext.UserRoles.AddRange(roleIds.Select(roleId => new UserRoleEntity { UserId = userId, RoleId = roleId }));
        await RevokeUserSessionsAsync(dbContext, userId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await orchestrator.RevokeForUserAsync(userId, "user_role_changed", cancellationToken);
        await ptzControlService.RevokeForUserAsync(userId, "user_role_changed", cancellationToken);
        await CancelUnauthorizedExportsForUsersAsync([userId], false, dbContext, accessService, commandService, cancellationToken);
        await auditService.WriteAsync(principal, "user.role.replace", "user", userId, new { roleIds }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPut("/users/{userId:guid}/password", async (Guid userId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AccountPasswordService passwordService, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, EdgeCommandControlService commandService, CancellationToken cancellationToken) =>
    {
        if (!await IsSystemAdministratorAsync(principal, accessService, cancellationToken))
        {
            return Results.Forbid();
        }
        if (await passwordService.ResetToUsernameAsync(userId, cancellationToken) == AccountPasswordResetResult.UserUnavailable)
        {
            return Results.NotFound();
        }

        await orchestrator.RevokeForUserAsync(userId, "password_reset", cancellationToken);
        await ptzControlService.RevokeForUserAsync(userId, "password_reset", cancellationToken);
        await CancelUnauthorizedExportsForUsersAsync([userId], true, dbContext, accessService, commandService, cancellationToken);
        await auditService.WriteAsync(principal, "user.password.reset", "user", userId, new { }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPatch("/users/{userId:guid}/status", async (Guid userId, SetUserStatusRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, StreamSessionOrchestrator orchestrator, PtzControlService ptzControlService, EdgeCommandControlService commandService, CancellationToken cancellationToken) =>
    {
        var actor = await accessService.FindUserAsync(principal, cancellationToken);
        if (!actor.IsSystemAdministrator)
        {
            return Results.Forbid();
        }
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }
        if (request.Disabled && user.Id == actor.Id)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = ["不能禁用当前登录的管理员账号。"] });
        }
        if (request.Disabled && user.IsSystemAdministrator && await dbContext.Users.CountAsync(item => item.IsSystemAdministrator && item.DisabledAt == null, cancellationToken) <= 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = ["不能禁用最后一个启用的平台管理员。"] });
        }

        user.DisabledAt = request.Disabled ? DateTimeOffset.UtcNow : null;
        if (request.Disabled)
        {
            await RevokeUserSessionsAsync(dbContext, userId, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (request.Disabled)
        {
            await orchestrator.RevokeForUserAsync(userId, "user_disabled", cancellationToken);
            await ptzControlService.RevokeForUserAsync(userId, "user_disabled", cancellationToken);
            await CancelUnauthorizedExportsForUsersAsync([userId], true, dbContext, accessService, commandService, cancellationToken);
        }
        await auditService.WriteAsync(principal, request.Disabled ? "user.disable" : "user.enable", "user", userId, new { }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapGet("/device-credentials", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceCredentials, cancellationToken))
        {
            return Results.Forbid();
        }
        var credentials = await dbContext.DeviceCredentials.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var credentialIds = credentials.Select(item => item.Id).ToList();
        var versions = await dbContext.DeviceCredentialVersions.AsNoTracking()
            .Where(item => credentialIds.Contains(item.CredentialId) && item.Status == "active")
            .ToDictionaryAsync(item => item.CredentialId, cancellationToken);
        var envelopeRows = await (
            from envelope in dbContext.DeviceCredentialEnvelopes.AsNoTracking()
            join version in dbContext.DeviceCredentialVersions.AsNoTracking() on envelope.CredentialVersionId equals version.Id
            where credentialIds.Contains(version.CredentialId) && version.Status == "active"
            select new { version.CredentialId, envelope.EdgeAgentId })
            .ToListAsync(cancellationToken);
        var usageRows = await dbContext.RecorderEndpoints.AsNoTracking()
            .Where(item => item.CredentialId != null && credentialIds.Contains(item.CredentialId.Value))
            .GroupBy(item => item.CredentialId!.Value)
            .Select(group => new { CredentialId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CredentialId, item => item.Count, cancellationToken);
        return Results.Ok(credentials.Select(credential => ToCredentialResponse(
            credential,
            versions.GetValueOrDefault(credential.Id)?.Version,
            envelopeRows.Where(item => item.CredentialId == credential.Id).Select(item => item.EdgeAgentId).Distinct().ToList(),
            usageRows.GetValueOrDefault(credential.Id))));
    });

    admin.MapPost("/device-credentials", async (CreateDeviceCredentialRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceCredentials, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 256)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = ["凭据名称长度必须在 1 至 256 之间。"] });
        }
        if (await dbContext.DeviceCredentials.AnyAsync(item => item.Name == request.Name.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "凭据引用名已存在。" });
        }

        var envelopes = GetRequestedEnvelopes(request.Envelope, request.Envelopes);
        if (envelopes.Count > 0)
        {
            if (!TryValidateAgentCredentialEnvelopes(envelopes, out var versionId, out var envelopeError) ||
                !await AreAgentCredentialEnvelopesAssignableAsync(envelopes, dbContext, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = [envelopeError ?? "凭据加密信封与目标边缘节点不匹配。"] });
            }
            var now = DateTimeOffset.UtcNow;
            var credential = new DeviceCredentialEntity
            {
                Id = Guid.NewGuid(), Name = request.Name.Trim(), ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope,
                Ciphertext = [], KeyVersion = "agent-envelope-v1", CreatedAt = now
            };
            dbContext.DeviceCredentials.Add(credential);
            dbContext.DeviceCredentialVersions.Add(new DeviceCredentialVersionEntity
            {
                Id = versionId, CredentialId = credential.Id, Version = 1, Status = "active", CreatedAt = now
            });
            foreach (var envelope in envelopes)
            {
                dbContext.DeviceCredentialEnvelopes.Add(LinuxEdgeAgentControlService.ToEnvelopeEntity(envelope, versionId));
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(principal, "device_credential.create", "device_credential", credential.Id,
                new { credential.Name, credential.ProtectionMode, agentCount = envelopes.Count }, cancellationToken);
            return Results.Created($"/api/v1/admin/device-credentials/{credential.Id}", ToCredentialResponse(credential, 1, envelopes.Select(item => item.AgentId).ToList(), 0));
        }

        if (string.IsNullOrWhiteSpace(request.CiphertextBase64) || string.IsNullOrWhiteSpace(request.KeyVersion) ||
            request.ProtectionMode != DeviceCredentialProtectionMode.WindowsDpapiLocalMachine ||
            !TryDecodeCiphertext(request.CiphertextBase64, out var ciphertext) || ciphertext.Length < 16)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = ["Linux 平台凭据必须提供有效的边缘节点加密信封。旧兼容路径仅支持合法 Windows DPAPI 密文。"] });
        }
        var legacyCredential = new DeviceCredentialEntity
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), ProtectionMode = DeviceCredentialProtectionMode.WindowsDpapiLocalMachine, Ciphertext = ciphertext,
            KeyVersion = request.KeyVersion.Trim(), CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.DeviceCredentials.Add(legacyCredential);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "device_credential.create", "device_credential", legacyCredential.Id, new { legacyCredential.Name, legacyCredential.ProtectionMode, legacyCredential.KeyVersion }, cancellationToken);
        return Results.Created($"/api/v1/admin/device-credentials/{legacyCredential.Id}", ToCredentialResponse(legacyCredential));
    });

    admin.MapPost("/device-credentials/{credentialId:guid}/rotate", async (Guid credentialId, RotateDeviceCredentialRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceCredentials, cancellationToken))
        {
            return Results.Forbid();
        }
        var credential = await dbContext.DeviceCredentials.SingleOrDefaultAsync(item => item.Id == credentialId, cancellationToken);
        if (credential is null) return Results.NotFound();
        var envelopes = GetRequestedEnvelopes(request.Envelope, request.Envelopes);
        if (envelopes.Count > 0)
        {
            if (!TryValidateAgentCredentialEnvelopes(envelopes, out var versionId, out var envelopeError) ||
                !await AreAgentCredentialEnvelopesAssignableAsync(envelopes, dbContext, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = [envelopeError ?? "凭据加密信封与目标边缘节点不匹配。"] });
            }
            var now = DateTimeOffset.UtcNow;
            var activeVersions = await dbContext.DeviceCredentialVersions.Where(item => item.CredentialId == credential.Id && item.Status == "active").ToListAsync(cancellationToken);
            foreach (var activeVersion in activeVersions)
            {
                activeVersion.Status = "retired";
                activeVersion.RetiredAt = now;
            }
            var nextVersion = activeVersions.Count == 0
                ? 1
                : await dbContext.DeviceCredentialVersions.Where(item => item.CredentialId == credential.Id).MaxAsync(item => item.Version, cancellationToken) + 1;
            credential.ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope;
            credential.Ciphertext = [];
            credential.KeyVersion = $"agent-envelope-v{nextVersion}";
            credential.RotatedAt = now;
            credential.DisabledAt = null;
            credential.LastVerificationError = null;
            dbContext.DeviceCredentialVersions.Add(new DeviceCredentialVersionEntity
            {
                Id = versionId, CredentialId = credential.Id, Version = nextVersion, Status = "active", CreatedAt = now
            });
            foreach (var envelope in envelopes)
            {
                dbContext.DeviceCredentialEnvelopes.Add(LinuxEdgeAgentControlService.ToEnvelopeEntity(envelope, versionId));
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(principal, "device_credential.rotate", "device_credential", credential.Id,
                new { credential.Name, credential.ProtectionMode, nextVersion, agentCount = envelopes.Count }, cancellationToken);
            return Results.Ok(ToCredentialResponse(credential, nextVersion, envelopes.Select(item => item.AgentId).ToList(), 0));
        }

        if (string.IsNullOrWhiteSpace(request.CiphertextBase64) || string.IsNullOrWhiteSpace(request.KeyVersion) ||
            request.ProtectionMode != DeviceCredentialProtectionMode.WindowsDpapiLocalMachine ||
            !TryDecodeCiphertext(request.CiphertextBase64, out var ciphertext) || ciphertext.Length < 16)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = ["轮换凭据必须提供有效的边缘节点加密信封。"] });
        }
        credential.ProtectionMode = DeviceCredentialProtectionMode.WindowsDpapiLocalMachine;
        credential.Ciphertext = ciphertext;
        credential.KeyVersion = request.KeyVersion.Trim();
        credential.RotatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "device_credential.rotate", "device_credential", credential.Id, new { credential.Name, credential.ProtectionMode, credential.KeyVersion }, cancellationToken);
        return Results.Ok(ToCredentialResponse(credential));
    });

    admin.MapPatch("/device-credentials/{credentialId:guid}/status", async (Guid credentialId, SetDeviceCredentialStatusRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceCredentials, cancellationToken))
        {
            return Results.Forbid();
        }
        var credential = await dbContext.DeviceCredentials.SingleOrDefaultAsync(item => item.Id == credentialId, cancellationToken);
        if (credential is null) return Results.NotFound();
        if (request.Disabled && await dbContext.RecorderEndpoints.AnyAsync(item => item.CredentialId == credential.Id, cancellationToken))
        {
            return Results.Conflict(new { message = "该凭据仍被设备引用，不能停用。请先替换关联设备的凭据。" });
        }
        credential.DisabledAt = request.Disabled ? DateTimeOffset.UtcNow : null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, request.Disabled ? "device_credential.disable" : "device_credential.enable", "device_credential", credential.Id,
            new { credential.Name }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapGet("/device-workers", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        var workers = await dbContext.DeviceWorkers.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var assignments = await dbContext.DeviceWorkerAssignments.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(workers.Select(worker => new DeviceWorkerResponse(worker.Id, worker.Name, worker.DisabledAt, worker.LastSeenAt,
            assignments.Where(item => item.WorkerId == worker.Id).Select(item => new DeviceWorkerAssignmentResponse(item.RecorderId, item.DefaultRegionId)).ToList())));
    });

    admin.MapGet("/device-worker-operation-statuses", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, IOptions<EdgeOperationReadinessOptions> readinessOptions, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        var validAfter = DateTimeOffset.UtcNow.AddSeconds(-readinessOptions.Value.MaximumStatusAgeSeconds);
        var statuses = await (
                from status in dbContext.DeviceWorkerOperationStatuses.AsNoTracking()
                join assignment in dbContext.DeviceWorkerAssignments.AsNoTracking()
                    on new { status.WorkerId, status.RecorderId } equals new { assignment.WorkerId, assignment.RecorderId }
                join worker in dbContext.DeviceWorkers.AsNoTracking()
                    on status.WorkerId equals worker.Id
                orderby status.RecorderId, status.WorkerId, status.OperationType
                select new DeviceWorkerOperationStatusResponse(
                    status.WorkerId,
                    status.RecorderId,
                    status.OperationType,
                    status.IsReady,
                    status.FailureKind,
                    status.ReportedAt,
                    status.IsReady && status.ReportedAt >= validAfter && worker.DisabledAt == null && worker.LastSeenAt >= validAfter,
                    worker.LastSeenAt,
                    worker.DisabledAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(statuses);
    });

    admin.MapGet("/recorders/{recorderId:guid}/operation-routes", async (Guid recorderId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, RecorderOperationRoutingService routingService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!await dbContext.Recorders.AsNoTracking().AnyAsync(item => item.Id == recorderId, cancellationToken))
        {
            return Results.NotFound();
        }
        var recordingSearch = await routingService.GetReadyRouteAsync(recorderId, RecorderOperation.RecordingSearch, cancellationToken);
        var playbackRelay = await routingService.GetReadyRouteAsync(recorderId, RecorderOperation.PlaybackRelay, cancellationToken);
        return Results.Ok(new RecorderOperationRoutesResponse(
            new RecorderOperationRouteResponse(recordingSearch is not null, recordingSearch?.WorkerId, recordingSearch?.CommandType),
            new RecorderOperationRouteResponse(playbackRelay is not null, playbackRelay?.WorkerId, playbackRelay?.CommandType)));
    });

    admin.MapPost("/device-workers", async (CreateDeviceWorkerRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 128)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Worker 名称长度必须在 1 至 128 之间。"] });
        }
        if (await dbContext.DeviceWorkers.AnyAsync(item => item.Name == request.Name.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "Worker 名称已存在。" });
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var worker = new DeviceWorkerEntity
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.DeviceWorkers.Add(worker);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "device_worker.create", "device_worker", worker.Id, new { worker.Name }, cancellationToken);
        return Results.Created($"/api/v1/admin/device-workers/{worker.Id}", new CreatedDeviceWorkerResponse(worker.Id, worker.Name, token));
    });

    admin.MapPost("/device-workers/{workerId:guid}/assignments", async (Guid workerId, CreateDeviceWorkerAssignmentRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        if (!await dbContext.DeviceWorkers.AnyAsync(item => item.Id == workerId && item.DisabledAt == null, cancellationToken) ||
            !await dbContext.Recorders.AnyAsync(item => item.Id == request.RecorderId, cancellationToken) ||
            !await dbContext.Regions.AnyAsync(item => item.Id == request.DefaultRegionId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["assignment"] = ["Worker、录像机或默认区域不存在或不可用。"] });
        }
        if (await dbContext.DeviceWorkerAssignments.AnyAsync(item => item.RecorderId == request.RecorderId, cancellationToken))
        {
            return Results.Conflict(new { message = "该录像机已存在 Worker 分配。" });
        }

        dbContext.DeviceWorkerAssignments.Add(new DeviceWorkerAssignmentEntity { Id = Guid.NewGuid(), WorkerId = workerId, RecorderId = request.RecorderId, DefaultRegionId = request.DefaultRegionId });
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "device_worker.assignment.create", "device_worker", workerId, new { request.RecorderId, request.DefaultRegionId }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPatch("/device-workers/{workerId:guid}/status", async (Guid workerId, SetDeviceWorkerStatusRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        var worker = await dbContext.DeviceWorkers.SingleOrDefaultAsync(item => item.Id == workerId, cancellationToken);
        if (worker is null)
        {
            return Results.NotFound();
        }

        worker.DisabledAt = request.Disabled ? DateTimeOffset.UtcNow : null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, request.Disabled ? "device_worker.disable" : "device_worker.enable", "device_worker", workerId, new { }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPost("/device-workers/{workerId:guid}/rotate-token", async (Guid workerId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageDeviceWorkers, cancellationToken))
        {
            return Results.Forbid();
        }
        var worker = await dbContext.DeviceWorkers.SingleOrDefaultAsync(item => item.Id == workerId && item.DisabledAt == null, cancellationToken);
        if (worker is null)
        {
            return Results.NotFound();
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        worker.TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "device_worker.token.rotate", "device_worker", workerId, new { }, cancellationToken);
        return Results.Ok(new RotatedDeviceWorkerTokenResponse(worker.Id, token));
    });

    admin.MapGet("/notification-channels", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var channels = await dbContext.NotificationChannels.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        return Results.Ok(channels.Select(item => ToNotificationChannelResponse(
            item, ReadNotificationChannelConfiguration(item))));
    });

    admin.MapPost("/notification-channels", async (CreateNotificationChannelRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, NotificationWebhookAddressProtector webhookAddressProtector, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var name = request.Name?.Trim() ?? string.Empty;
        var secretReference = request.SecretReference?.Trim() ?? string.Empty;
        var webhookUrl = request.WebhookUrl?.Trim() ?? string.Empty;
        var validationErrors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            validationErrors["name"] = ["通知渠道名称不能为空且不能超过 128 个字符。"];
        }
        if (!Enum.IsDefined(request.Type))
        {
            validationErrors["type"] = ["通知渠道类型无效。"];
        }
        if (Enum.IsDefined(request.Type))
        {
            if (request.Type == NotificationChannelType.Email &&
                !NotificationChannelRequestValidator.TryValidateSecretReference(secretReference, out var secretReferenceError))
            {
                validationErrors["secretReference"] = [secretReferenceError];
            }
            if (request.Type == NotificationChannelType.WeComWebhook &&
                !NotificationChannelRequestValidator.TryValidateWebhookAddress(webhookUrl, out var webhookAddressError))
            {
                validationErrors["webhookUrl"] = [webhookAddressError];
            }
            if (!NotificationChannelRequestValidator.TryValidateConfiguration(request.Type, request.Configuration, out var configurationError))
            {
                validationErrors["configuration"] = [configurationError];
            }
        }
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }
        if (await dbContext.NotificationChannels.AnyAsync(item => item.Name == name, cancellationToken))
        {
            return Results.Conflict(new { message = "通知渠道名称已存在。" });
        }

        var channel = new NotificationChannelEntity
        {
            Id = Guid.NewGuid(), Name = name, Type = request.Type,
            ConfigurationJson = request.Configuration.GetRawText(),
            SecretReference = request.Type == NotificationChannelType.Email ? secretReference : string.Empty,
            Enabled = true, CreatedAt = DateTimeOffset.UtcNow
        };
        if (channel.Type == NotificationChannelType.WeComWebhook)
        {
            webhookAddressProtector.Protect(channel, webhookUrl);
        }
        dbContext.NotificationChannels.Add(channel);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "notification_channel.create", "notification_channel", channel.Id,
            ToNotificationChannelAuditDetails(channel), cancellationToken);
        return Results.Created($"/api/v1/admin/notification-channels/{channel.Id}",
            ToNotificationChannelResponse(channel, request.Configuration));
    });

    admin.MapPatch("/notification-channels/{channelId:guid}", async (Guid channelId, UpdateNotificationChannelRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, NotificationWebhookAddressProtector webhookAddressProtector, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }

        var channel = await dbContext.NotificationChannels.SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var secretReference = request.SecretReference?.Trim() ?? string.Empty;
        var webhookUrl = request.WebhookUrl?.Trim() ?? string.Empty;
        var validationErrors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            validationErrors["name"] = ["通知渠道名称不能为空且不能超过 128 个字符。"];
        }
        if (channel.Type == NotificationChannelType.Email &&
            !NotificationChannelRequestValidator.TryValidateSecretReference(secretReference, out var secretReferenceError))
        {
            validationErrors["secretReference"] = [secretReferenceError];
        }
        if (channel.Type == NotificationChannelType.WeComWebhook &&
            !string.IsNullOrWhiteSpace(webhookUrl) &&
            !NotificationChannelRequestValidator.TryValidateWebhookAddress(webhookUrl, out var webhookAddressError))
        {
            validationErrors["webhookUrl"] = [webhookAddressError];
        }
        if (channel.Type == NotificationChannelType.WeComWebhook &&
            string.IsNullOrWhiteSpace(webhookUrl) &&
            !HasPersistedWebhookAddress(channel))
        {
            validationErrors["webhookUrl"] = ["企业微信群机器人尚未保存完整 Webhook 地址，请填写后保存。"];
        }
        if (!NotificationChannelRequestValidator.TryValidateConfiguration(channel.Type, request.Configuration, out var configurationError))
        {
            validationErrors["configuration"] = [configurationError];
        }
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }
        if (await dbContext.NotificationChannels.AnyAsync(item => item.Id != channelId && item.Name == name, cancellationToken))
        {
            return Results.Conflict(new { message = "通知渠道名称已存在。" });
        }

        channel.Name = name;
        channel.ConfigurationJson = request.Configuration.GetRawText();
        if (channel.Type == NotificationChannelType.Email)
        {
            channel.SecretReference = secretReference;
        }
        else if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            webhookAddressProtector.Protect(channel, webhookUrl);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "notification_channel.update", "notification_channel", channel.Id,
            ToNotificationChannelAuditDetails(channel), cancellationToken);
        return Results.Ok(ToNotificationChannelResponse(channel, request.Configuration));
    });

    admin.MapPatch("/notification-channels/{channelId:guid}/status", async (Guid channelId, SetEnabledRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var channel = await dbContext.NotificationChannels.SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }
        channel.Enabled = request.Enabled;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, request.Enabled ? "notification_channel.enable" : "notification_channel.disable",
            "notification_channel", channel.Id, new { }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPost("/notification-channels/{channelId:guid}/test", async (Guid channelId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var channel = await dbContext.NotificationChannels.AsNoTracking().SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }
        var existingEvent = await dbContext.OutboxEvents.AsNoTracking()
            .Where(item => item.EventType == NotificationChannelTestRequestedPayload.EventType &&
                           item.AggregateType == NotificationChannelTestRequestedPayload.AggregateType &&
                           item.AggregateId == channelId &&
                           item.ProcessedAt == null &&
                           item.DeadLetteredAt == null)
            .OrderBy(item => item.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingEvent is not null)
        {
            return Results.Accepted(value: new NotificationChannelTestQueuedResponse(existingEvent.Id, existingEvent.OccurredAt));
        }

        var requestedAt = DateTimeOffset.UtcNow;
        var testEvent = new OutboxEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = NotificationChannelTestRequestedPayload.EventType,
            AggregateType = NotificationChannelTestRequestedPayload.AggregateType,
            AggregateId = channel.Id,
            PayloadJson = JsonSerializer.Serialize(new NotificationChannelTestRequestedPayload(channel.Id)),
            OccurredAt = requestedAt,
            NextAttemptAt = requestedAt
        };
        dbContext.OutboxEvents.Add(testEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "notification_channel.test.enqueue", "notification_channel", channel.Id,
            new { testEvent.Id, channel.Name, channel.Type }, cancellationToken);
        return Results.Accepted(value: new NotificationChannelTestQueuedResponse(testEvent.Id, testEvent.OccurredAt));
    }).RequireRateLimiting("notification-channel-test");

    admin.MapGet("/alert-rules", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var rules = await dbContext.AlertRules.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var links = await dbContext.AlertRuleChannels.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(rules.Select(item => new AlertRuleResponse(item.Id, item.Name, item.ResourceType, item.RegionId,
            item.NotifyOnRecovery, item.Enabled, links.Where(link => link.AlertRuleId == item.Id).Select(link => link.NotificationChannelId).ToList())));
    });

    admin.MapPost("/alert-rules", async (CreateAlertRuleRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var resourceType = request.ResourceType.Trim().ToLowerInvariant();
        var channelIds = request.NotificationChannelIds?.Distinct().ToList() ?? [];
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 128 ||
            resourceType is not ("*" or "camera" or "recorder") || channelIds.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["rule"] = ["规则名称、资源类型和至少一个通知渠道不能为空。"] });
        }
        if (request.RegionId is not null && !await dbContext.Regions.AnyAsync(item => item.Id == request.RegionId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["regionId"] = ["指定区域不存在。"] });
        }
        if (await dbContext.NotificationChannels.CountAsync(item => channelIds.Contains(item.Id), cancellationToken) != channelIds.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["notificationChannelIds"] = ["存在不存在的通知渠道。"] });
        }
        if (await dbContext.AlertRules.AnyAsync(item => item.Name == request.Name.Trim(), cancellationToken))
        {
            return Results.Conflict(new { message = "告警规则名称已存在。" });
        }

        var rule = new AlertRuleEntity
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), ResourceType = resourceType, RegionId = request.RegionId,
            NotifyOnRecovery = request.NotifyOnRecovery, Enabled = true, CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AlertRules.Add(rule);
        dbContext.AlertRuleChannels.AddRange(channelIds.Select(channelId => new AlertRuleChannelEntity { AlertRuleId = rule.Id, NotificationChannelId = channelId }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "alert_rule.create", "alert_rule", rule.Id,
            new { rule.Name, rule.ResourceType, rule.RegionId, rule.NotifyOnRecovery, channelIds }, cancellationToken);
        return Results.Created($"/api/v1/admin/alert-rules/{rule.Id}",
            new AlertRuleResponse(rule.Id, rule.Name, rule.ResourceType, rule.RegionId, rule.NotifyOnRecovery, rule.Enabled, channelIds));
    });

    admin.MapPatch("/alert-rules/{ruleId:guid}/status", async (Guid ruleId, SetEnabledRequest request, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var rule = await dbContext.AlertRules.SingleOrDefaultAsync(item => item.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }
        rule.Enabled = request.Enabled;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, request.Enabled ? "alert_rule.enable" : "alert_rule.disable", "alert_rule", rule.Id, new { }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapGet("/alert-incidents", async (bool? openOnly, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var query = dbContext.AlertIncidents.AsNoTracking();
        if (openOnly == true)
        {
            query = query.Where(item => item.ResolvedAt == null);
        }
        return Results.Ok(await query.OrderByDescending(item => item.OpenedAt).Take(1000).ToListAsync(cancellationToken));
    });

    admin.MapGet("/notification-deliveries", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        return Results.Ok(await dbContext.NotificationDeliveries.AsNoTracking()
            .OrderByDescending(item => item.CreatedAt).Take(1000).ToListAsync(cancellationToken));
    });

    admin.MapGet("/outbox/dead-letters", async (ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        return Results.Ok(await dbContext.OutboxEvents.AsNoTracking()
            .Where(item => item.DeadLetteredAt != null)
            .OrderByDescending(item => item.DeadLetteredAt).Take(1000).ToListAsync(cancellationToken));
    });

    admin.MapPost("/outbox/{eventId:guid}/retry", async (Guid eventId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageOperations, cancellationToken))
        {
            return Results.Forbid();
        }
        var outboxEvent = await dbContext.OutboxEvents.SingleOrDefaultAsync(item => item.Id == eventId && item.DeadLetteredAt != null, cancellationToken);
        if (outboxEvent is null)
        {
            return Results.NotFound();
        }
        outboxEvent.Attempts = 0;
        outboxEvent.DeadLetteredAt = null;
        outboxEvent.NextAttemptAt = DateTimeOffset.UtcNow;
        outboxEvent.LastError = null;
        outboxEvent.LockedBy = null;
        outboxEvent.LockedUntil = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "outbox.retry", "outbox_event", outboxEvent.Id, new { outboxEvent.EventType }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapPost("/notification-deliveries/{deliveryId:guid}/retry", async (Guid deliveryId, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, AuditService auditService, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ManageNotifications, cancellationToken))
        {
            return Results.Forbid();
        }
        var delivery = await dbContext.NotificationDeliveries.SingleOrDefaultAsync(
            item => item.Id == deliveryId &&
                    (item.Status == NotificationDeliveryStatus.Failed || item.Status == NotificationDeliveryStatus.DeadLettered),
            cancellationToken);
        if (delivery is null)
        {
            return Results.NotFound();
        }
        delivery.Status = NotificationDeliveryStatus.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptAt = DateTimeOffset.UtcNow;
        delivery.LastError = null;
        delivery.LockedBy = null;
        delivery.LockedUntil = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(principal, "notification_delivery.retry", "notification_delivery", delivery.Id, new { delivery.EventType }, cancellationToken);
        return Results.NoContent();
    });

    admin.MapGet("/audit-logs", async (int? limit, ClaimsPrincipal principal, PlatformAccessService accessService, PlatformDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!await HasSystemPermissionAsync(principal, accessService, SystemPermission.ViewAudit, cancellationToken))
        {
            return Results.Forbid();
        }
        var take = Math.Clamp(limit ?? 500, 1, 2000);
        return Results.Ok(await dbContext.AuditLogs.AsNoTracking()
            .OrderByDescending(item => item.OccurredAt).Take(take).ToListAsync(cancellationToken));
    });
}

static bool HasPersistedWebhookAddress(NotificationChannelEntity channel) =>
    channel.WebhookCiphertext is { Length: > 0 } &&
    channel.WebhookProtectionMode is not null &&
    !string.IsNullOrWhiteSpace(channel.WebhookKeyVersion);

static JsonElement ReadNotificationChannelConfiguration(NotificationChannelEntity channel)
{
    using var document = JsonDocument.Parse(channel.ConfigurationJson);
    if (channel.Type != NotificationChannelType.WeComWebhook)
    {
        return document.RootElement.Clone();
    }
    if (!WeComWebhookConfiguration.TryParse(document.RootElement, out var configuration, out _))
    {
        return JsonSerializer.SerializeToElement(new { });
    }

    return JsonSerializer.SerializeToElement(new
    {
        messageType = configuration.MessageType,
        mentionedList = configuration.MentionedList,
        mentionedMobileList = configuration.MentionedMobileList,
        securityKeyword = configuration.SecurityKeyword
    });
}

static NotificationChannelResponse ToNotificationChannelResponse(NotificationChannelEntity channel, JsonElement configuration) =>
    new(
        channel.Id,
        channel.Name,
        channel.Type,
        configuration,
        channel.Type == NotificationChannelType.Email ? channel.SecretReference : null,
        channel.Enabled,
        channel.CreatedAt,
        channel.Type == NotificationChannelType.WeComWebhook ? HasPersistedWebhookAddress(channel) : null);

static object ToNotificationChannelAuditDetails(NotificationChannelEntity channel) =>
    channel.Type == NotificationChannelType.Email
        ? new { channel.Name, channel.Type, channel.SecretReference }
        : new { channel.Name, channel.Type, WebhookConfigured = HasPersistedWebhookAddress(channel) };

static async Task<bool> IsSystemAdministratorAsync(ClaimsPrincipal principal, PlatformAccessService accessService, CancellationToken cancellationToken)
{
    return (await accessService.FindUserAsync(principal, cancellationToken)).IsSystemAdministrator;
}

static Task<bool> HasSystemPermissionAsync(
    ClaimsPrincipal principal,
    PlatformAccessService accessService,
    SystemPermission requiredPermission,
    CancellationToken cancellationToken) =>
    accessService.HasSystemPermissionAsync(principal, requiredPermission, cancellationToken);

static DevicePluginResponse ToDevicePluginResponse(DevicePluginEntity plugin, int usageCount) =>
    new(
        plugin.Id,
        plugin.Key,
        plugin.Name,
        plugin.Version,
        plugin.ProtocolType,
        plugin.RuntimeType,
        plugin.AdapterType,
        plugin.Vendor,
        plugin.Description,
        plugin.PackageHash,
        plugin.IsBuiltIn,
        plugin.Enabled,
        plugin.InstalledAt,
        plugin.UpdatedAt,
        usageCount,
        DevicePluginService.ParseManifest(plugin));

static AdminCameraResponse ToAdminCameraResponse(
    CameraEntity camera,
    RecorderEntity recorder,
    RecorderEndpointEntity? rtspEndpoint,
    Guid? workerId,
    Guid? edgeAgentId = null)
{
    string? mainStreamUrl = null;
    string? subStreamUrl = null;
    if (camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase) && rtspEndpoint is not null)
    {
        try
        {
            using var map = JsonDocument.Parse(camera.StreamingChannelMap);
            mainStreamUrl = BuildSafeStreamUrl(map.RootElement.GetProperty("main"), rtspEndpoint);
            subStreamUrl = BuildSafeStreamUrl(map.RootElement.GetProperty("sub"), rtspEndpoint);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // 历史异常映射不向后台拼接伪造地址。
        }
    }
    return new AdminCameraResponse(
        camera.Id,
        camera.RecorderId,
        camera.RegionId,
        camera.Code,
        camera.Alias,
        camera.InputChannelNumber,
        camera.StreamingChannelMap,
        camera.SupportsPtz,
        camera.Connectivity,
        camera.LastVerifiedAt,
        camera.SourceType,
        camera.ProvisioningMode,
        recorder.DevicePluginId,
        camera.Manufacturer,
        camera.Model,
        camera.SerialNumber,
        camera.Description,
        camera.CreatedAt,
        mainStreamUrl,
        subStreamUrl,
        camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase)
            ? rtspEndpoint?.CredentialReference
            : null,
        camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase) ? workerId : null,
        camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase) ? recorder.TimeZoneId : null,
        camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase) ? rtspEndpoint?.CredentialId : null,
        camera.SourceType.Equals(CameraSourceTypes.Direct, StringComparison.OrdinalIgnoreCase) ? edgeAgentId : null);
}

static string? BuildSafeStreamUrl(JsonElement mapping, RecorderEndpointEntity endpoint)
{
    if (mapping.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(mapping.GetString()))
    {
        return null;
    }
    var value = mapping.GetString()!;
    UriBuilder builder;
    if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
    {
        if (!absolute.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase) || absolute.Port != endpoint.Port)
        {
            return null;
        }
        builder = new UriBuilder(absolute);
    }
    else if (value.StartsWith("/", StringComparison.Ordinal))
    {
        var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        builder = queryIndex < 0
            ? new UriBuilder(endpoint.UseTls ? "rtsps" : "rtsp", endpoint.Host, endpoint.Port, value)
            : new UriBuilder(endpoint.UseTls ? "rtsps" : "rtsp", endpoint.Host, endpoint.Port, value[..queryIndex])
            {
                Query = value[(queryIndex + 1)..]
            };
    }
    else
    {
        return null;
    }
    builder.UserName = string.Empty;
    builder.Password = string.Empty;
    return builder.Uri.AbsoluteUri;
}

static string? NormalizeDeviceKind(string? value)
{
    var normalized = string.IsNullOrWhiteSpace(value) ? DeviceKinds.Recorder : value.Trim().ToLowerInvariant();
    return DeviceKinds.Known.Contains(normalized) ? normalized : null;
}

static bool TryNormalizeJsonObject(string? value, out string normalized, out string validationError)
{
    normalized = "{}";
    if (string.IsNullOrWhiteSpace(value))
    {
        validationError = string.Empty;
        return true;
    }
    if (value.Length > 65_536)
    {
        validationError = "高级配置不能超过 64 KB。";
        return false;
    }
    try
    {
        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            validationError = "高级配置必须是 JSON 对象。";
            return false;
        }
        normalized = JsonSerializer.Serialize(document.RootElement);
        validationError = string.Empty;
        return true;
    }
    catch (JsonException)
    {
        validationError = "高级配置必须是有效 JSON。";
        return false;
    }
}

static async Task<string> CreateDirectDeviceCodeAsync(
    PlatformDbContext dbContext,
    string cameraCode,
    CancellationToken cancellationToken)
{
    var prefix = cameraCode.Length > 53 ? cameraCode[..53] : cameraCode;
    var candidate = $"{prefix}-DIRECT";
    var suffix = 2;
    while (await dbContext.Recorders.AnyAsync(item => item.Code == candidate, cancellationToken))
    {
        var suffixText = $"-{suffix++}";
        candidate = $"{prefix[..Math.Min(prefix.Length, 64 - 7 - suffixText.Length)]}-DIRECT{suffixText}";
    }
    return candidate;
}

static string? NullIfWhiteSpace(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static bool IsValidScope(RoleCameraScopeRequest scope)
{
    var exactlyOneTarget = (scope.RegionId is null) != (scope.CameraId is null);
    const CameraPermission knownPermissions = CameraPermission.LiveView | CameraPermission.Playback | CameraPermission.PtzControl | CameraPermission.Export | CameraPermission.Manage;
    return exactlyOneTarget && scope.Permissions != CameraPermission.None && (scope.Permissions & ~knownPermissions) == CameraPermission.None;
}

static bool IsValidSystemPermissions(long permissions) =>
    permissions >= 0 && (permissions & ~(long)SystemPermission.All) == 0;

static RecordingSearchResponse ToRecordingSearchResponse(RecordingSearchEntity search)
{
    var expired = search.ExpiresAt <= DateTimeOffset.UtcNow;
    JsonElement? result = null;
    var segments = new List<RecordingSegmentResponse>();
    if (!expired && search.Status == RecordingSearchStatus.Completed && !string.IsNullOrWhiteSpace(search.ResultJson))
    {
        try
        {
            using var document = JsonDocument.Parse(search.ResultJson);
            result = document.RootElement.Clone();
            if (document.RootElement.TryGetProperty("segments", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in values.EnumerateArray())
                {
                    if (!value.TryGetProperty("startedAt", out var startedValue) ||
                        !startedValue.TryGetDateTimeOffset(out var startedAt) ||
                        !value.TryGetProperty("endedAt", out var endedValue) ||
                        !endedValue.TryGetDateTimeOffset(out var endedAt) || startedAt >= endedAt)
                    {
                        continue;
                    }
                    var vendorSegmentId = value.TryGetProperty("vendorSegmentId", out var idValue)
                        ? idValue.ToString()
                        : string.Empty;
                    var sizeBytes = value.TryGetProperty("sizeBytes", out var sizeValue) && sizeValue.TryGetInt64(out var size)
                        ? size
                        : 0;
                    var isLocked = value.TryGetProperty("isLocked", out var lockedValue) && lockedValue.ValueKind == JsonValueKind.True;
                    var segmentType = value.TryGetProperty("fileType", out var fileTypeValue)
                        ? fileTypeValue.ToString()
                        : value.TryGetProperty("trackType", out var trackTypeValue) ? trackTypeValue.ToString() : string.Empty;
                    var coverageApproximate = value.TryGetProperty("coverageApproximate", out var coverageValue) && coverageValue.ValueKind == JsonValueKind.True;
                    segments.Add(new RecordingSegmentResponse(
                        vendorSegmentId,
                        startedAt,
                        endedAt,
                        sizeBytes,
                        isLocked,
                        segmentType,
                        coverageApproximate));
                }
            }
        }
        catch (JsonException)
        {
            // 结果由边缘 Worker 返回，异常内容不向客户端回显。
        }
    }

    return new RecordingSearchResponse(
        search.Id,
        search.CameraId,
        (expired ? RecordingSearchStatus.Expired : search.Status).ToString(),
        search.StartedAt,
        search.EndedAt,
        search.MaxResults,
        search.CreatedAt,
        search.CompletedAt,
        search.ExpiresAt,
        expired ? null : search.FailureKind,
        result,
        segments);
}

static PlaybackSessionResponse ToPlaybackSessionResponse(
    StreamSessionEntity session,
    PlaybackRelaySessionState relayState,
    PlaybackTransportState transportState) => new(
    session.Id,
    relayState.Status.ToString(),
    relayState.StartCommandId,
    relayState.FailureKind,
    relayState.Attempts,
    session.PlaybackStartedAt!.Value,
    session.PlaybackEndedAt!.Value,
    session.ExpiresAt,
    new PlaybackTransportResponse(
        transportState.Status.ToString(),
        transportState.IsPaused,
        transportState.Position,
        transportState.Speed,
        transportState.CanPause,
        transportState.CanSeek,
        transportState.CanChangeSpeed,
        transportState.Detail,
        transportState.CommandId));

static bool IsValidTimeZoneId(string timeZoneId)
{
    try
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        return true;
    }
    catch (TimeZoneNotFoundException)
    {
        return false;
    }
    catch (InvalidTimeZoneException)
    {
        return false;
    }
}

static RoleCameraScopeResponse ToScopeResponse(RoleCameraScopeEntity scope) => new(scope.Id, scope.RegionId, scope.CameraId, scope.Permissions);

static bool TryDecodeCiphertext(string value, out byte[] ciphertext)
{
    try
    {
        ciphertext = Convert.FromBase64String(value);
        return true;
    }
    catch (FormatException)
    {
        ciphertext = [];
        return false;
    }
}

static async Task RevokeUserSessionsAsync(PlatformDbContext dbContext, Guid userId, CancellationToken cancellationToken)
{
    var activeSessions = await dbContext.UserSessions
        .Where(item => item.UserId == userId && item.RevokedAt == null)
        .ToListAsync(cancellationToken);
    foreach (var session in activeSessions)
    {
        session.RevokedAt = DateTimeOffset.UtcNow;
    }
}

static async Task<bool> WouldCreateRegionCycleAsync(PlatformDbContext dbContext, Guid regionId, Guid proposedParentId, CancellationToken cancellationToken)
{
    var visited = new HashSet<Guid>();
    var current = proposedParentId;
    while (visited.Add(current))
    {
        if (current == regionId)
        {
            return true;
        }
        var parentId = await dbContext.Regions.AsNoTracking()
            .Where(item => item.Id == current)
            .Select(item => item.ParentId)
            .SingleOrDefaultAsync(cancellationToken);
        if (parentId is null)
        {
            return false;
        }
        current = parentId.Value;
    }
    return true;
}

static async Task<IReadOnlyList<Guid>> GetCameraIdsInRegionSubtreeAsync(
    PlatformDbContext dbContext,
    Guid rootRegionId,
    CancellationToken cancellationToken)
{
    var regionIds = new HashSet<Guid> { rootRegionId };
    var pending = new HashSet<Guid> { rootRegionId };
    while (pending.Count > 0)
    {
        var children = await dbContext.Regions.AsNoTracking()
            .Where(item => item.ParentId != null && pending.Contains(item.ParentId.Value))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        pending.Clear();
        foreach (var child in children.Where(regionIds.Add))
        {
            pending.Add(child);
        }
    }
    return await dbContext.Cameras.AsNoTracking()
        .Where(item => regionIds.Contains(item.RegionId))
        .Select(item => item.Id)
        .ToListAsync(cancellationToken);
}

static async Task RevokeUnauthorizedCameraResourcesAsync(
    IReadOnlyCollection<Guid> cameraIds,
    PlatformDbContext dbContext,
    PlatformAccessService accessService,
    StreamSessionOrchestrator orchestrator,
    PtzControlService ptzControlService,
    EdgeCommandControlService commandService,
    CancellationToken cancellationToken)
{
    if (cameraIds.Count == 0)
    {
        return;
    }
    await orchestrator.RevokeUnauthorizedForCamerasAsync(cameraIds, "asset_permission_changed", cancellationToken);
    await ptzControlService.RevokeUnauthorizedForCamerasAsync(cameraIds, "asset_permission_changed", cancellationToken);
    var exports = await dbContext.PlaybackExports.AsNoTracking()
        .Where(item => cameraIds.Contains(item.CameraId) &&
            (item.Status == PlaybackExportStatus.Queued || item.Status == PlaybackExportStatus.Running))
        .Select(item => new { item.Id, item.CameraId, item.RequestedByUserId })
        .ToListAsync(cancellationToken);
    foreach (var export in exports)
    {
        if (!await accessService.HasCameraPermissionAsync(
                export.RequestedByUserId,
                export.CameraId,
                CameraPermission.Export,
                cancellationToken))
        {
            await commandService.CancelPlaybackExportAsync(
                export.Id,
                "cancelled_by_authorization_change",
                "摄像头资产或权限范围变化后，发起人已失去录像导出权限。",
                cancellationToken);
        }
    }
}

static async Task CancelUnauthorizedExportsForUsersAsync(
    IReadOnlyCollection<Guid> userIds,
    bool forceCancellation,
    PlatformDbContext dbContext,
    PlatformAccessService accessService,
    EdgeCommandControlService commandService,
    CancellationToken cancellationToken)
{
    var distinctUserIds = userIds.Where(item => item != Guid.Empty).Distinct().ToList();
    if (distinctUserIds.Count == 0)
    {
        return;
    }
    var exports = await dbContext.PlaybackExports.AsNoTracking()
        .Where(item => distinctUserIds.Contains(item.RequestedByUserId) &&
            (item.Status == PlaybackExportStatus.Queued || item.Status == PlaybackExportStatus.Running))
        .Select(item => new { item.Id, item.CameraId, item.RequestedByUserId })
        .ToListAsync(cancellationToken);
    foreach (var export in exports)
    {
        if (forceCancellation || !await accessService.HasCameraPermissionAsync(
                export.RequestedByUserId,
                export.CameraId,
                CameraPermission.Export,
                cancellationToken))
        {
            var code = forceCancellation ? "cancelled_by_account_security_change" : "cancelled_by_authorization_change";
            var detail = forceCancellation
                ? "账号安全状态变化后，已取消录像导出任务。"
                : "权限范围变化后，发起人已失去录像导出权限。";
            await commandService.CancelPlaybackExportAsync(export.Id, code, detail, cancellationToken);
        }
    }
}

static DeviceCredentialResponse ToCredentialResponse(
    DeviceCredentialEntity credential,
    int? activeVersion = null,
    IReadOnlyList<Guid>? agentIds = null,
    int usageCount = 0) => new(
    credential.Id,
    credential.Name,
    credential.ProtectionMode,
    credential.KeyVersion,
    credential.CreatedAt,
    credential.RotatedAt,
    credential.DisabledAt,
    activeVersion,
    agentIds ?? [],
    usageCount,
    credential.LastVerifiedAt,
    credential.LastVerificationError);

static IReadOnlyList<DeviceCredentialEnvelopeInput> GetRequestedEnvelopes(
    DeviceCredentialEnvelopeInput? envelope,
    IReadOnlyList<DeviceCredentialEnvelopeInput>? envelopes)
{
    var result = envelopes is null ? new List<DeviceCredentialEnvelopeInput>() : envelopes.ToList();
    if (envelope is not null)
    {
        result.Add(envelope);
    }
    return result;
}

static bool TryValidateAgentCredentialEnvelopes(
    IReadOnlyList<DeviceCredentialEnvelopeInput> envelopes,
    out Guid credentialVersionId,
    out string? error)
{
    credentialVersionId = Guid.Empty;
    error = null;
    if (envelopes.Count == 0)
    {
        error = "至少需要为一个边缘节点提供凭据加密信封。";
        return false;
    }
    var agentIds = new HashSet<Guid>();
    foreach (var envelope in envelopes)
    {
        if (!LinuxEdgeAgentControlService.TryValidateEnvelope(envelope, out error))
        {
            return false;
        }
        if (credentialVersionId == Guid.Empty)
        {
            credentialVersionId = envelope.CredentialVersionId;
        }
        else if (credentialVersionId != envelope.CredentialVersionId)
        {
            error = "同一次凭据保存的所有信封必须绑定同一凭据版本。";
            return false;
        }
        if (!agentIds.Add(envelope.AgentId))
        {
            error = "同一边缘节点不能重复提交凭据加密信封。";
            return false;
        }
    }
    return true;
}

static async Task<bool> AreAgentCredentialEnvelopesAssignableAsync(
    IReadOnlyList<DeviceCredentialEnvelopeInput> envelopes,
    PlatformDbContext dbContext,
    CancellationToken cancellationToken)
{
    var agentIds = envelopes.Select(item => item.AgentId).Distinct().ToList();
    var agents = await dbContext.EdgeAgents.AsNoTracking()
        .Where(item => agentIds.Contains(item.Id) && item.DisabledAt == null)
        .ToDictionaryAsync(item => item.Id, cancellationToken);
    return agents.Count == agentIds.Count && envelopes.All(envelope =>
        agents.TryGetValue(envelope.AgentId, out var agent) &&
        string.Equals(agent.PublicKeyId, envelope.KeyId, StringComparison.Ordinal));
}

static PlatformOperationResponse ToPlatformOperationResponse(PlatformOperationEntity operation) => new(
    operation.Id,
    operation.EdgeAgentId,
    operation.OperationType,
    operation.Status,
    operation.Summary,
    ToSafePlatformOperationDetails(operation),
    operation.RequestedAt,
    operation.CompletedAt);

static string ToSafePlatformOperationDetails(PlatformOperationEntity operation)
{
    if (string.IsNullOrWhiteSpace(operation.DetailsJson))
    {
        return "{}";
    }

    try
    {
        using var document = JsonDocument.Parse(operation.DetailsJson);
        if (operation.OperationType.Equals("deployment", StringComparison.OrdinalIgnoreCase))
        {
            var root = document.RootElement;
            return JsonSerializer.Serialize(new
            {
                releaseId = root.TryGetProperty("releaseId", out var releaseId) && releaseId.ValueKind == JsonValueKind.String
                    ? releaseId.GetString()
                    : null,
                publicKeyId = root.TryGetProperty("publicKeyId", out var publicKeyId) && publicKeyId.ValueKind == JsonValueKind.String
                    ? publicKeyId.GetString()
                    : null,
                manifest = "[受控发布清单已保存]",
                signature = "[已隐藏]"
            });
        }

        return JsonSerializer.Serialize(SanitizePlatformOperationElement(document.RootElement));
    }
    catch (JsonException)
    {
        return JsonSerializer.Serialize(new { message = "运维任务详情格式无效，已拒绝返回原始内容。" });
    }
}

static object? SanitizePlatformOperationElement(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.Object => element.EnumerateObject().ToDictionary(
        item => item.Name,
        item => IsSensitivePlatformOperationProperty(item.Name) ? "[已脱敏]" : SanitizePlatformOperationElement(item.Value)),
    JsonValueKind.Array => element.EnumerateArray().Select(SanitizePlatformOperationElement).ToArray(),
    JsonValueKind.String => SanitizePlatformOperationText(element.GetString()),
    JsonValueKind.Number => element.GetRawText(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    _ => null
};

static bool IsSensitivePlatformOperationProperty(string name) =>
    name.Equals("user", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("username", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("account", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("login", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("cipher", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("manifestJson", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("signatureBase64", StringComparison.OrdinalIgnoreCase);

static string? SanitizePlatformOperationText(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var normalized = value.Trim()[..Math.Min(value.Trim().Length, 512)];
    normalized = Regex.Replace(
        normalized,
        @"(?i)\b(password|passwd|pwd|token|secret|authorization|ciphertext|credential|username|user|account)\b\s*[:=]\s*(?:\""[^\""\r\n]*\""|'[^'\r\n]*'|[^\s,;]+)",
        "$1=[已脱敏]");
    normalized = Regex.Replace(normalized, @"(?i)([a-z][a-z0-9+.-]*://)[^\s/@]+@", "$1[已脱敏]@");
    return Regex.Replace(
        normalized,
        @"(?i)([?&](?:password|passwd|pwd|token|access_token|secret|authorization|username|user|account)=)[^&#\s]+",
        "$1[已脱敏]");
}

static bool TryNormalizeJson(string? value, out string? normalized)
{
    normalized = null;
    if (string.IsNullOrWhiteSpace(value) || value.Length > 65_536)
    {
        return false;
    }
    try
    {
        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        if (ContainsRecoverableSecret(document.RootElement))
        {
            return false;
        }
        normalized = document.RootElement.GetRawText();
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}

static bool ContainsRecoverableSecret(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Array)
    {
        return element.EnumerateArray().Any(ContainsRecoverableSecret);
    }
    if (element.ValueKind != JsonValueKind.Object)
    {
        return false;
    }

    foreach (var property in element.EnumerateObject())
    {
        if (IsRecoverableSecretProperty(property.Name) || ContainsRecoverableSecret(property.Value))
        {
            return true;
        }
    }
    return false;
}

static bool IsRecoverableSecretProperty(string name) =>
    name.Equals("user", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("username", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("account", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("login", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("passwd", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("pwd", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
    name.EndsWith("Token", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("ciphertext", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("encryptedkey", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("authorization", StringComparison.OrdinalIgnoreCase);

app.MapMethods("/api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"], () => Results.Json(
    new
    {
        code = "api_route_not_found",
        message = "请求的 API 路径不存在，或当前中心服务尚未提供该接口。"
    },
    statusCode: StatusCodes.Status404NotFound))
    .WithOrder(1000);

if (Directory.Exists(app.Environment.WebRootPath) && File.Exists(Path.Combine(app.Environment.WebRootPath, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public sealed class DatabaseConfigurationMissing;
public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string Username, bool RequiresPasswordChange);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record StreamSessionRequest(CameraPermission Operation, string? Profile, int SlotNumber, Guid ClientRequestId);
public sealed record StreamSessionResponse(Guid Id, Uri GatewayUri, DateTimeOffset TicketExpiresAt, DateTimeOffset LeaseExpiresAt, int RenewAfterSeconds);
public sealed record StreamSessionRenewalResponse(Guid Id, DateTimeOffset LeaseExpiresAt, int RenewAfterSeconds);
public sealed record PlaybackSessionResponse(
    Guid Id,
    string Status,
    Guid? StartCommandId,
    string? FailureKind,
    int Attempts,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    DateTimeOffset LeaseExpiresAt,
    PlaybackTransportResponse Transport);
public sealed record PlaybackTransportResponse(
    string Status,
    bool IsPaused,
    DateTimeOffset Position,
    double Speed,
    bool CanPause,
    bool CanSeek,
    bool CanChangeSpeed,
    string? Detail,
    Guid? CommandId);
public sealed record PtzLeaseResponse(Guid LeaseId, DateTimeOffset ExpiresAt, long LastSequence, string LeaseToken);
public sealed record PtzCommandRequest(Guid LeaseId, string LeaseToken, PtzAction Action, PtzMotion Motion, int Speed, long Sequence);
public sealed record PtzCommandResponse(Guid CommandId, DateTimeOffset LeaseExpiresAt, long LastSequence);
public sealed record CreatePlaybackExportAdminRequest(Guid CameraId, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Container);
public sealed record ExportArtifactResponse(Guid Id, string FileName, long SizeBytes, string Sha256, DateTimeOffset ExpiresAt);
public sealed record PlaybackExportResponse(Guid Id, Guid CameraId, string Status, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Container, DateTimeOffset RequestedAt, ExportArtifactResponse? Artifact, string? FailureCode);
public sealed record ExportCameraResponse(Guid Id, string Code, string Alias, Guid RegionId, CameraConnectivity Connectivity);
public sealed record GatewayTicketConsumeRequest(string Ticket, string GatewayName);
public sealed record GatewaySessionStatusRequest(IReadOnlyList<Guid> SessionIds);
public sealed record CameraResponse(
    Guid Id,
    string Code,
    string Alias,
    Guid RegionId,
    bool SupportsPtz,
    CameraConnectivity Connectivity,
    bool CanLiveView = false,
    bool CanPlayback = false,
    bool CanControlPtz = false);
public sealed record CreateRegionRequest(Guid? ParentId, string Code, string Name);
public sealed record UpdateRegionRequest(Guid? ParentId, string Code, string Name);
public sealed record CreateRecorderEndpointRequest(RecorderEndpointProtocol Protocol, string Host, int Port, bool UseTls, string? CertificateThumbprint, string CredentialReference);
public sealed record CreateRecorderRequest(
    string Code,
    string Name,
    string? Vendor,
    string? Model,
    string? AdapterType,
    string TimeZoneId,
    IReadOnlyList<CreateRecorderEndpointRequest> Endpoints,
    Guid? DevicePluginId = null,
    string? DeviceKind = null,
    string? SerialNumber = null,
    string? FirmwareVersion = null,
    string? Description = null,
    string? ConfigurationJson = null);
public sealed record RecorderResponse(RecorderEntity Recorder, IReadOnlyList<RecorderEndpointEntity> Endpoints);
public sealed record UpdateRecorderRequest(
    string Code,
    string Name,
    string? Model,
    string TimeZoneId,
    string? Vendor = null,
    Guid? DevicePluginId = null,
    string? DeviceKind = null,
    string? SerialNumber = null,
    string? FirmwareVersion = null,
    string? Description = null,
    string? ConfigurationJson = null,
    IReadOnlyList<CreateRecorderEndpointRequest>? Endpoints = null);
public sealed record CreateCameraRequest(
    Guid? RecorderId,
    Guid RegionId,
    string Code,
    string Alias,
    int InputChannelNumber,
    string? StreamingChannelMap,
    bool SupportsPtz,
    string? SourceType = null,
    Guid? DevicePluginId = null,
    Guid? WorkerId = null,
    string? MainStreamUrl = null,
    string? SubStreamUrl = null,
    string? CredentialReference = null,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    string? Description = null,
    string? TimeZoneId = null,
    Guid? EdgeAgentId = null,
    Guid? CredentialId = null);
public sealed record UpdateCameraRequest(
    Guid RegionId,
    string Code,
    string Alias,
    int? InputChannelNumber = null,
    string? StreamingChannelMap = null,
    bool? SupportsPtz = null,
    string? MainStreamUrl = null,
    string? SubStreamUrl = null,
    string? CredentialReference = null,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    string? Description = null,
    Guid? DevicePluginId = null,
    Guid? WorkerId = null,
    string? TimeZoneId = null,
    Guid? EdgeAgentId = null,
    Guid? CredentialId = null);
public sealed record AdminCameraResponse(
    Guid Id,
    Guid RecorderId,
    Guid RegionId,
    string Code,
    string Alias,
    int InputChannelNumber,
    string StreamingChannelMap,
    bool SupportsPtz,
    CameraConnectivity Connectivity,
    DateTimeOffset? LastVerifiedAt,
    string SourceType,
    string ProvisioningMode,
    Guid? DevicePluginId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Description,
    DateTimeOffset CreatedAt,
    string? MainStreamUrl,
    string? SubStreamUrl,
    string? CredentialReference,
    Guid? WorkerId,
    string? TimeZoneId,
    Guid? CredentialId = null,
    Guid? EdgeAgentId = null);
public sealed record InstallDevicePluginRequest(DevicePluginManifest Manifest);
public sealed record SetDevicePluginStatusRequest(bool Enabled);
public sealed record DevicePluginResponse(
    Guid Id,
    string Key,
    string Name,
    string Version,
    string ProtocolType,
    string RuntimeType,
    string AdapterType,
    string? Vendor,
    string? Description,
    string PackageHash,
    bool IsBuiltIn,
    bool Enabled,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    int UsageCount,
    DevicePluginManifest Manifest);
public sealed record RoleCameraScopeRequest(Guid? RegionId, Guid? CameraId, CameraPermission Permissions);
public sealed record RoleCameraScopeResponse(Guid Id, Guid? RegionId, Guid? CameraId, CameraPermission Permissions);
public sealed record ReplaceRoleCameraScopesRequest(IReadOnlyList<RoleCameraScopeRequest> Scopes);
public sealed record CreateRoleRequest(string Code, string Name, long SystemPermissions);
public sealed record UpdateRoleSystemPermissionsRequest(long SystemPermissions);
public sealed record RoleResponse(Guid Id, string Code, string Name, long SystemPermissions, IReadOnlyList<RoleCameraScopeResponse> CameraScopes);
public sealed record CreateUserRequest(string Username, IReadOnlyList<Guid>? RoleIds, bool IsSystemAdministrator);
public sealed record UserResponse(Guid Id, string Username, bool IsSystemAdministrator, DateTimeOffset? DisabledAt, bool RequiresPasswordChange, IReadOnlyList<Guid> RoleIds);
public sealed record ReplaceUserRolesRequest(IReadOnlyList<Guid>? RoleIds);
public sealed record SetUserStatusRequest(bool Disabled);
public sealed record CreateDeviceCredentialRequest(
    string Name,
    DeviceCredentialProtectionMode ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope,
    string? CiphertextBase64 = null,
    string? KeyVersion = null,
    DeviceCredentialEnvelopeInput? Envelope = null,
    IReadOnlyList<DeviceCredentialEnvelopeInput>? Envelopes = null);
public sealed record RotateDeviceCredentialRequest(
    DeviceCredentialProtectionMode ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope,
    string? CiphertextBase64 = null,
    string? KeyVersion = null,
    DeviceCredentialEnvelopeInput? Envelope = null,
    IReadOnlyList<DeviceCredentialEnvelopeInput>? Envelopes = null);
public sealed record DeviceCredentialResponse(
    Guid Id,
    string Name,
    DeviceCredentialProtectionMode ProtectionMode,
    string KeyVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? DisabledAt = null,
    int? ActiveVersion = null,
    IReadOnlyList<Guid>? AgentIds = null,
    int UsageCount = 0,
    DateTimeOffset? LastVerifiedAt = null,
    string? LastVerificationError = null);
public sealed record SetDeviceCredentialStatusRequest(bool Disabled);
public sealed record SetEdgeAgentStatusRequest(bool Disabled);
public sealed record RequestEdgeAgentDiagnosticRequest(string Kind);
public sealed record RequestPlatformDiagnosticRequest(Guid EdgeAgentId, string Kind);
public sealed record UpdateEdgeAgentConfigurationRequest(string ConfigurationJson);
public sealed record DirectCameraPreflightRequest(Guid EdgeAgentId, Guid CredentialId, string MainStreamUrl, string? SubStreamUrl = null);
public sealed record RequestPlatformDeploymentRequest(Guid EdgeAgentId, string ReleaseId, string ManifestJson, string SignatureBase64, string PublicKeyId);
public sealed record PlatformOperationResponse(
    Guid Id,
    Guid? EdgeAgentId,
    string OperationType,
    string Status,
    string Summary,
    string DetailsJson,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);
public sealed record CreateDeviceWorkerRequest(string Name);
public sealed record CreateDeviceWorkerAssignmentRequest(Guid RecorderId, Guid DefaultRegionId);
public sealed record DeviceWorkerAssignmentResponse(Guid RecorderId, Guid DefaultRegionId);
public sealed record DeviceWorkerResponse(Guid Id, string Name, DateTimeOffset? DisabledAt, DateTimeOffset? LastSeenAt, IReadOnlyList<DeviceWorkerAssignmentResponse> Assignments);
public sealed record DeviceWorkerOperationStatusResponse(
    Guid WorkerId,
    Guid RecorderId,
    string OperationType,
    bool IsReady,
    string? FailureKind,
    DateTimeOffset ReportedAt,
    bool IsEffective,
    DateTimeOffset? WorkerLastSeenAt,
    DateTimeOffset? WorkerDisabledAt);
public sealed record RecorderOperationRouteResponse(bool IsReady, Guid? WorkerId, string? CommandType);
public sealed record RecorderOperationRoutesResponse(RecorderOperationRouteResponse RecordingSearch, RecorderOperationRouteResponse PlaybackRelay);
public sealed record CreatedDeviceWorkerResponse(Guid Id, string Name, string EnrollmentToken);
public sealed record SetDeviceWorkerStatusRequest(bool Disabled);
public sealed record RotatedDeviceWorkerTokenResponse(Guid Id, string EnrollmentToken);
public sealed record RecordingSearchResponse(
    Guid Id,
    Guid CameraId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int MaxResults,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset ExpiresAt,
    string? FailureKind,
    JsonElement? Result,
    IReadOnlyList<RecordingSegmentResponse> Segments);
public sealed record RecordingSegmentResponse(
    string VendorSegmentId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long SizeBytes,
    bool IsLocked,
    string SegmentType,
    bool CoverageApproximate);
public sealed record CreateNotificationChannelRequest(
    string Name,
    NotificationChannelType Type,
    JsonElement Configuration,
    string? SecretReference = null,
    string? WebhookUrl = null);
public sealed record UpdateNotificationChannelRequest(
    string Name,
    JsonElement Configuration,
    string? SecretReference = null,
    string? WebhookUrl = null);
public sealed record NotificationChannelResponse(
    Guid Id,
    string Name,
    NotificationChannelType Type,
    JsonElement Configuration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SecretReference,
    bool Enabled,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? WebhookConfigured);
public sealed record NotificationChannelTestQueuedResponse(Guid EventId, DateTimeOffset QueuedAt);
public sealed record SetEnabledRequest(bool Enabled);
public sealed record CreateAlertRuleRequest(string Name, string ResourceType, Guid? RegionId, bool NotifyOnRecovery, IReadOnlyList<Guid>? NotificationChannelIds);
public sealed record AlertRuleResponse(Guid Id, string Name, string ResourceType, Guid? RegionId, bool NotifyOnRecovery, bool Enabled, IReadOnlyList<Guid> NotificationChannelIds);
