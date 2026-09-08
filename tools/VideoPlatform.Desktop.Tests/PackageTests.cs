using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class PackageFactAttribute : FactAttribute
{
    public PackageFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VP_PACKAGE_QA"))) Skip = "通过 tools/v2-package.ps1 -VerifyInstallation 创建隔离安装包后运行。";
    }
}

public sealed class PackageTests
{
    private static readonly string[] PayloadFiles = ["VideoPlatform.Desktop.exe", "VideoPlatform.Desktop.dll", "VideoPlatform.Client.dll", "VideoPlatform.Updater.exe"];

    [PackageArtifactFact]
    [Trait("Category", "Package")]
    public async Task FinalMsiAndZipPayloadsMatchPublishedFiles()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("VP_PACKAGE_OUTPUT")!);
        var publish = Path.Combine(output, "win-x64");
        var expected = await PayloadHashesAsync(publish);
        var evidence = Path.Combine(output, "payload-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        var manifestPath = Path.Combine(output, "release-manifest.json");
        var manifestHash = await HashAsync(manifestPath);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var version = Version.Parse(manifest.RootElement.GetProperty("version").GetString()!).ToString(3);
        var msi = Path.Combine(output, $"VideoPlatform.Desktop-{version}-x64.msi");
        var zip = Path.Combine(output, $"VideoPlatform.Desktop-{version}-win-x64.zip");
        foreach (var file in new[] { msi, zip })
        {
            var entry = manifest.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("fileName").GetString() == Path.GetFileName(file));
            Assert.Equal(new FileInfo(file).Length, entry.GetProperty("fileSize").GetInt64());
            Assert.Equal((await HashAsync(file)).ToLowerInvariant(), entry.GetProperty("sha256").GetString());
        }
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var name in PayloadFiles)
            {
                var entry = archive.Entries.Single(item => item.FullName == name);
                await using var stream = entry.Open();
                Assert.Equal(expected[name], Convert.ToHexString(await SHA256.HashDataAsync(stream)));
            }
        }
        var extract = Path.Combine(evidence, "msi-extracted");
        var exit = await RunAsync(Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            ["/a", msi, "/qn", "/norestart", "TARGETDIR=" + extract, "/L*v", Path.Combine(evidence, "msi-extract.log")]);
        Assert.Equal(0, exit);
        foreach (var name in PayloadFiles)
        {
            var file = Directory.EnumerateFiles(extract, name, SearchOption.AllDirectories).Single();
            Assert.Equal(expected[name], await HashAsync(file));
        }
        await AssertPayloadMatchesAsync(expected, publish);
        Assert.Equal(manifestHash, await HashAsync(manifestPath));
        await File.WriteAllLinesAsync(Path.Combine(evidence, "产物一致性验收.txt"),
            PayloadFiles.Select(name => $"通过：{name}，发布目录、ZIP、最终 MSI SHA256：{expected[name]}")
                .Append($"清单 SHA256：{manifestHash}"));
    }

    [Theory]
    [InlineData("VideoPlatform.Desktop.dll")]
    [InlineData("VideoPlatform.Client.dll")]
    [InlineData("VideoPlatform.Updater.exe")]
    [Trait("Category", "Package")]
    public async Task UnchangedApphostCannotHideStalePayload(string staleFile)
    {
        var directory = Path.Combine(Path.GetTempPath(), "VideoPlatform.Package.Payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in PayloadFiles) await File.WriteAllTextAsync(Path.Combine(directory, name), "当前产物：" + name);
            var expected = await PayloadHashesAsync(directory);
            await AssertPayloadMatchesAsync(expected, directory);
            // 模拟相同 apphost 搭配旧 DLL 或旧更新器，不能仅凭入口 EXE 判定通过。
            await File.WriteAllTextAsync(Path.Combine(directory, staleFile), "旧产物：" + staleFile);
            Assert.Equal(expected["VideoPlatform.Desktop.exe"], await HashAsync(Path.Combine(directory, "VideoPlatform.Desktop.exe")));
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => AssertPayloadMatchesAsync(expected, directory));
            Assert.Contains(staleFile, error.Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    [PackageFact]
    [Trait("Category", "Package")]
    public async Task IsolatedMsiInstallUpdateRollbackAndUninstall()
    {
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("VP_PACKAGE_QA")!);
        var upgradeCode = Guid.Parse(Environment.GetEnvironmentVariable("VP_PACKAGE_UPGRADE_CODE")!).ToString("B").ToUpperInvariant();
        var registryKey = Environment.GetEnvironmentVariable("VP_PACKAGE_REGISTRY")!;
        var updater = Path.GetFullPath(Environment.GetEnvironmentVariable("VP_PACKAGE_UPDATER")!);
        var expected = await PayloadHashesAsync(Path.GetDirectoryName(updater)!);
        Assert.StartsWith("Software\\Liteware\\VideoPlatform.Desktop.QA\\", registryKey);
        Assert.NotEqual("{8D1D5A6B-5E3C-4CF8-9D5E-9A7B1E5B7A11}", upgradeCode);
        Assert.Empty(RelatedProducts(upgradeCode));
        var install = Path.Combine(directory, "installed");
        var probe = Path.Combine(directory, "probe", "RestartProbe.exe");
        var marker = Path.Combine(directory, "probe", "restarted.txt");
        var initial = Path.Combine(directory, "base.msi");
        var failure = Path.Combine(directory, "failure.msi");
        var success = Path.Combine(directory, "success.msi");
        var outcomes = new List<string>();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.MapGet("/{name}", (string name) => name is "base.msi" or "failure.msi" or "success.msi"
            ? Results.File(Path.Combine(directory, name), "application/x-msi", enableRangeProcessing: true) : Results.NotFound());
        await server.StartAsync();
        var address = server.Urls.Single();
        try
        {
            var exit = await RunAsync(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), ["/i", initial, "/qn", "/norestart", "MSIRESTARTMANAGERCONTROL=Disable", "INSTALLFOLDER=" + install, "/L*v", Path.Combine(directory, "install.log")]);
            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(install, "VideoPlatform.Desktop.exe")));
            Assert.True(File.Exists(Path.Combine(install, "VideoPlatform.Updater.exe")));
            Assert.True(File.Exists(Path.Combine(install, "libvlc", "win-x64", "libvlc.dll")));
            Assert.Equal("2.0.0", ReadVersion(registryKey));
            await AssertPayloadMatchesAsync(expected, install);
            outcomes.Add("通过：独立 perUser 安装，桌面入口、Desktop.dll、Client.dll 和更新器哈希与发布目录一致，原生 VLC 文件完整。");

            var invalid = await InvokeUpdaterAsync(updater, probe, address + "/base.msi", initial, new string('0', 64), true);
            Assert.Equal(1, invalid); Assert.False(File.Exists(marker)); Assert.Equal("2.0.0", ReadVersion(registryKey));
            await AssertPayloadMatchesAsync(expected, install);
            outcomes.Add("通过：错误哈希拒绝安装且未重启程序。");

            var rollback = await InvokeUpdaterAsync(updater, probe, address + "/failure.msi", failure, await HashAsync(failure), false);
            Assert.Equal(1, rollback); await WaitForMarkerAsync(marker, 1);
            Assert.Equal("2.0.0", ReadVersion(registryKey));
            await AssertPayloadMatchesAsync(expected, install);
            Assert.Single(RelatedProducts(upgradeCode));
            outcomes.Add("通过：受控升级失败，MSI 恢复旧版注册信息与文件，更新器重新启动旧程序探针。");

            var updated = await InvokeUpdaterAsync(updater, probe, address + "/success.msi", success, await HashAsync(success), false);
            Assert.Equal(0, updated); await WaitForMarkerAsync(marker, 2);
            Assert.Equal("2.0.1", ReadVersion(registryKey)); Assert.Single(RelatedProducts(upgradeCode));
            Assert.True(File.Exists(Path.Combine(install, "VideoPlatform.Desktop.exe")));
            await AssertPayloadMatchesAsync(expected, install);
            await AssertPayloadMatchesAsync(expected, Path.GetDirectoryName(updater)!);
            outcomes.Add("通过：独立 MSI 升级成功并重启探针，旧产品记录已替换。");
        }
        finally
        {
            foreach (var product in RelatedProducts(upgradeCode))
            {
                var removed = await RunAsync(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), ["/x", product, "/qn", "/norestart", "/L*v", Path.Combine(directory, "uninstall.log")]);
                Assert.Equal(0, removed);
            }
            await server.StopAsync();
            await File.WriteAllLinesAsync(Path.Combine(directory, "安装更新验收.txt"), outcomes);
        }
        Assert.False(File.Exists(Path.Combine(install, "VideoPlatform.Desktop.exe")));
        Assert.Empty(RelatedProducts(upgradeCode));
        await File.AppendAllTextAsync(Path.Combine(directory, "安装更新验收.txt"), "通过：测试产品已卸载，正式安装未修改。" + Environment.NewLine);
    }

    private static async Task<int> InvokeUpdaterAsync(string updater, string probe, string url, string package, string hash, bool verifyOnly)
    {
        using var parent = Process.Start(new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--wait" } })!;
        var arguments = new List<string> { "--url", url, "--file-name", Path.GetFileName(package), "--sha256", hash, "--file-size", new FileInfo(package).Length.ToString(), "--parent", probe, "--parent-pid", parent.Id.ToString(), "--restart", probe, "--quiet" };
        if (verifyOnly) arguments.Add("--verify-only");
        var exit = await RunAsync(updater, arguments);
        await parent.WaitForExitAsync();
        return exit;
    }
    private static async Task<int> RunAsync(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileName(executable).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Windows Installer 要求属性值内部加引号，不能把整个“键=值”包在引号中。
            info.Arguments = string.Join(" ", arguments.Select(argument => argument.StartsWith("INSTALLFOLDER=", StringComparison.Ordinal) || argument.StartsWith("TARGETDIR=", StringComparison.Ordinal)
                ? argument[..(argument.IndexOf('=') + 1)] + "\"" + argument[(argument.IndexOf('=') + 1)..] + "\"" : argument.Contains(' ') ? "\"" + argument + "\"" : argument));
        }
        else foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动安装验收进程。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode;
    }
    private static async Task<string> HashAsync(string path) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)); }
    private static async Task<Dictionary<string, string>> PayloadHashesAsync(string directory)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in PayloadFiles) hashes.Add(name, await HashAsync(Path.Combine(directory, name)));
        return hashes;
    }
    private static async Task AssertPayloadMatchesAsync(IReadOnlyDictionary<string, string> expected, string directory)
    {
        foreach (var name in PayloadFiles)
            if (expected[name] != await HashAsync(Path.Combine(directory, name)))
                throw new InvalidDataException($"安装产物不一致：{name}，SHA256 与发布目录不符。");
    }
    private static string? ReadVersion(string key) { using var registry = Registry.CurrentUser.OpenSubKey(key); return registry?.GetValue("Version") as string; }
    private static async Task WaitForMarkerAsync(string path, int count)
    {
        for (var i = 0; i < 50; ++i)
        {
            if (File.Exists(path) && (await File.ReadAllLinesAsync(path)).Length >= count) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("未收到更新器重启探针的记录。");
    }
    private static string[] RelatedProducts(string upgradeCode)
    {
        var products = new List<string>();
        for (uint index = 0; ; ++index)
        {
            var result = new StringBuilder(39); var error = MsiEnumRelatedProducts(upgradeCode, 0, index, result);
            if (error == 259) return products.ToArray();
            if (error != 0) throw new InvalidOperationException($"读取隔离 MSI 安装记录失败：{error}。");
            products.Add(result.ToString());
        }
    }
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);
}

public sealed class PackageArtifactFactAttribute : FactAttribute
{
    public PackageArtifactFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VP_PACKAGE_OUTPUT")))
            Skip = "需要 VP_PACKAGE_OUTPUT 指向包含发布目录、最终 MSI、ZIP 和清单的产物目录。";
    }
}
