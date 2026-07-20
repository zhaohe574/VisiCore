namespace VisiCore.EdgeAgent;

public sealed record EdgeNodeHostConfigurationInput(
    bool Enabled,
    bool AllowExecution,
    string? SigningPublicKeyId,
    string? SigningPublicKeyPem,
    IReadOnlyList<string> AllowedArtifactHosts,
    long MaximumArtifactBytes,
    int ExecutionTimeoutSeconds);

public sealed record EdgeNodeConfigurationCommand(
    string AccessToken,
    string Action,
    string? ControlPlaneBaseUri = null,
    string? EnrollmentCode = null,
    EdgeNodeHostConfigurationInput? Host = null);

public sealed record EdgeNodeConfigurationCommandResult(bool Succeeded, string? FailureKind = null, bool HostRestarting = false);
