using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

/// <summary>
/// 双形态边缘 Agent 的配对、配置和受控运维控制面。设备凭据的可解封内容不会进入此服务。
/// </summary>
public class EdgeAgentControlService(PlatformDbContext dbContext)
{
    public async Task<CreatedEdgeAgentEnrollment> CreateEnrollmentAsync(
        string name,
        int? lifetimeMinutes,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeAgentName(name);
        var lifetime = Math.Clamp(lifetimeMinutes ?? 15, 5, 60);
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var enrollment = new EdgeAgentEnrollmentEntity
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            CodeHash = HashToken(code),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(lifetime)
        };
        dbContext.EdgeAgentEnrollments.Add(enrollment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreatedEdgeAgentEnrollment(enrollment.Id, enrollment.Name, code, enrollment.ExpiresAt);
    }

    public static string GetEnrollmentStatus(EdgeAgentEnrollmentEntity enrollment, DateTimeOffset now)
    {
        return enrollment.UsedAt is not null ? "used" : enrollment.ExpiresAt <= now ? "expired" : "available";
    }

    public async Task<EnrolledEdgeAgent> EnrollAsync(EnrollEdgeAgentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentCode) ||
            string.IsNullOrWhiteSpace(request.AgentVersion) ||
            request.PublicKey is null ||
            !TryNormalizePlatform(request.Platform, out var platform))
        {
            throw new ArgumentException("边缘节点注册请求无效。", nameof(request));
        }
        if (!TryValidatePublicKey(request.PublicKey, out var publicKeyError))
        {
            throw new ArgumentException(publicKeyError ?? "边缘节点注册请求无效。", nameof(request));
        }

        var enrollment = await dbContext.EdgeAgentEnrollments.SingleOrDefaultAsync(
            item => item.CodeHash == HashToken(request.EnrollmentCode), cancellationToken)
            ?? throw new InvalidOperationException("配对码不存在、已失效或已被使用。 ");
        var now = DateTimeOffset.UtcNow;
        if (enrollment.UsedAt is not null || enrollment.ExpiresAt <= now)
        {
            throw new InvalidOperationException("配对码不存在、已失效或已被使用。 ");
        }
        if (await dbContext.EdgeAgents.AnyAsync(item => item.Name == enrollment.Name || item.Id == request.PublicKey.AgentId, cancellationToken) ||
            await dbContext.DeviceWorkers.AnyAsync(item => item.Name == GetWorkerName(enrollment.Name), cancellationToken))
        {
            throw new InvalidOperationException("边缘节点名称已被使用。 ");
        }

        var workerToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var worker = new DeviceWorkerEntity
        {
            Id = Guid.NewGuid(),
            Name = GetWorkerName(enrollment.Name),
            TokenHash = HashToken(workerToken),
            CreatedAt = now,
            LastSeenAt = now
        };
        var agent = new EdgeAgentEntity
        {
            Id = request.PublicKey.AgentId,
            DeviceWorkerId = worker.Id,
            Name = enrollment.Name,
            Platform = platform,
            AgentVersion = request.AgentVersion.Trim(),
            PublicKeyId = request.PublicKey.KeyId.Trim(),
            SubjectPublicKeyInfoBase64 = request.PublicKey.SubjectPublicKeyInfoBase64.Trim(),
            CapabilitiesJson = NormalizeJson(request.CapabilitiesJson),
            ServiceStatusJson = "{}",
            ConfigurationVersion = 1,
            CreatedAt = now,
            LastSeenAt = now
        };
        dbContext.DeviceWorkers.Add(worker);
        dbContext.EdgeAgents.Add(agent);
        dbContext.EdgeAgentConfigurations.Add(new EdgeAgentConfigurationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = agent.Id,
            Version = 1,
            ConfigurationJson = "{}",
            Status = "applied",
            PublishedAt = now,
            AppliedAt = now
        });
        enrollment.UsedAt = now;
        enrollment.UsedByAgentId = agent.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new EnrolledEdgeAgent(agent.Id, worker.Id, workerToken, agent.ConfigurationVersion);
    }

    public async Task<ManagedEdgeAgent> RenameAsync(Guid agentId, string name, CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeAgentName(name);
        var agent = await dbContext.EdgeAgents.SingleOrDefaultAsync(item => item.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException("边缘节点不存在。 ");
        var worker = await dbContext.DeviceWorkers.SingleAsync(item => item.Id == agent.DeviceWorkerId, cancellationToken);
        var workerName = GetWorkerName(normalizedName);

        if (await dbContext.EdgeAgents.AnyAsync(item => item.Id != agentId && item.Name == normalizedName, cancellationToken) ||
            await dbContext.DeviceWorkers.AnyAsync(item => item.Id != worker.Id && item.Name == workerName, cancellationToken))
        {
            throw new InvalidOperationException("边缘节点名称已被使用。 ");
        }

        agent.Name = normalizedName;
        worker.Name = workerName;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ManagedEdgeAgent(agent.Id, agent.Name, worker.Id);
    }

    public async Task<ManagedEdgeAgent> SetStatusAsync(Guid agentId, bool disabled, CancellationToken cancellationToken)
    {
        var agent = await dbContext.EdgeAgents.SingleOrDefaultAsync(item => item.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException("边缘节点不存在。 ");
        var worker = await dbContext.DeviceWorkers.SingleAsync(item => item.Id == agent.DeviceWorkerId, cancellationToken);
        DateTimeOffset? disabledAt = disabled ? DateTimeOffset.UtcNow : null;
        agent.DisabledAt = disabledAt;
        worker.DisabledAt = disabledAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ManagedEdgeAgent(agent.Id, agent.Name, worker.Id);
    }

    public async Task<DeletedEdgeAgent> DeleteAsync(Guid agentId, string confirmationName, CancellationToken cancellationToken)
    {
        var agent = await dbContext.EdgeAgents.SingleOrDefaultAsync(item => item.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException("边缘节点不存在。 ");
        if (!string.Equals(agent.Name, confirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("确认名称与当前边缘节点名称不一致。", nameof(confirmationName));
        }

        var workerId = agent.DeviceWorkerId;
        var worker = await dbContext.DeviceWorkers.SingleAsync(item => item.Id == workerId, cancellationToken);
        var assignments = await dbContext.DeviceWorkerAssignments.Where(item => item.WorkerId == workerId).ToListAsync(cancellationToken);
        var operationStatuses = await dbContext.DeviceWorkerOperationStatuses.Where(item => item.WorkerId == workerId).ToListAsync(cancellationToken);
        var commands = await dbContext.EdgeCommands.Where(item => item.WorkerId == workerId).ToListAsync(cancellationToken);
        var envelopes = await dbContext.DeviceCredentialEnvelopes.Where(item => item.EdgeAgentId == agentId).ToListAsync(cancellationToken);
        var configurations = await dbContext.EdgeAgentConfigurations.Where(item => item.EdgeAgentId == agentId).ToListAsync(cancellationToken);
        var enrollments = await dbContext.EdgeAgentEnrollments.Where(item => item.UsedByAgentId == agentId).ToListAsync(cancellationToken);
        var operations = await dbContext.PlatformOperations.Where(item => item.EdgeAgentId == agentId).ToListAsync(cancellationToken);
        var upgradeTargets = await dbContext.UpgradeTargets.Where(item => item.EdgeAgentId == agentId).ToListAsync(cancellationToken);

        foreach (var enrollment in enrollments) enrollment.UsedByAgentId = null;
        foreach (var operation in operations) operation.EdgeAgentId = null;
        foreach (var target in upgradeTargets) target.EdgeAgentId = null;
        dbContext.DeviceWorkerAssignments.RemoveRange(assignments);
        dbContext.DeviceWorkerOperationStatuses.RemoveRange(operationStatuses);
        dbContext.EdgeCommands.RemoveRange(commands);
        dbContext.DeviceCredentialEnvelopes.RemoveRange(envelopes);
        dbContext.EdgeAgentConfigurations.RemoveRange(configurations);
        dbContext.EdgeAgents.Remove(agent);
        dbContext.DeviceWorkers.Remove(worker);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeletedEdgeAgent(agent.Id, agent.Name, assignments.Count, envelopes.Count, configurations.Count, commands.Count);
    }

    public async Task<EdgeAgentEntity?> AuthenticateAsync(HttpRequest request, Guid agentId, CancellationToken cancellationToken)
    {
        var token = request.Headers.Authorization.ToString();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..].Trim();
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            token = request.Headers["X-Device-Worker-Token"].ToString();
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = HashToken(token);
        var agent = await dbContext.EdgeAgents.SingleOrDefaultAsync(
            item => item.Id == agentId && item.DisabledAt == null,
            cancellationToken);
        if (agent is null)
        {
            return null;
        }
        var worker = await dbContext.DeviceWorkers.SingleOrDefaultAsync(
            item => item.Id == agent.DeviceWorkerId && item.TokenHash == tokenHash && item.DisabledAt == null,
            cancellationToken);
        if (worker is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        agent.LastSeenAt = now;
        worker.LastSeenAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return agent;
    }

    public async Task HeartbeatAsync(EdgeAgentEntity agent, EdgeAgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentVersion))
        {
            throw new ArgumentException("边缘节点版本不能为空。", nameof(request));
        }
        agent.AgentVersion = request.AgentVersion.Trim();
        agent.CapabilitiesJson = NormalizeJson(request.CapabilitiesJson);
        agent.ServiceStatusJson = NormalizeJson(request.ServiceStatusJson);
        agent.LastSeenAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<EdgeAgentConfigurationResponse> GetConfigurationAsync(EdgeAgentEntity agent, CancellationToken cancellationToken)
    {
        var configuration = await dbContext.EdgeAgentConfigurations.AsNoTracking()
            .Where(item => item.EdgeAgentId == agent.Id)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return configuration is null
            ? new EdgeAgentConfigurationResponse(agent.ConfigurationVersion, "{}", "applied")
            : new EdgeAgentConfigurationResponse(configuration.Version, configuration.ConfigurationJson, configuration.Status);
    }

    public async Task ReportConfigurationStatusAsync(
        EdgeAgentEntity agent,
        EdgeAgentConfigurationStatusReport report,
        CancellationToken cancellationToken)
    {
        if (report.Version < 1 ||
            (!report.Applied && (string.IsNullOrWhiteSpace(report.FailureKind) ||
                                !Regex.IsMatch(report.FailureKind, "^[a-z0-9_]{3,64}$"))))
        {
            throw new ArgumentException("边缘节点配置回执无效。", nameof(report));
        }

        var configuration = await dbContext.EdgeAgentConfigurations.SingleOrDefaultAsync(
            item => item.EdgeAgentId == agent.Id && item.Version == report.Version,
            cancellationToken);
        if (configuration is null)
        {
            throw new ArgumentException("边缘节点配置版本不存在。", nameof(report));
        }

        configuration.Status = report.Applied ? "applied" : "rejected";
        configuration.AppliedAt = report.Applied ? DateTimeOffset.UtcNow : null;
        configuration.FailureSummary = report.Applied ? null : report.FailureKind!.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EdgeAgentCredentialEnvelopeResponse>> GetCredentialEnvelopesAsync(
        EdgeAgentEntity agent,
        CancellationToken cancellationToken)
    {
        return await (
            from envelope in dbContext.DeviceCredentialEnvelopes.AsNoTracking()
            join version in dbContext.DeviceCredentialVersions.AsNoTracking() on envelope.CredentialVersionId equals version.Id
            join credential in dbContext.DeviceCredentials.AsNoTracking() on version.CredentialId equals credential.Id
            where envelope.EdgeAgentId == agent.Id && version.Status == "active" && credential.DisabledAt == null
            select new EdgeAgentCredentialEnvelopeResponse(
                credential.Id,
                credential.Name,
                version.Id,
                version.Version,
                envelope.KeyId,
                envelope.KeyEncryptionAlgorithm,
                envelope.ContentEncryptionAlgorithm,
                Convert.ToBase64String(envelope.EncryptedKey),
                Convert.ToBase64String(envelope.InitializationVector),
                Convert.ToBase64String(envelope.Ciphertext),
                Convert.ToBase64String(envelope.AuthenticationTag)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EdgeAgentOperationResponse>> GetPendingOperationsAsync(
        EdgeAgentEntity agent,
        CancellationToken cancellationToken)
    {
        return await dbContext.PlatformOperations
            .Where(item => item.EdgeAgentId == agent.Id && item.Status == "pending")
            .OrderBy(item => item.RequestedAt)
            .Take(10)
            .Select(item => new EdgeAgentOperationResponse(item.Id, item.OperationType, item.DetailsJson))
            .ToListAsync(cancellationToken);
    }

    public async Task ReportDiagnosticAsync(EdgeAgentEntity agent, EdgeAgentDiagnosticReport report, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(report.Kind) || report.Kind.Trim().Length > 64)
        {
            throw new ArgumentException("诊断类型无效。", nameof(report));
        }
        var now = DateTimeOffset.UtcNow;
        var safeResult = SanitizeDiagnosticJson(report.ResultJson);
        agent.LastDiagnosticAt = now;
        agent.LastDiagnosticSucceeded = report.Succeeded;
        agent.LastDiagnosticMessage = report.Succeeded ? null : SanitizeDiagnosticMessage(report.Message, 512);
        var verifiedCredentialId = report.CredentialId;
        if (report.OperationId is { } operationId)
        {
            var operation = await dbContext.PlatformOperations.SingleOrDefaultAsync(
                item => item.Id == operationId && item.EdgeAgentId == agent.Id && item.Status == "pending",
                cancellationToken);
            if (operation is not null)
            {
                verifiedCredentialId ??= TryReadCredentialId(operation.DetailsJson);
                operation.Status = report.Succeeded ? "succeeded" : "failed";
                operation.DetailsJson = safeResult;
                operation.CompletedAt = now;
            }
        }
        if (verifiedCredentialId is { } credentialId)
        {
            var credential = await dbContext.DeviceCredentials.SingleOrDefaultAsync(item => item.Id == credentialId, cancellationToken);
            if (credential is not null)
            {
                credential.LastVerifiedAt = now;
                credential.LastVerificationError = report.Succeeded ? null : SanitizeDiagnosticMessage(report.Message, 256);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlatformOperationEntity> ScheduleDiagnosticAsync(Guid edgeAgentId, string kind, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Trim().Length > 64 ||
            !await dbContext.EdgeAgents.AnyAsync(item => item.Id == edgeAgentId && item.DisabledAt == null, cancellationToken))
        {
            throw new ArgumentException("目标边缘节点或诊断类型无效。", nameof(edgeAgentId));
        }
        var operation = new PlatformOperationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = edgeAgentId,
            OperationType = "diagnostic",
            Status = "pending",
            Summary = $"边缘节点诊断：{kind.Trim()}",
            DetailsJson = JsonSerializer.Serialize(new { kind = kind.Trim() }),
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.PlatformOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    public async Task<PlatformOperationEntity> ScheduleDirectRtspProbeAsync(
        Guid edgeAgentId,
        Guid credentialId,
        string mainStreamUrl,
        string? subStreamUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mainStreamUrl) ||
            await ResolveCredentialBindingAsync(edgeAgentId, credentialId, cancellationToken) is null)
        {
            throw new ArgumentException("目标边缘节点、凭据或码流地址无效。", nameof(edgeAgentId));
        }
        var mainUri = new Uri(mainStreamUrl, UriKind.Absolute);
        var subUri = string.IsNullOrWhiteSpace(subStreamUrl) ? null : new Uri(subStreamUrl, UriKind.Absolute);
        var operation = new PlatformOperationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = edgeAgentId,
            OperationType = "direct-rtsp.probe",
            Status = "pending",
            Summary = "直连摄像头连通性预检",
            DetailsJson = JsonSerializer.Serialize(new
            {
                credentialId,
                host = mainUri.Host,
                port = mainUri.Port,
                useTls = mainUri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase),
                hasSubStream = subUri is not null
            }),
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.PlatformOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    public async Task<EdgeAgentCredentialBinding?> ResolveCredentialBindingAsync(
        Guid edgeAgentId,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var agent = await dbContext.EdgeAgents.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == edgeAgentId && item.DisabledAt == null,
            cancellationToken);
        if (agent is null)
        {
            return null;
        }
        var binding = await (
            from credential in dbContext.DeviceCredentials.AsNoTracking()
            join version in dbContext.DeviceCredentialVersions.AsNoTracking() on credential.Id equals version.CredentialId
            join envelope in dbContext.DeviceCredentialEnvelopes.AsNoTracking() on version.Id equals envelope.CredentialVersionId
            where credential.Id == credentialId && credential.DisabledAt == null && version.Status == "active" && envelope.EdgeAgentId == edgeAgentId
            select new EdgeAgentCredentialBinding(agent.DeviceWorkerId, credential.Id, credential.Name, version.Id))
            .SingleOrDefaultAsync(cancellationToken);
        return binding;
    }

    public static bool TryValidateEnvelope(DeviceCredentialEnvelopeInput? envelope, out string? error)
    {
        error = null;
        if (envelope is null)
        {
            error = "凭据加密信封缺少必要字段或算法不受支持。";
            return false;
        }
        return AgentCredentialEnvelopeCryptography.TryValidate(new AgentCredentialEnvelope(
            envelope.SchemaVersion,
            envelope.AgentId,
            envelope.CredentialVersionId,
            envelope.KeyId,
            envelope.KeyEncryptionAlgorithm,
            envelope.ContentEncryptionAlgorithm,
            envelope.EncryptedKeyBase64,
            envelope.InitializationVectorBase64,
            envelope.CiphertextBase64,
            envelope.AuthenticationTagBase64), out error);
    }

    public static DeviceCredentialEnvelopeEntity ToEnvelopeEntity(DeviceCredentialEnvelopeInput envelope, Guid versionId) => new()
    {
        Id = Guid.NewGuid(),
        CredentialVersionId = versionId,
        EdgeAgentId = envelope.AgentId,
        KeyId = envelope.KeyId.Trim(),
        KeyEncryptionAlgorithm = envelope.KeyEncryptionAlgorithm,
        ContentEncryptionAlgorithm = envelope.ContentEncryptionAlgorithm,
        EncryptedKey = Convert.FromBase64String(envelope.EncryptedKeyBase64),
        InitializationVector = Convert.FromBase64String(envelope.InitializationVectorBase64),
        Ciphertext = Convert.FromBase64String(envelope.CiphertextBase64),
        AuthenticationTag = Convert.FromBase64String(envelope.AuthenticationTagBase64),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static bool TryValidatePublicKey(EdgeAgentPublicKeyRequest publicKey, out string? error)
    {
        error = null;
        return AgentCredentialEnvelopeCryptography.TryValidatePublicKey(new AgentPublicKeyContract(
            publicKey.AgentId,
            publicKey.KeyId,
            publicKey.KeyEncryptionAlgorithm,
            publicKey.SubjectPublicKeyInfoBase64), out error);
    }

    private static string NormalizeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "{}";
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            throw new ArgumentException("JSON 配置格式无效。", nameof(value));
        }
    }

    private static string SanitizeDiagnosticJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "{}";
        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(SanitizeElement(document.RootElement));
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { message = "诊断结果格式无效，已拒绝保留原始内容。" });
        }
    }

    private static Guid? TryReadCredentialId(string detailsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            return document.RootElement.TryGetProperty("credentialId", out var value) && value.TryGetGuid(out var credentialId)
                ? credentialId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? SanitizeElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            item => item.Name,
            item => IsSensitiveName(item.Name) ? "[已脱敏]" : SanitizeElement(item.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(SanitizeElement).ToArray(),
        JsonValueKind.String => SanitizeDiagnosticMessage(element.GetString(), 512),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private static bool IsSensitiveName(string name) =>
        name.Equals("user", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("username", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("account", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("login", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("cipher", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("authorization", StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static string? SanitizeDiagnosticMessage(string? value, int maxLength)
    {
        var normalized = Truncate(value, maxLength);
        if (normalized is null)
        {
            return null;
        }
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(password|passwd|pwd|token|secret|authorization|ciphertext|credential|username|user|account)\b\s*[:=]\s*(?:\""[^\""\r\n]*\""|'[^'\r\n]*'|[^\s,;]+)",
            "$1=[已脱敏]");
        normalized = Regex.Replace(normalized, @"(?i)([a-z][a-z0-9+.-]*://)[^\s/@]+@", "$1[已脱敏]@");
        normalized = Regex.Replace(
            normalized,
            @"(?i)([?&](?:password|passwd|pwd|token|access_token|secret|authorization|username|user|account)=)[^&#\s]+",
            "$1[已脱敏]");
        return normalized;
    }

    private static bool TryNormalizePlatform(string? platform, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }
        var value = platform.Trim().ToLowerInvariant();
        if (value == "linux" || value.StartsWith("linux-", StringComparison.Ordinal))
        {
            normalized = "linux";
            return true;
        }
        if (value == "windows" || value.StartsWith("windows-", StringComparison.Ordinal))
        {
            normalized = "windows";
            return true;
        }
        return false;
    }

    private static string NormalizeAgentName(string name)
    {
        const int maxAgentNameLength = 117;
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > maxAgentNameLength)
        {
            throw new ArgumentException($"边缘节点名称长度必须在 1 至 {maxAgentNameLength} 个字符之间。", nameof(name));
        }
        return name.Trim();
    }

    private static string GetWorkerName(string agentName) => $"edge-agent-{agentName}";

    private static string HashToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record EdgeAgentPublicKeyRequest(
    Guid AgentId,
    string KeyId,
    string KeyEncryptionAlgorithm,
    string SubjectPublicKeyInfoBase64);

public sealed record CreateEdgeAgentEnrollmentRequest(string Name, int? LifetimeMinutes = null);
public sealed record CreatedEdgeAgentEnrollment(Guid Id, string Name, string EnrollmentCode, DateTimeOffset ExpiresAt);
public sealed record EdgeAgentEnrollmentSummary(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    string Status,
    Guid? UsedByAgentId,
    string? UsedByAgentName);
public sealed record EnrollEdgeAgentRequest(
    string EnrollmentCode,
    string AgentVersion,
    string Platform,
    string? CapabilitiesJson,
    EdgeAgentPublicKeyRequest? PublicKey);
public sealed record EnrolledEdgeAgent(Guid AgentId, Guid WorkerId, string WorkerToken, int ConfigurationVersion);
public sealed record ManagedEdgeAgent(Guid Id, string Name, Guid WorkerId);
public sealed record DeletedEdgeAgent(Guid Id, string Name, int DetachedAssignmentCount, int DeletedCredentialEnvelopeCount, int DeletedConfigurationCount, int DeletedCommandCount);
public sealed record EdgeAgentHeartbeatRequest(string AgentVersion, string? CapabilitiesJson, string? ServiceStatusJson);
public sealed record EdgeAgentConfigurationResponse(int Version, string ConfigurationJson, string Status);
public sealed record EdgeAgentConfigurationStatusReport(int Version, bool Applied, string? FailureKind);
public sealed record EdgeAgentCredentialEnvelopeResponse(
    Guid CredentialId,
    string CredentialName,
    Guid CredentialVersionId,
    int Version,
    string KeyId,
    string KeyEncryptionAlgorithm,
    string ContentEncryptionAlgorithm,
    string EncryptedKeyBase64,
    string InitializationVectorBase64,
    string CiphertextBase64,
    string AuthenticationTagBase64);
public sealed record EdgeAgentOperationResponse(Guid Id, string OperationType, string DetailsJson);
public sealed record EdgeAgentDiagnosticReport(Guid? OperationId, string Kind, bool Succeeded, string? Message, string? ResultJson, Guid? CredentialId = null);
public sealed record EdgeAgentCredentialBinding(Guid WorkerId, Guid CredentialId, string CredentialName, Guid CredentialVersionId);
public sealed record DeviceCredentialEnvelopeInput(
    Guid AgentId,
    Guid CredentialVersionId,
    string KeyId,
    string KeyEncryptionAlgorithm,
    string ContentEncryptionAlgorithm,
    string EncryptedKeyBase64,
    string InitializationVectorBase64,
    string CiphertextBase64,
    string AuthenticationTagBase64,
    int SchemaVersion = AgentCredentialEnvelopeAlgorithms.CurrentSchemaVersion);

/// <summary>
/// 旧服务名称仅保留给现有扩展和测试；新代码必须使用 <see cref="EdgeAgentControlService"/>。
/// </summary>
[Obsolete("请改用 EdgeAgentControlService。")]
public sealed class LinuxEdgeAgentControlService(PlatformDbContext dbContext) : EdgeAgentControlService(dbContext);
