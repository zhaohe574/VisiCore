using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisiCore.Core;

namespace VisiCore.EdgeAgent;

public sealed class EdgeAgentRuntimeWorker(
    EdgeAgentOptions options,
    EdgeAgentIdentityStore identityStore,
    EdgeAgentControlPlaneClient controlPlaneClient,
    EdgeAgentRuntimeState runtimeState,
    HostOperationWorker hostOperationWorker,
    HostOperationState hostOperationState,
    ILogger<EdgeAgentRuntimeWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, byte> completedOperations = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.TryGetControlPlaneBaseUri(out _, out var validationError))
        {
            runtimeState.SetFailure("invalid_configuration");
            logger.LogError("Edge Agent 控制面配置无效，失败类别 {FailureKind}。", "invalid_configuration");
            return;
        }

        EdgeAgentIdentityMaterial identity;
        try
        {
            identity = identityStore.LoadOrCreate();
        }
        catch (Exception exception)
        {
            runtimeState.SetFailure("identity_store_unavailable");
            logger.LogError("Edge Agent 身份状态不可用，失败类别 {FailureKind}。", exception.GetType().Name);
            return;
        }

        using (identity)
        {
            runtimeState.SetIdentity(
                identity.Identity.AgentId,
                identity.Identity.KeyId,
                identity.Identity.ConfigurationVersion);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!identity.Identity.IsEnrolled)
                    {
                        if (string.IsNullOrWhiteSpace(options.EnrollmentCode))
                        {
                            runtimeState.SetAwaitingEnrollment();
                            await DelayAsync(stoppingToken);
                            continue;
                        }

                        var enrollment = await controlPlaneClient.EnrollAsync(identity, options, stoppingToken);
                        if (!Guid.TryParse(enrollment.AgentId, out var enrolledAgentId) ||
                            !Guid.TryParse(identity.Identity.AgentId, out var localAgentId) ||
                            enrolledAgentId != localAgentId)
                        {
                            throw new EdgeControlPlaneException("agent_id_mismatch");
                        }
                        identity.UpdateEnrollment(enrollment.WorkerId, enrollment.WorkerToken, enrollment.ConfigurationVersion);
                        runtimeState.SetIdentity(identity.Identity.AgentId, identity.Identity.KeyId, identity.Identity.ConfigurationVersion);
                    }

                    await controlPlaneClient.SendHeartbeatAsync(
                        identity.Identity,
                        options,
                        runtimeState.Snapshot(),
                        stoppingToken);
                    var configuration = await controlPlaneClient.GetConfigurationAsync(identity.Identity, stoppingToken);
                    // 当前阶段只保存配置版本，不将未定义 schema 的配置或凭据内容写入磁盘、日志或诊断。
                    identity.UpdateConfigurationVersion(configuration.Version);
                    var credentialEnvelopes = await controlPlaneClient.GetCredentialEnvelopesAsync(identity.Identity, stoppingToken);
                    var credentials = DecryptCredentialEnvelopes(identity, credentialEnvelopes);
                    try
                    {
                        var credentialEnvelopeCount = credentials.Count;
                        runtimeState.SetRunning(identity.Identity.ConfigurationVersion, credentialEnvelopeCount);

                        var operations = await controlPlaneClient.GetOperationsAsync(identity.Identity, stoppingToken);
                        foreach (var operation in operations)
                        {
                            await ProcessOperationAsync(identity.Identity, operation, credentials, stoppingToken);
                        }
                    }
                    finally
                    {
                        // 明文对象只在当前同步轮次存活，随后移除全部引用。
                        credentials.Clear();
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    var failureKind = exception is EdgeControlPlaneException controlPlaneException
                        ? controlPlaneException.FailureKind
                        : exception.GetType().Name;
                    runtimeState.SetFailure(failureKind);
                    // 禁止记录 HTTP 响应、请求地址、令牌、凭据或加密信封。
                    logger.LogWarning("Edge Agent 与控制面同步失败，失败类别 {FailureKind}。", failureKind);
                }

                await DelayAsync(stoppingToken);
            }
        }
    }

    private async Task ProcessOperationAsync(
        EdgeAgentIdentity identity,
        EdgeOperation operation,
        IReadOnlyDictionary<Guid, AgentCredentialPayload> credentials,
        CancellationToken cancellationToken)
    {
        if (completedOperations.ContainsKey(operation.Id))
        {
            return;
        }

        EdgeDiagnostic diagnostic;
        if (operation.OperationType.Equals("diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = BuildDiagnostic(operation);
        }
        else if (operation.OperationType.Equals("direct-rtsp.probe", StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = await ProbeDirectRtspAsync(operation, credentials, cancellationToken);
        }
        else if (operation.OperationType.Equals("deployment", StringComparison.OrdinalIgnoreCase) ||
                 operation.OperationType.Equals("rollback", StringComparison.OrdinalIgnoreCase))
        {
            var result = await hostOperationWorker.ValidateAndExecuteAsync(operation, cancellationToken);
            diagnostic = new EdgeDiagnostic(
                operation.OperationType.ToLowerInvariant(),
                JsonSerializer.Serialize(new { reason = result.FailureKind ?? "completed" }),
                result.Succeeded,
                operation.Id);
        }
        else
        {
            diagnostic = new EdgeDiagnostic(
                "operation-rejected",
                JsonSerializer.Serialize(new { reason = "unsupported_operation" }),
                false,
                operation.Id);
        }

        await controlPlaneClient.SendDiagnosticAsync(identity, diagnostic, cancellationToken);
        completedOperations.TryAdd(operation.Id, 0);
    }

    private EdgeDiagnostic BuildDiagnostic(EdgeOperation operation)
    {
        var requestedKind = "runtime";
        if (!string.IsNullOrWhiteSpace(operation.DetailsJson))
        {
            try
            {
                using var details = JsonDocument.Parse(operation.DetailsJson);
                if (details.RootElement.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
                {
                    requestedKind = kind.GetString() ?? requestedKind;
                }
            }
            catch (JsonException)
            {
                return new EdgeDiagnostic(
                    "diagnostic-rejected",
                    JsonSerializer.Serialize(new { reason = "invalid_details" }),
                    false,
                    operation.Id);
            }
        }

        if (!requestedKind.Equals("runtime", StringComparison.OrdinalIgnoreCase) &&
            !requestedKind.Equals("health", StringComparison.OrdinalIgnoreCase) &&
            !requestedKind.Equals("identity", StringComparison.OrdinalIgnoreCase) &&
            !requestedKind.Equals("host-health", StringComparison.OrdinalIgnoreCase))
        {
            return new EdgeDiagnostic(
                "diagnostic-rejected",
                JsonSerializer.Serialize(new { reason = "unsupported_diagnostic" }),
                false,
                operation.Id);
        }

        var snapshot = runtimeState.Snapshot();
        return new EdgeDiagnostic(
            requestedKind.ToLowerInvariant(),
            JsonSerializer.Serialize(new
            {
                snapshot.Status,
                snapshot.AgentId,
                snapshot.KeyId,
                snapshot.ConfigurationVersion,
                snapshot.CredentialEnvelopeCount,
                snapshot.LastHeartbeatAt,
                snapshot.LastFailureKind,
                hostAgent = hostOperationState.Snapshot()
            }),
            true,
            operation.Id);
    }

    private static async Task<EdgeDiagnostic> ProbeDirectRtspAsync(
        EdgeOperation operation,
        IReadOnlyDictionary<Guid, AgentCredentialPayload> credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(operation.DetailsJson))
            {
                return RejectedDirectRtspProbe(operation.Id, "invalid_details");
            }

            using var details = JsonDocument.Parse(operation.DetailsJson);
            var root = details.RootElement;
            var host = root.TryGetProperty("host", out var hostValue) && hostValue.ValueKind == JsonValueKind.String
                ? hostValue.GetString()
                : null;
            var port = root.TryGetProperty("port", out var portValue) && portValue.TryGetInt32(out var parsedPort)
                ? parsedPort
                : 0;
            var useTls = root.TryGetProperty("useTls", out var tlsValue) && tlsValue.ValueKind is JsonValueKind.True or JsonValueKind.False && tlsValue.GetBoolean();
            var credentialId = root.TryGetProperty("credentialId", out var credentialValue) && credentialValue.ValueKind == JsonValueKind.String
                ? credentialValue.GetString()
                : null;
            if (!RecorderEndpointHostPolicy.IsValidHost(host) || port is < 1 or > 65535 ||
                !Guid.TryParse(credentialId, out var parsedCredentialId))
            {
                return RejectedDirectRtspProbe(operation.Id, "invalid_details");
            }
            if (!credentials.TryGetValue(parsedCredentialId, out _))
            {
                return RejectedDirectRtspProbe(operation.Id, "credential_unavailable");
            }

            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(host!, port, timeout.Token);
            Stream transport = client.GetStream();
            SslStream? tlsStream = null;
            if (useTls)
            {
                tlsStream = new SslStream(transport, leaveInnerStreamOpen: false);
                await tlsStream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = host! },
                    timeout.Token);
                transport = tlsStream;
            }

            try
            {
                // 认证由后续协议运行时负责；预检只证明已解封凭据绑定、传输连通和 RTSP 协议响应。
                var requestBytes = Encoding.ASCII.GetBytes("OPTIONS * RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: VisiCore-EdgeAgent\r\n\r\n");
                try
                {
                    await transport.WriteAsync(requestBytes, timeout.Token);
                    await transport.FlushAsync(timeout.Token);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(requestBytes);
                }

                var responseBuffer = new byte[1024];
                var read = await transport.ReadAsync(responseBuffer, timeout.Token);
                try
                {
                    if (read == 0)
                    {
                        return RejectedDirectRtspProbe(operation.Id, "protocol_no_response");
                    }
                    var response = Encoding.ASCII.GetString(responseBuffer, 0, read);
                    if (!response.StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase))
                    {
                        return RejectedDirectRtspProbe(operation.Id, "protocol_invalid");
                    }
                    return new EdgeDiagnostic(
                        "direct-rtsp.probe",
                        JsonSerializer.Serialize(new { reachable = true, protocolResponded = true, transport = useTls ? "rtsps" : "rtsp" }),
                        true,
                        operation.Id);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(responseBuffer);
                }
            }
            finally
            {
                tlsStream?.Dispose();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EdgeDiagnostic(
                "direct-rtsp.probe",
                JsonSerializer.Serialize(new { reachable = false, reason = "connect_timeout" }),
                false,
                operation.Id);
        }
        catch (SocketException)
        {
            return new EdgeDiagnostic(
                "direct-rtsp.probe",
                JsonSerializer.Serialize(new { reachable = false, reason = "connect_failed" }),
                false,
                operation.Id);
        }
        catch (AuthenticationException)
        {
            return RejectedDirectRtspProbe(operation.Id, "tls_handshake_failed");
        }
        catch (JsonException)
        {
            return RejectedDirectRtspProbe(operation.Id, "invalid_details");
        }
    }

    private static EdgeDiagnostic RejectedDirectRtspProbe(Guid operationId, string reason) =>
        new("direct-rtsp.probe", JsonSerializer.Serialize(new { reachable = false, reason }), false, operationId);

    private Task DelayAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds), cancellationToken);

    private static Dictionary<Guid, AgentCredentialPayload> DecryptCredentialEnvelopes(
        EdgeAgentIdentityMaterial identity,
        IReadOnlyList<EdgeCredentialEnvelope> credentialEnvelopes)
    {
        if (!Guid.TryParseExact(identity.Identity.AgentId, "N", out var agentId))
        {
            throw new EdgeControlPlaneException("invalid_agent_identity");
        }

        var credentials = new Dictionary<Guid, AgentCredentialPayload>();
        foreach (var received in credentialEnvelopes)
        {
            var envelope = received.Envelope with { AgentId = agentId };
            if (!string.Equals(envelope.KeyId, identity.Identity.KeyId, StringComparison.Ordinal) ||
                !AgentCredentialEnvelopeCryptography.TryValidate(envelope, out _))
            {
                // 只暴露固定失败类别，禁止将密文、用户名或密码带入日志与诊断。
                throw new EdgeControlPlaneException("credential_envelope_invalid");
            }

            try
            {
                var credential = AgentCredentialEnvelopeCryptography.Decrypt(
                    envelope,
                    agentId,
                    received.CredentialVersionId,
                    identity.PrivateKey);
                if (!credentials.TryAdd(received.CredentialId, credential))
                {
                    throw new EdgeControlPlaneException("credential_envelope_invalid");
                }
            }
            catch (CryptographicException)
            {
                // 只暴露固定失败类别，禁止将密文、用户名或密码带入日志与诊断。
                throw new EdgeControlPlaneException("credential_envelope_invalid");
            }
        }

        return credentials;
    }
}

public sealed class EdgeAgentLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(HealthCheckResult.Healthy("Edge Agent 进程正在运行。"));
}

public sealed class EdgeAgentReadinessHealthCheck(EdgeAgentRuntimeState runtimeState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var snapshot = runtimeState.Snapshot();
        return Task.FromResult(snapshot.Status switch
        {
            "running" => HealthCheckResult.Healthy("Edge Agent 已完成控制面同步。"),
            "awaiting_enrollment" => HealthCheckResult.Degraded("Edge Agent 等待后台平台注册。"),
            _ => HealthCheckResult.Degraded("Edge Agent 尚未完成控制面同步。")
        });
    }
}
