namespace VisiCore.EdgeAgent;

public sealed class EdgeAgentRuntimeState
{
    private readonly object gate = new();
    private EdgeAgentRuntimeSnapshot snapshot = new(
        "starting",
        null,
        null,
        null,
        null,
        0,
        0,
        null,
        null,
        null,
        DateTimeOffset.UtcNow);

    public EdgeAgentRuntimeSnapshot Snapshot()
    {
        lock (gate)
        {
            return snapshot;
        }
    }

    public void SetIdentity(string agentId, string keyId, string? configurationVersion)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                AgentId = agentId,
                KeyId = keyId,
                ConfigurationVersion = configurationVersion,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void SetAwaitingEnrollment()
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Status = "awaiting_enrollment",
                LastFailureKind = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void SetRunning(string? configurationVersion, int credentialEnvelopeCount, int assignedRecorderCount)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Status = "running",
                ConfigurationVersion = configurationVersion ?? snapshot.ConfigurationVersion,
                CredentialEnvelopeCount = credentialEnvelopeCount,
                AssignedRecorderCount = assignedRecorderCount,
                LastHeartbeatAt = DateTimeOffset.UtcNow,
                LastFailureKind = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void SetFailure(string failureKind)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Status = "degraded",
                LastFailureKind = failureKind,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void SetResources(EdgeNodeResourceSnapshot resource)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Resource = resource,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }
}

public sealed record EdgeAgentRuntimeSnapshot(
    string Status,
    string? AgentId,
    string? KeyId,
    string? ConfigurationVersion,
    DateTimeOffset? LastHeartbeatAt,
    int CredentialEnvelopeCount,
    int AssignedRecorderCount,
    string? LastFailureKind,
    DateTimeOffset? LastDiagnosticAt,
    EdgeNodeResourceSnapshot? Resource,
    DateTimeOffset UpdatedAt);

public sealed class HostOperationState
{
    private readonly object gate = new();
    private HostOperationSnapshot snapshot = new(false, "disabled", 0, null, null, DateTimeOffset.UtcNow);

    public HostOperationSnapshot Snapshot()
    {
        lock (gate)
        {
            return snapshot;
        }
    }

    public void SetDisabled()
    {
        lock (gate)
        {
            snapshot = new(false, "disabled", 0, null, null, DateTimeOffset.UtcNow);
        }
    }

    public void SetBlocked(string failureKind)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Enabled = true,
                Status = "blocked",
                LastFailureKind = failureKind,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void SetAccepted()
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Enabled = true,
                Status = "accepted_pending_executor",
                AcceptedOperationCount = snapshot.AcceptedOperationCount + 1,
                LastFailureKind = null,
                LastAcceptedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }
}

public sealed record HostOperationSnapshot(
    bool Enabled,
    string Status,
    int AcceptedOperationCount,
    DateTimeOffset? LastAcceptedAt,
    string? LastFailureKind,
    DateTimeOffset UpdatedAt);
