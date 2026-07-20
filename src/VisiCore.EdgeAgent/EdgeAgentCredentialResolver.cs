using System.Net;
using VisiCore.Core;
using VisiCore.DeviceWorker;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 将当前同步轮次中的 AgentEnvelope 明文映射为 ONVIF 客户端可用凭据。
/// 不落盘、不记录敏感字段，下一轮同步会整体替换引用。
/// </summary>
public sealed class EdgeAgentCredentialResolver : IRecorderCredentialResolver
{
    private readonly object gate = new();
    private IReadOnlyDictionary<string, AgentCredentialPayload> credentials = new Dictionary<string, AgentCredentialPayload>(StringComparer.Ordinal);

    public void Replace(IReadOnlyDictionary<string, AgentCredentialPayload> next)
    {
        var copy = next.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        lock (gate)
        {
            credentials = copy;
        }
    }

    public bool Contains(string credentialReference)
    {
        lock (gate)
        {
            return credentials.ContainsKey(credentialReference);
        }
    }

    public Task<NetworkCredential> ResolveAsync(RecorderCredentialTarget recorder, CancellationToken cancellationToken)
    {
        AgentCredentialPayload? credential;
        lock (gate)
        {
            credential = credentials.GetValueOrDefault(recorder.CredentialReference);
        }
        if (credential is null)
        {
            throw new EdgeAgentCredentialUnavailableException();
        }
        return Task.FromResult(new NetworkCredential(credential.Username, credential.Password));
    }
}

public sealed class EdgeAgentCredentialUnavailableException : Exception
{
    public EdgeAgentCredentialUnavailableException() : base("边缘节点未持有设备凭据引用。")
    {
    }
}
