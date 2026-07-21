using System.Text.Json;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VisiCore.Core;
using VisiCore.EdgeAgent;
using Xunit;

namespace VisiCore.EdgeAgent.Tests;

public sealed class EdgeAgentRuntimeTests
{
    [Fact(DisplayName = "未安装 Host Agent 时，配置页仍可安全写入本地配对状态")]
    public void LocalConfigurationStoreWritesOnlyAgentState()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-local-configuration-tests", Guid.NewGuid().ToString("N"));
        var configurationPath = Path.Combine(root, "state", "edge-agent.json");
        var bootstrapPath = Path.Combine(root, "state", "bootstrap", "bootstrap.json");
        try
        {
            var store = new LocalEdgeNodeConfigurationStore(configurationPath, bootstrapPath);

            Assert.True(store.TryApply("https://center.example/", "one-time-code", out var failureKind));
            Assert.Null(failureKind);

            using var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));
            Assert.Equal("https://center.example/", configuration.RootElement.GetProperty("EdgeAgent").GetProperty("ControlPlaneBaseUri").GetString());
            Assert.False(configuration.RootElement.GetProperty("EdgeAgent").GetProperty("HostUpgradeEnabled").GetBoolean());
            using var bootstrap = JsonDocument.Parse(File.ReadAllText(bootstrapPath));
            Assert.Equal("one-time-code", bootstrap.RootElement.GetProperty("enrollmentCode").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "运行时只接受 v1 允许字段并原子保留上一个有效配置")]
    public void RuntimeConfigurationOnlyAppliesValidatedV1()
    {
        var settings = new EdgeAgentRuntimeSettings(new EdgeAgentOptions
        {
            StateDirectory = Path.GetTempPath(),
            ControlPlaneBaseUri = "https://center.example/"
        });

        Assert.True(settings.TryApply("{\"schemaVersion\":1,\"inventorySyncIntervalSeconds\":120,\"onvifEnabled\":false}", out _));
        var applied = settings.Snapshot();
        Assert.Equal(120, applied.InventorySyncIntervalSeconds);
        Assert.False(applied.OnvifEnabled);

        Assert.False(settings.TryApply("{\"schemaVersion\":1,\"credential\":\"forbidden\"}", out var failureKind));
        Assert.Equal("configuration_schema_invalid", failureKind);
        Assert.Equal(applied, settings.Snapshot());
    }

    [Fact(DisplayName = "资源策略允许不限制并拒绝越界值")]
    public void ResourcePolicyValidatesLimits()
    {
        Assert.True(new EdgeNodeResourcePolicy().TryValidate(out _));
        Assert.True(new EdgeNodeResourcePolicy(50, 1024, 85).TryValidate(out _));
        Assert.False(new EdgeNodeResourcePolicy(0, 1024, 85).TryValidate(out var cpuFailure));
        Assert.Equal("resource_policy_invalid", cpuFailure);
        Assert.False(new EdgeNodeResourcePolicy(50, 128, 85).TryValidate(out _));
        Assert.False(new EdgeNodeResourcePolicy(50, 1024, 96).TryValidate(out _));
    }

    [Fact(DisplayName = "Linux 资源覆盖文件仅写入固定 edge-node CPU 与内存限制")]
    public async Task LinuxResourcePolicyOverrideContainsOnlyFixedFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-resource-policy-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "compose.resources.yaml");
        try
        {
            await LinuxResourcePolicyComposeWriter.WriteAsync(path, new EdgeNodeResourcePolicy(50, 1024, 85), CancellationToken.None);
            var content = await File.ReadAllTextAsync(path);

            Assert.Contains("edge-node:", content, StringComparison.Ordinal);
            Assert.Contains("cpus:", content, StringComparison.Ordinal);
            Assert.Contains("mem_limit: 1024m", content, StringComparison.Ordinal);
            Assert.DoesNotContain("command:", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("volumes:", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "运行状态保留资源快照而不改变节点生命周期")]
    public void RuntimeStateStoresResourceSnapshot()
    {
        var state = new EdgeAgentRuntimeState();
        var snapshot = new EdgeNodeResourceSnapshot(12.5, 512 * 1024 * 1024, 1000, 400, false,
            new EdgeNodeResourcePolicy(50, 1024, 85), "applied", null, DateTimeOffset.UtcNow);

        state.SetAwaitingEnrollment();
        state.SetResources(snapshot);

        Assert.Equal("awaiting_enrollment", state.Snapshot().Status);
        Assert.Equal(snapshot, state.Snapshot().Resource);
    }

    [Fact(DisplayName = "Windows 身份材料必须以 DPAPI 本机保护格式保存")]
    public void WindowsIdentityMaterialIsDpapiProtected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "visicore-edge-agent-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EdgeAgentIdentityStore(new EdgeAgentOptions
            {
                StateDirectory = directory,
                ControlPlaneBaseUri = "https://center.example/"
            });
            using var identity = store.LoadOrCreate();
            var persisted = File.ReadAllText(Path.Combine(directory, "identity.json"));
            Assert.StartsWith("dpapi-local-machine:", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(identity.Identity.AgentId, persisted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Host 操作交换区只返回最小回执")]
    public async Task HostOperationExchangeUsesOperationAndReceiptFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-exchange-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new HostAgentOptions
            {
                OperationInboxDirectory = Path.Combine(root, "inbox"),
                OperationReceiptDirectory = Path.Combine(root, "receipts")
            };
            var exchange = new HostOperationExchange(options);
            var id = Guid.NewGuid();
            await exchange.SubmitAsync(new EdgeOperation(id, "deployment", "{}"), CancellationToken.None);
            Assert.True(File.Exists(Path.Combine(options.OperationInboxDirectory, $"{id:N}.operation.json")));

            Directory.CreateDirectory(options.OperationReceiptDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(options.OperationReceiptDirectory, $"{id:N}.receipt.json"),
                JsonSerializer.Serialize(new HostOperationReceipt(id, false, "signature_invalid", DateTimeOffset.UtcNow)));
            var receipt = await exchange.TryReadReceiptAsync(id, CancellationToken.None);
            Assert.Equal("signature_invalid", receipt?.FailureKind);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "发行清单必须绑定当前可识别的平台、制品哈希和有效期")]
    public void ReleaseManifestRejectsUntrustedShape()
    {
        var now = DateTimeOffset.UtcNow;
        var valid = JsonSerializer.Serialize(new
        {
            releaseId = "v0.1.0-windows-x64",
            operationType = "deployment",
            targetPlatform = "windows",
            targetArchitecture = "x64",
            artifactUrl = "https://releases.example/edge-node.msi",
            artifactSha256 = new string('a', 64),
            minimumHostAgentVersion = "0.1.0",
            signingPublicKeyId = "release-key-2026",
            issuedAt = now,
            expiresAt = now.AddHours(1)
        });
        Assert.True(EdgeReleaseManifest.TryParse(valid, out _, out _));
        Assert.False(EdgeReleaseManifest.TryParse(valid.Replace("https://", "http://", StringComparison.Ordinal), out _, out _));
    }

    [Fact(DisplayName = "Host Agent 回滚前必须再次验证已暂存制品哈希")]
    public async Task PersistedArtifactMustStillMatchVerifiedHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-artifact-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var artifactPath = Path.Combine(root, "edge-node.msi");
            await File.WriteAllTextAsync(artifactPath, "verified-release");
            string hash;
            await using (var stream = File.OpenRead(artifactPath))
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            }
            var verifier = new HostReleaseArtifactVerifier(new HostAgentOptions
            {
                ReleaseArtifactDirectory = root
            });
            var artifact = new HostVerifiedReleaseArtifact(artifactPath, null, hash);

            Assert.True(await verifier.VerifyPersistedAsync(artifact, CancellationToken.None));

            await File.WriteAllTextAsync(artifactPath, "tampered-release");
            Assert.False(await verifier.VerifyPersistedAsync(artifact, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "回滚目标必须是部署前已知良好制品，而不是刚完成部署的新制品")]
    public async Task RollbackUsesPreviousKnownGoodArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-rollback-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var options = new HostAgentOptions { OperationStateDirectory = root };
            var store = new VerifiedDeploymentStore(options);
            var first = new HostVerifiedReleaseArtifact(null, null, new string('a', 64), "visicore/visicore-edge-node@sha256:" + new string('a', 64), "0.1.0");
            var second = new HostVerifiedReleaseArtifact(null, null, new string('b', 64), "visicore/visicore-edge-node@sha256:" + new string('b', 64), "0.1.1");
            var firstOperation = Guid.NewGuid();
            var secondOperation = Guid.NewGuid();
            await store.RememberAsync(firstOperation, "0.1.0", "key", first, CancellationToken.None);
            await store.RememberAsync(secondOperation, "0.1.1", "key", second, CancellationToken.None);

            var rollback = await store.GetRollbackArtifactAsync(secondOperation, CancellationToken.None);
            Assert.NotNull(rollback);
            Assert.Equal(first.OciImageReference, rollback!.OciImageReference);
            Assert.NotEqual(second.OciImageReference, rollback.OciImageReference);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Host 配置通道可在未导入发行信任根时运行，但实际升级必须导入信任根")]
    public void HostAgentSeparatesConfigurationFromReleaseExecution()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-options-tests", Guid.NewGuid().ToString("N"));
        var options = new HostAgentOptions
        {
            Enabled = true,
            OperationInboxDirectory = Path.Combine(root, "inbox"),
            OperationReceiptDirectory = Path.Combine(root, "receipts"),
            OperationStateDirectory = Path.Combine(root, "state"),
            ReleaseArtifactDirectory = Path.Combine(root, "releases")
        };

        Assert.True(options.TryValidate(out _));
        var executionOptions = new HostAgentOptions
        {
            Enabled = true,
            AllowExecution = true,
            OperationInboxDirectory = options.OperationInboxDirectory,
            OperationReceiptDirectory = options.OperationReceiptDirectory,
            OperationStateDirectory = options.OperationStateDirectory,
            ReleaseArtifactDirectory = options.ReleaseArtifactDirectory
        };
        Assert.False(executionOptions.TryValidate(out _));
    }

    [Fact(DisplayName = "Windows Host Agent 开启实际升级前必须保留已知良好 MSI")]
    public void WindowsHostAgentRequiresKnownGoodMsi()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "visicore-host-windows-options-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var msiPath = Path.Combine(root, "known-good.msi");
            var runnerPath = Path.Combine(root, "VisiCore.EdgeUpdateRunner.exe");
            var keyPath = Path.Combine(root, "release-public-key.pem");
            Directory.CreateDirectory(root);
            File.WriteAllText(msiPath, "known-good");
            File.WriteAllText(runnerPath, "runner");
            File.WriteAllText(keyPath, "public-key");

            var options = new HostAgentOptions
            {
                Enabled = true,
                AllowExecution = true,
                OperationInboxDirectory = Path.Combine(root, "inbox"),
                OperationReceiptDirectory = Path.Combine(root, "receipts"),
                OperationStateDirectory = Path.Combine(root, "state"),
                ReleaseArtifactDirectory = Path.Combine(root, "releases"),
                SigningPublicKeyPath = keyPath,
                SigningPublicKeyId = "release-key-2026",
                AllowedArtifactHosts = ["github.com"],
                WindowsInstallerExecutablePath = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                WindowsUpdateRunnerExecutablePath = runnerPath
            };

            Assert.False(options.TryValidate(out _));
            options = new HostAgentOptions
            {
                Enabled = options.Enabled,
                AllowExecution = options.AllowExecution,
                OperationInboxDirectory = options.OperationInboxDirectory,
                OperationReceiptDirectory = options.OperationReceiptDirectory,
                OperationStateDirectory = options.OperationStateDirectory,
                ReleaseArtifactDirectory = options.ReleaseArtifactDirectory,
                SigningPublicKeyPath = options.SigningPublicKeyPath,
                SigningPublicKeyId = options.SigningPublicKeyId,
                AllowedArtifactHosts = options.AllowedArtifactHosts,
                WindowsInstallerExecutablePath = options.WindowsInstallerExecutablePath,
                WindowsUpdateRunnerExecutablePath = options.WindowsUpdateRunnerExecutablePath,
                WindowsInstallerPath = msiPath
            };
            Assert.True(options.TryValidate(out _));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Linux 配置 Socket 只接受持有访问令牌的固定校验请求")]
    public async Task LinuxConfigurationSocketRequiresAccessToken()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), "visicore-host-configuration-tests", Guid.NewGuid().ToString("N"));
        var socketPath = Path.Combine(root, "host-agent.sock");
        var tokenPath = Path.Combine(root, "access.token");
        var composePath = Path.Combine(root, "compose.yaml");
        var agentConfigurationPath = Path.Combine(root, "state", "edge-agent.json");
        var bootstrapPath = Path.Combine(root, "state", "bootstrap", "bootstrap.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(tokenPath, "test-access-token");
        await File.WriteAllTextAsync(composePath, "services: {}\n");
        using var cancellation = new CancellationTokenSource();
        var options = new HostAgentOptions
        {
            ConfigurationSocketPath = socketPath,
            ConfigurationTokenPath = tokenPath,
            DockerComposeExecutablePath = "/bin/true",
            ComposeFilePath = composePath,
            ManagedEdgeAgentConfigurationPath = agentConfigurationPath,
            ManagedEdgeAgentBootstrapPath = bootstrapPath
        };
        var worker = new HostConfigurationSocketWorker(
            options,
            new DockerComposeHostOperationExecutor(options),
            NullLogger<HostConfigurationSocketWorker>.Instance);

        try
        {
            await worker.StartAsync(cancellation.Token);
            for (var attempt = 0; attempt < 20 && !File.Exists(socketPath); attempt++)
            {
                await Task.Delay(50, cancellation.Token);
            }
            Assert.True(File.Exists(socketPath));

            var accepted = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "test-access-token", "test", "https://center.example/"));
            Assert.True(accepted.Succeeded);

            var resourceAccepted = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "test-access-token", "resource-test", ResourcePolicy: new EdgeNodeResourcePolicy(50, 1024, 85)));
            Assert.True(resourceAccepted.Succeeded);

            var resourceRejected = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "test-access-token", "resource-test", ResourcePolicy: new EdgeNodeResourcePolicy(0, 1024, 85)));
            Assert.False(resourceRejected.Succeeded);
            Assert.Equal("resource_policy_invalid", resourceRejected.FailureKind);

            var rejected = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "invalid-token", "test", "https://center.example/"));
            Assert.False(rejected.Succeeded);
            Assert.Equal("configuration_access_denied", rejected.FailureKind);

            var applied = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "test-access-token", "apply", "https://center.example/", "one-time-code"));
            Assert.True(applied.Succeeded);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(agentConfigurationPath));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(bootstrapPath));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(bootstrapPath)!));
        }
        finally
        {
            cancellation.Cancel();
            await worker.StopAsync(CancellationToken.None);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<EdgeNodeConfigurationCommandResult> SendConfigurationCommandAsync(string socketPath, EdgeNodeConfigurationCommand command)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();

        var responsePrefix = new byte[4];
        await stream.ReadExactlyAsync(responsePrefix);
        var response = new byte[BinaryPrimitives.ReadInt32LittleEndian(responsePrefix)];
        await stream.ReadExactlyAsync(response);
        return JsonSerializer.Deserialize<EdgeNodeConfigurationCommandResult>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
