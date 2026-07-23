using Microsoft.AspNetCore.Http;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Setup;

/// <summary>
/// 首次安装前即可访问的系统和安装端点。
/// </summary>
public sealed class SystemSetupEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Bootstrap;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/system/version", () => Results.Ok(new
        {
            version = RuntimeVersion.ProductVersion,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        }));

        endpoints.MapGet("/api/v1/setup/status", (InstallationService installationService) =>
        {
            var status = installationService.GetStatus();
            return Results.Ok(new
            {
                state = status.State.ToString().ToLowerInvariant(),
                defaults = status.Defaults
            });
        });

        endpoints.MapPost("/api/v1/setup/initialize", async (
            InstallationRequest request,
            HttpContext context,
            InstallationService installationService,
            IHostApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBrowserOrigin(context.Request, out var browserOrigin))
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "初始化来源无效", "setup_origin_invalid", "请从当前视枢初始化页面提交配置，不要通过跨域请求调用此接口。");
            }

            try
            {
                var installation = await installationService.InitializeAsync(request, browserOrigin, cancellationToken);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    applicationLifetime.StopApplication();
                });
                return Results.Accepted("/api/v1/setup/status", new { state = "restarting", recoveryKey = installation.RecoveryKey });
            }
            catch (InstallationConflictException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "初始化已拒绝", "setup_conflict", exception.Message);
            }
            catch (InstallationValidationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "初始化参数无效", "setup_validation_failed", exception.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, "初始化已取消", "setup_cancelled", "初始化请求已取消，未保存运行配置。请检查核心容器运行状态后重试。");
            }
            catch (Exception)
            {
                // 初始化请求可能包含管理员密码；日志仅保留固定事件，不输出异常对象或请求内容。
                loggerFactory.CreateLogger("VisiCore.Api.Setup").LogWarning("首次初始化未完成，运行配置未写入。");
                return ApiProblems.Create(StatusCodes.Status502BadGateway, "初始化失败", "setup_failed", "无法完成内置服务校验或数据库初始化。请检查核心容器状态后重试。");
            }
        });

        endpoints.MapPost("/api/v1/setup/restore", async (
            IFormFile backup,
            string recoveryKey,
            HttpContext context,
            InstallationService installationService,
            BootstrapRestoreService restoreService,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBrowserOrigin(context.Request, out _))
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "初始化来源无效", "setup_origin_invalid", "请从当前视枢初始化页面提交恢复请求。");
            }
            if (installationService.GetStatus().State != InstallationState.Unconfigured)
            {
                return Results.Conflict(new { message = "当前核心已完成初始化，恢复请从后台备份页执行。" });
            }
            try
            {
                await restoreService.StageAsync(backup, recoveryKey, cancellationToken);
                return Results.Accepted("/api/v1/setup/status", new { state = "restarting" });
            }
            catch (InvalidDataException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "恢复包无效", "setup_restore_invalid", exception.Message, new Dictionary<string, string[]> { ["backup"] = [exception.Message] });
            }
            catch (InvalidOperationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "恢复已拒绝", "setup_restore_conflict", exception.Message);
            }
        });
    }

    private static bool TryGetBrowserOrigin(HttpRequest request, out Uri browserOrigin)
    {
        browserOrigin = null!;
        var suppliedOrigin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(suppliedOrigin) ||
            !Uri.TryCreate(suppliedOrigin, UriKind.Absolute, out var parsedOrigin) ||
            !string.IsNullOrEmpty(parsedOrigin.UserInfo) ||
            !string.IsNullOrEmpty(parsedOrigin.Query) ||
            !string.IsNullOrEmpty(parsedOrigin.Fragment))
        {
            return false;
        }

        browserOrigin = new Uri(parsedOrigin.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
        return true;
    }
}
