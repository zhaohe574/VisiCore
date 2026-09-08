using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Updater;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class PtzAndUpdaterTests
{
    [Fact]
    public async Task PtzLeaseRenewsAndStopsOnRelease()
    {
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var count = 0;
        var api = new FakeApi { Send = (_, path, _) => { if (path.EndsWith("/ptz") && Interlocked.Increment(ref count) == 2) renewed.SetResult(); return Task.CompletedTask; } };
        await using var ptz = new PtzService(api);
        await ptz.StartAsync(202, "focusNear", 4); await renewed.Task.WaitAsync(TimeSpan.FromSeconds(5)); await ptz.StopAsync();
        var starts = api.Calls.Where(c => c.Path == "channels/202/ptz").ToArray();
        Assert.Equal(2, starts.Length); Assert.All(starts, c => Assert.Equal(new PtzRequest("focusNear", 4), c.Body));
        Assert.Single(api.Calls, c => c.Path == "channels/202/ptz/stop");
    }

    [Fact]
    public async Task ExclusivePtzConflictDoesNotStopAnotherOperatorsLease()
    {
        var api = new FakeApi { Send = (_, _, _) => throw new PlatformException("控制被占用", HttpStatusCode.Conflict) };
        await using var ptz = new PtzService(api);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ptz.StartAsync(101, "up", 2));
        Assert.Contains("其他值守员", error.Message);
        await ptz.StopAsync(); Assert.DoesNotContain(api.Calls, c => c.Path.EndsWith("/stop"));
    }

    [Theory]
    [InlineData("up")][InlineData("down")][InlineData("left")][InlineData("right")][InlineData("auto")]
    [InlineData("zoomIn")][InlineData("zoomOut")][InlineData("focusNear")][InlineData("focusFar")][InlineData("irisOpen")][InlineData("irisClose")]
    public async Task EveryPtzCommandMatchesV2Contract(string command)
    {
        var api = new FakeApi(); await using var ptz = new PtzService(api);
        await ptz.StartAsync(999, command, 7); await ptz.StopAsync();
        Assert.Contains(api.Calls, c => c.Path == "channels/999/ptz" && c.Body is PtzRequest { Speed: 7 } body && body.Command == command);
    }

    [Fact]
    public async Task UpdaterRejectsCorruptionAndTruncatedDownloads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VideoPlatform.Desktop.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.msi"); var bytes = "安装包完整性测试"u8.ToArray(); await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.Equal(hash, await UpdatePolicy.VerifyAsync(path, hash, bytes.Length));
            await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePolicy.VerifyAsync(path, new string('0', 64), bytes.Length));
            await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePolicy.VerifyAsync(path, hash, bytes.Length + 1));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void UpdaterRejectsTraversalAndUntrustedDownloadTransport()
    {
        var executable = Environment.ProcessPath!; var hash = new string('a', 64);
        Assert.Throws<InvalidOperationException>(() => UpdatePolicy.Validate("http://server/update.msi", "update.msi", hash, executable));
        Assert.Throws<InvalidOperationException>(() => UpdatePolicy.Validate("https://server/update.msi", "../update.msi", hash, executable));
        Assert.Throws<InvalidOperationException>(() => UpdatePolicy.Validate("https://name:password@server/update.msi", "update.msi", hash, executable));
    }

    [Theory]
    [InlineData(0, true)][InlineData(3010, true)][InlineData(1641, true)][InlineData(1603, false)][InlineData(1618, false)]
    public void InstallerExitCodeDistinguishesSuccessFromRollback(int code, bool success) => Assert.Equal(success, UpdatePolicy.InstallSucceeded(code));
}
