using Microsoft.AspNetCore.Http;
using VisiCore.Api;
using VisiCore.Core;

/// <summary>
/// Linux 和 Windows Edge Agent 与中心之间的控制面端点。
/// </summary>
public sealed class EdgeAgentEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var edgeAgents = endpoints.MapGroup("/api/v1/edge-agents");
        edgeAgents.MapPost("/enroll", async (EnrollEdgeAgentRequest request, EdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await edgeAgentControl.EnrollAsync(request, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "边缘节点注册请求无效", "edge_enrollment_invalid", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "边缘节点注册冲突", "edge_enrollment_conflict", exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return ApiProblems.Create(StatusCodes.Status403Forbidden, "设备插件签名不受信任", "edge_plugin_untrusted", exception.Message);
            }
        });

        edgeAgents.MapPost("/{agentId:guid}/heartbeat", async (Guid agentId, EdgeAgentHeartbeatRequest request, HttpRequest httpRequest, EdgeAgentControlService edgeAgentControl, PlatformTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
            if (agent is null) return Results.Unauthorized();
            using var activity = telemetry.StartActivity("visicore.edge.heartbeat");
            activity?.SetTag("visicore.edge.platform", agent.Platform);
            try
            {
                await edgeAgentControl.HeartbeatAsync(agent, request, cancellationToken);
                telemetry.RecordEdgeHeartbeat(agent.Platform);
                activity?.SetTag("visicore.edge.heartbeat.accepted", true);
                return Results.NoContent();
            }
            catch (ArgumentException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "边缘节点心跳无效", "edge_heartbeat_invalid", exception.Message);
            }
        });

        edgeAgents.MapGet("/{agentId:guid}/configuration", async (Guid agentId, HttpRequest httpRequest, EdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
        {
            var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
            return agent is null ? Results.Unauthorized() : Results.Ok(await edgeAgentControl.GetConfigurationAsync(agent, cancellationToken));
        });

        edgeAgents.MapPost("/{agentId:guid}/configuration-status", async (Guid agentId, EdgeAgentConfigurationStatusReport report, HttpRequest httpRequest, EdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
        {
            var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
            if (agent is null) return Results.Unauthorized();
            try
            {
                await edgeAgentControl.ReportConfigurationStatusAsync(agent, report, cancellationToken);
                return Results.NoContent();
            }
            catch (ArgumentException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "边缘节点配置回执无效", "edge_configuration_status_invalid", exception.Message);
            }
        });

        edgeAgents.MapGet("/{agentId:guid}/credentials", async (Guid agentId, HttpRequest httpRequest, EdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
        {
            var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
            return agent is null ? Results.Unauthorized() : Results.Ok(await edgeAgentControl.GetCredentialEnvelopesAsync(agent, cancellationToken));
        });

        edgeAgents.MapGet("/{agentId:guid}/operations", async (Guid agentId, HttpRequest httpRequest, EdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
        {
            var agent = await edgeAgentControl.AuthenticateAsync(httpRequest, agentId, cancellationToken);
            return agent is null ? Results.Unauthorized() : Results.Ok(await edgeAgentControl.GetPendingOperationsAsync(agent, cancellationToken));
        });

        edgeAgents.MapPost("/{agentId:guid}/diagnostics", async (Guid agentId, EdgeAgentDiagnosticReport report, HttpRequest httpRequest, EdgeAgentControlService edgeAgentControl, CancellationToken cancellationToken) =>
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
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "边缘节点诊断回执无效", "edge_diagnostic_invalid", exception.Message);
            }
        });
    }
}
