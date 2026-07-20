using System.Net;

namespace VisiCore.DeviceWorker;

public sealed record RecorderCredentialTarget(
    Guid Id,
    string Name,
    string Vendor,
    string Host,
    int Port,
    string AdapterType,
    string CredentialReference);

public interface IRecorderCredentialResolver
{
    Task<NetworkCredential> ResolveAsync(RecorderCredentialTarget recorder, CancellationToken cancellationToken);
}
