using VisiCore.Setup;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class InstallationServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"visicore-setup-tests-{Guid.NewGuid():N}");

    public InstallationServiceTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void 未配置时返回引导状态和非敏感默认值()
    {
        var service = CreateService();

        var status = service.GetStatus();

        Assert.Equal(InstallationState.Unconfigured, status.State);
        Assert.Equal("visicore", status.Defaults.DatabaseName);
        Assert.DoesNotContain("password", status.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 已有运行配置时返回完成状态()
    {
        var path = Path.Combine(directory, "runtime.json");
        File.WriteAllText(path, "{}");
        var service = new InstallationService(path);

        var status = service.GetStatus();

        Assert.Equal(InstallationState.Completed, status.State);
    }

    [Fact]
    public async Task 已有运行配置拒绝再次初始化()
    {
        var path = Path.Combine(directory, "runtime.json");
        File.WriteAllText(path, "{}");
        var service = new InstallationService(path);

        await Assert.ThrowsAsync<InstallationConflictException>(() =>
            service.InitializeAsync(CreateRequest(), new Uri("http://127.0.0.1:8080/"), CancellationToken.None));
    }

    [Fact]
    public async Task 安装锁拒绝并发初始化()
    {
        var path = Path.Combine(directory, "runtime.json");
        File.WriteAllText(path + ".lock", string.Empty);
        var service = new InstallationService(path);

        Assert.Equal(InstallationState.Initializing, service.GetStatus().State);
        await Assert.ThrowsAsync<InstallationConflictException>(() =>
            service.InitializeAsync(CreateRequest(), new Uri("http://127.0.0.1:8080/"), CancellationToken.None));
    }

    [Fact]
    public async Task 局域网HTTP必须与浏览器来源完全一致()
    {
        var service = CreateService();
        var request = CreateRequest() with
        {
            MediaMode = "remote",
            MediaApiBaseUri = "https://media.example.test/api/",
            MediaHlsBaseUri = "https://media.example.test/hls/",
            PublicBaseUri = "http://10.37.200.52:8080/",
            AllowInsecureLanHttp = true
        };

        var exception = await Assert.ThrowsAsync<InstallationValidationException>(() =>
            service.InitializeAsync(request, new Uri("http://10.37.200.53:8080/"), CancellationToken.None));

        Assert.Contains("浏览器访问来源", exception.Message);
    }

    [Fact]
    public async Task 远程MediaMtx拒绝非HTTPS地址()
    {
        var service = CreateService();
        var request = CreateRequest() with
        {
            MediaMode = "remote",
            MediaApiBaseUri = "http://media.example.test/api/",
            MediaHlsBaseUri = "http://media.example.test/hls/",
            PublicBaseUri = "https://visicore.example.test/"
        };

        var exception = await Assert.ThrowsAsync<InstallationValidationException>(() =>
            service.InitializeAsync(request, new Uri("https://visicore.example.test/"), CancellationToken.None));

        Assert.Contains("HTTPS", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private InstallationService CreateService() => new(Path.Combine(directory, "runtime.json"));

    private static InstallationRequest CreateRequest() => new(
        "postgres.example.test",
        5432,
        "disable",
        "postgres",
        "database-admin-password",
        "visicore",
        "http://127.0.0.1:8080/",
        "same-host",
        "http://mediamtx:9997/",
        "http://mediamtx:8888/",
        "admin",
        "platform-admin-password",
        true);
}
