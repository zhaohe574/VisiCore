using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.EdgeAgent;

public sealed class EdgeAgentControlPlaneClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<EnrollmentResult> EnrollAsync(
        EdgeAgentIdentityMaterial identity,
        EdgeAgentOptions options,
        string enrollmentCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(enrollmentCode))
        {
            throw new InvalidOperationException("Edge Agent 尚未配置注册码。 ");
        }

        if (!Guid.TryParseExact(identity.Identity.AgentId, "N", out var agentId))
        {
            throw new EdgeControlPlaneException("invalid_agent_identity");
        }

        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(
            agentId,
            identity.Identity.KeyId,
            identity.PrivateKey);
        var payload = new EnrollmentRequest(
            enrollmentCode,
            options.GetAgentVersion(),
            options.GetPlatform(),
            options.GetCapabilitiesJson(),
            new EnrollmentPublicKey(
                publicKey.AgentId,
                publicKey.KeyId,
                publicKey.KeyEncryptionAlgorithm,
                publicKey.SubjectPublicKeyInfoBase64));
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/edge-agents/enroll",
            payload,
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        return new EnrollmentResult(
            ReadRequiredString(document.RootElement, "agentId"),
            ReadRequiredString(document.RootElement, "workerId"),
            ReadRequiredString(document.RootElement, "workerToken"),
            ReadOptionalScalarString(document.RootElement, "configurationVersion"));
    }

    public async Task SendHeartbeatAsync(
        EdgeAgentIdentity identity,
        EdgeAgentOptions options,
        EdgeAgentRuntimeSnapshot runtime,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            $"api/v1/edge-agents/{identity.AgentId}/heartbeat",
            identity,
            JsonContent.Create(new HeartbeatRequest(
                options.GetAgentVersion(),
                options.GetCapabilitiesJson(),
                JsonSerializer.Serialize(new
                {
                    runtime.Status,
                    runtime.ConfigurationVersion,
                    runtime.CredentialEnvelopeCount,
                    runtime.LastFailureKind
                }, SerializerOptions)), options: SerializerOptions),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ConfigurationResult> GetConfigurationAsync(
        EdgeAgentIdentity identity,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"api/v1/edge-agents/{identity.AgentId}/configuration",
            identity,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        return new ConfigurationResult(
            ReadOptionalScalarString(document.RootElement, "version"),
            ReadOptionalString(document.RootElement, "configurationJson"),
            ReadOptionalString(document.RootElement, "status"));
    }

    public async Task ReportConfigurationStatusAsync(
        EdgeAgentIdentity identity,
        string configurationVersion,
        bool applied,
        string? failureKind,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            $"api/v1/edge-agents/{identity.AgentId}/configuration-status",
            identity,
            JsonContent.Create(new ConfigurationStatusRequest(configurationVersion, applied, failureKind), options: SerializerOptions),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<EdgeCredentialEnvelope>> GetCredentialEnvelopesAsync(
        EdgeAgentIdentity identity,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"api/v1/edge-agents/{identity.AgentId}/credentials",
            identity,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var envelopes = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("envelopes", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : default;
        if (envelopes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<EdgeCredentialEnvelope>();
        foreach (var item in envelopes.EnumerateArray())
        {
            if (!Guid.TryParse(ReadOptionalString(item, "credentialId"), out var credentialId) ||
                !Guid.TryParse(ReadOptionalString(item, "credentialVersionId"), out var credentialVersionId))
            {
                throw new EdgeControlPlaneException("credential_envelope_invalid");
            }

            var credentialName = ReadOptionalString(item, "credentialName");
            var encryptedKey = ReadOptionalString(item, "encryptedKeyBase64");
            var initializationVector = ReadOptionalString(item, "initializationVectorBase64");
            var ciphertext = ReadOptionalString(item, "ciphertextBase64");
            var authenticationTag = ReadOptionalString(item, "authenticationTagBase64");
            var keyId = ReadOptionalString(item, "keyId");
            var keyEncryptionAlgorithm = ReadOptionalString(item, "keyEncryptionAlgorithm");
            var contentEncryptionAlgorithm = ReadOptionalString(item, "contentEncryptionAlgorithm");
            if (string.IsNullOrWhiteSpace(credentialName) || string.IsNullOrWhiteSpace(encryptedKey) || string.IsNullOrWhiteSpace(initializationVector) ||
                string.IsNullOrWhiteSpace(ciphertext) || string.IsNullOrWhiteSpace(authenticationTag) ||
                string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keyEncryptionAlgorithm) ||
                string.IsNullOrWhiteSpace(contentEncryptionAlgorithm))
            {
                throw new EdgeControlPlaneException("credential_envelope_invalid");
            }

            result.Add(new EdgeCredentialEnvelope(
                credentialId,
                credentialName,
                credentialVersionId,
                new AgentCredentialEnvelope(
                    AgentCredentialEnvelopeAlgorithms.CurrentSchemaVersion,
                    Guid.Empty,
                    credentialVersionId,
                    keyId,
                    keyEncryptionAlgorithm,
                    contentEncryptionAlgorithm,
                    encryptedKey,
                    initializationVector,
                    ciphertext,
                    authenticationTag)));
        }
        return result;
    }

    public async Task<IReadOnlyList<WorkerRecorderAssignment>> GetWorkerAssignmentsAsync(
        EdgeAgentIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!identity.IsEnrolled)
        {
            throw new EdgeControlPlaneException("agent_not_enrolled");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/device-worker/assignments");
        request.Headers.TryAddWithoutValidation("X-Device-Worker-Token", identity.WorkerToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<WorkerRecorderAssignment>>(SerializerOptions, cancellationToken) ?? [];
    }

    public async Task ReportInventoryAsync(
        EdgeAgentIdentity identity,
        WorkerInventoryReport report,
        CancellationToken cancellationToken) =>
        await SendWorkerReportAsync(identity, HttpMethod.Post, "api/v1/device-worker/inventory", report, cancellationToken);

    public async Task ReportHealthAsync(
        EdgeAgentIdentity identity,
        WorkerHealthReport report,
        CancellationToken cancellationToken) =>
        await SendWorkerReportAsync(identity, HttpMethod.Post, "api/v1/device-worker/health", report, cancellationToken);

    public async Task ReportClockAsync(
        EdgeAgentIdentity identity,
        WorkerClockReport report,
        CancellationToken cancellationToken) =>
        await SendWorkerReportAsync(identity, HttpMethod.Post, "api/v1/device-worker/clock", report, cancellationToken);

    public async Task ReportOperationStatusesAsync(
        EdgeAgentIdentity identity,
        WorkerOperationStatusReport report,
        CancellationToken cancellationToken) =>
        await SendWorkerReportAsync(identity, HttpMethod.Put, "api/v1/device-worker/operation-statuses", report, cancellationToken);

    public async Task<IReadOnlyList<EdgeOperation>> GetOperationsAsync(
        EdgeAgentIdentity identity,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"api/v1/edge-agents/{identity.AgentId}/operations",
            identity,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var pending = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("pending", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : default;
        if (pending.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var operations = new List<EdgeOperation>();
        foreach (var item in pending.EnumerateArray())
        {
            var operationId = ReadOptionalString(item, "id");
            var operationType = ReadOptionalString(item, "operationType");
            if (Guid.TryParse(operationId, out var id) && !string.IsNullOrWhiteSpace(operationType))
            {
                operations.Add(new EdgeOperation(id, operationType, ReadOptionalString(item, "detailsJson")));
            }
        }
        return operations;
    }

    public async Task SendDiagnosticAsync(
        EdgeAgentIdentity identity,
        EdgeDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            $"api/v1/edge-agents/{identity.AgentId}/diagnostics",
            identity,
            JsonContent.Create(new DiagnosticRequest(
                diagnostic.Kind,
                diagnostic.ResultJson,
                diagnostic.Succeeded,
                diagnostic.OperationId), options: SerializerOptions),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string relativeUri,
        EdgeAgentIdentity identity,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (!identity.IsEnrolled)
        {
            throw new InvalidOperationException("Edge Agent 尚未完成注册。 ");
        }

        var request = new HttpRequestMessage(method, relativeUri)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.WorkerToken);
        request.Headers.Add("X-Edge-Agent-Id", identity.AgentId);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private async Task SendWorkerReportAsync<TReport>(
        EdgeAgentIdentity identity,
        HttpMethod method,
        string relativeUri,
        TReport report,
        CancellationToken cancellationToken)
    {
        if (!identity.IsEnrolled)
        {
            throw new EdgeControlPlaneException("agent_not_enrolled");
        }

        using var request = new HttpRequestMessage(method, relativeUri)
        {
            Content = JsonContent.Create(report, options: SerializerOptions)
        };
        request.Headers.TryAddWithoutValidation("X-Device-Worker-Token", identity.WorkerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new EdgeControlPlaneException($"http_{(int)response.StatusCode}");
        }

        await Task.CompletedTask;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = ReadOptionalString(element, propertyName);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new EdgeControlPlaneException("invalid_response");
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadOptionalScalarString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    private sealed record EnrollmentRequest(
        string EnrollmentCode,
        string AgentVersion,
        string Platform,
        string CapabilitiesJson,
        EnrollmentPublicKey PublicKey);

    private sealed record EnrollmentPublicKey(
        Guid AgentId,
        string KeyId,
        string KeyEncryptionAlgorithm,
        string SubjectPublicKeyInfoBase64);

    private sealed record HeartbeatRequest(string AgentVersion, string CapabilitiesJson, string ServiceStatusJson);
    private sealed record DiagnosticRequest(string Kind, string ResultJson, bool Succeeded, Guid? OperationId);
    private sealed record ConfigurationStatusRequest(string Version, bool Applied, string? FailureKind);
}

public sealed record EnrollmentResult(string AgentId, string WorkerId, string WorkerToken, string? ConfigurationVersion);
public sealed record ConfigurationResult(string? Version, string? ConfigurationJson, string? Status);
public sealed record EdgeCredentialEnvelope(Guid CredentialId, string CredentialName, Guid CredentialVersionId, AgentCredentialEnvelope Envelope);
public sealed record EdgeOperation(Guid Id, string OperationType, string? DetailsJson);
public sealed record EdgeDiagnostic(string Kind, string ResultJson, bool Succeeded, Guid? OperationId = null);

public sealed class EdgeControlPlaneException(string failureKind) : Exception(failureKind)
{
    public string FailureKind { get; } = failureKind;
}
