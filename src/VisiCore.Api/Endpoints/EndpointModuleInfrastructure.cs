using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VisiCore.Api;
using VisiCore.Core;

/// <summary>
/// 标识端点应在首次安装前还是平台初始化完成后注册。
/// </summary>
public enum EndpointModulePhase
{
    Bootstrap,
    Configured
}

/// <summary>
/// API 业务域的端点注册契约。新增业务域只需新增实现，不需要继续扩展 Program.cs。
/// </summary>
public interface IApiEndpointModule
{
    EndpointModulePhase Phase { get; }

    void Map(IEndpointRouteBuilder endpoints);
}

public static class EndpointModuleExtensions
{
    public static void MapEndpointModules(this IEndpointRouteBuilder endpoints, EndpointModulePhase phase)
    {
        var modules = typeof(EndpointModuleExtensions).Assembly
            .DefinedTypes
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IApiEndpointModule).IsAssignableFrom(type))
            .Select(type => (IApiEndpointModule)Activator.CreateInstance(type.AsType())!)
            .Where(module => module.Phase == phase)
            .OrderBy(module => module.GetType().FullName, StringComparer.Ordinal);

        foreach (var module in modules)
        {
            module.Map(endpoints);
        }
    }
}

/// <summary>
/// 统一最小 API 错误模型，客户端可稳定读取 code、title、status 和 traceId。
/// </summary>
public static class ApiProblems
{
    public static IResult Create(
        int statusCode,
        string title,
        string code,
        string? detail = null,
        IDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code
        };

        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(
            detail: detail,
            statusCode: statusCode,
            title: title,
            type: $"https://visicore.dev/problems/{code}",
            extensions: extensions);
    }

    public static async Task WriteAsync(HttpContext context, int statusCode, string title, string code)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = $"https://visicore.dev/problems/{code}",
            Title = title,
            Status = statusCode,
            Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
        });
    }
}

/// <summary>
/// 为新管理端点提供声明式系统权限校验，避免在每个处理器内重复手写授权分支。
/// </summary>
public static class PermissionEndpointExtensions
{
    public static RouteGroupBuilder RequireSystemAdministrator(this RouteGroupBuilder builder)
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var principal = context.HttpContext.User;
            var accessService = context.HttpContext.RequestServices.GetRequiredService<PlatformAccessService>();
            var user = await accessService.FindUserAsync(principal, context.HttpContext.RequestAborted);
            if (!user.IsSystemAdministrator)
            {
                return Results.Forbid();
            }

            return await next(context);
        });
        return builder;
    }

    public static RouteGroupBuilder RequireSystemPermission(this RouteGroupBuilder builder, SystemPermission permission)
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var accessService = context.HttpContext.RequestServices.GetRequiredService<PlatformAccessService>();
            var cancellationToken = context.HttpContext.RequestAborted;
            if (!await accessService.HasSystemPermissionAsync(context.HttpContext.User, permission, cancellationToken))
            {
                return Results.Forbid();
            }

            return await next(context);
        });
        return builder;
    }

    public static RouteHandlerBuilder RequireSystemPermission(this RouteHandlerBuilder builder, SystemPermission permission)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var accessService = context.HttpContext.RequestServices.GetRequiredService<PlatformAccessService>();
            var cancellationToken = context.HttpContext.RequestAborted;
            if (!await accessService.HasSystemPermissionAsync(context.HttpContext.User, permission, cancellationToken))
            {
                return Results.Forbid();
            }

            return await next(context);
        });
    }

    /// <summary>
    /// 为新增写端点附加统一审计。既有端点保留原有的业务审计详情，迁移时不重复写入。
    /// </summary>
    public static RouteHandlerBuilder WithAudit(
        this RouteHandlerBuilder builder,
        string action,
        string resourceType,
        Func<EndpointFilterInvocationContext, Guid?> resourceId)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var statusCode = (result as IStatusCodeHttpResult)?.StatusCode;
            if (statusCode is null or >= StatusCodes.Status400BadRequest)
            {
                return result;
            }

            var auditService = context.HttpContext.RequestServices.GetRequiredService<AuditService>();
            await auditService.WriteAsync(
                context.HttpContext.User,
                action,
                resourceType,
                resourceId(context) ?? Guid.Empty,
                new { path = context.HttpContext.Request.Path.Value },
                context.HttpContext.RequestAborted);
            return result;
        });
    }
}
