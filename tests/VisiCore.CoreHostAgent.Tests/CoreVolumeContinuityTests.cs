using VisiCore.CoreHostAgent;
using VisiCore.Core;
using Xunit;

namespace VisiCore.CoreHostAgent.Tests;

public sealed class CoreVolumeContinuityTests
{
    [Fact(DisplayName = "核心 Host Agent 程序集使用独立 Core 版本")]
    public void CoreHostAgentAssemblyUsesCoreVersion()
    {
        var expectedVersion = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "versions", "core.txt")).Trim();

        Assert.Equal(expectedVersion, RuntimeVersion.GetProductVersion(typeof(CoreHostAgentWorker).Assembly));
    }

    [Fact(DisplayName = "核心 Compose 配置读取四个稳定数据卷")]
    public void ReadsExpectedVolumesFromComposeConfiguration()
    {
        var parsed = CoreVolumeContinuity.TryReadExpectedVolumes(ComposeConfiguration, out var volumes);

        Assert.True(parsed);
        Assert.Equal("visicore_postgres-data", volumes["/var/lib/postgresql/data"]);
        Assert.Equal("visicore_visicore-config", volumes["/var/lib/visicore/config"]);
        Assert.Equal("visicore_api-exports", volumes["/var/lib/visicore/exports"]);
        Assert.Equal("visicore_visicore-backups", volumes["/var/lib/visicore/backups"]);
    }

    [Fact(DisplayName = "运行中的核心容器挂载稳定卷时允许升级")]
    public void AcceptsMatchingContainerVolumes()
    {
        Assert.True(CoreVolumeContinuity.TryReadExpectedVolumes(ComposeConfiguration, out var volumes));

        Assert.True(CoreVolumeContinuity.MatchesRunningContainer(MatchingContainerInspection, volumes));
    }

    [Fact(DisplayName = "运行中的核心容器挂载空卷或其他卷时拒绝升级")]
    public void RejectsMismatchedContainerVolumes()
    {
        Assert.True(CoreVolumeContinuity.TryReadExpectedVolumes(ComposeConfiguration, out var volumes));

        Assert.False(CoreVolumeContinuity.MatchesRunningContainer(MismatchedContainerInspection, volumes));
    }

    private const string ComposeConfiguration = """
        {
          "volumes": {
            "postgres-data": { "name": "visicore_postgres-data" },
            "visicore-config": { "name": "visicore_visicore-config" },
            "api-exports": { "name": "visicore_api-exports" },
            "visicore-backups": { "name": "visicore_visicore-backups" }
          }
        }
        """;

    private const string MatchingContainerInspection = """
        [
          {
            "Mounts": [
              { "Type": "volume", "Name": "visicore_postgres-data", "Destination": "/var/lib/postgresql/data" },
              { "Type": "volume", "Name": "visicore_visicore-config", "Destination": "/var/lib/visicore/config" },
              { "Type": "volume", "Name": "visicore_api-exports", "Destination": "/var/lib/visicore/exports" },
              { "Type": "volume", "Name": "visicore_visicore-backups", "Destination": "/var/lib/visicore/backups" },
              { "Type": "bind", "Source": "/opt/visicore/tls", "Destination": "/run/visicore/tls" }
            ]
          }
        ]
        """;

    private const string MismatchedContainerInspection = """
        [
          {
            "Mounts": [
              { "Type": "volume", "Name": "visicore_postgres-data", "Destination": "/var/lib/postgresql/data" },
              { "Type": "volume", "Name": "visicore-config-empty", "Destination": "/var/lib/visicore/config" },
              { "Type": "volume", "Name": "visicore_api-exports", "Destination": "/var/lib/visicore/exports" },
              { "Type": "volume", "Name": "visicore_visicore-backups", "Destination": "/var/lib/visicore/backups" }
            ]
          }
        ]
        """;
}
