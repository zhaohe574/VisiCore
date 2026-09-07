using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VideoPlatform.Updater;

internal static class Program
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Url is null || options.FileName is null || options.Sha256 is null || options.Parent is null || options.Restart is null)
        {
            Log("更新参数不完整");
            return 2;
        }
        while (true)
        {
            try
            {
                await RunAsync(options);
                return 0;
            }
            catch (Exception ex)
            {
                Log($"更新失败：{ex.Message}");
                if (options.VerifyOnly) return 1;
                if (options.Force)
                {
                    if (MessageBox.Show($"自动更新失败：{ex.Message}\n是否重试？", "京华安防平台更新", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry) continue;
                }
                else
                {
                    Log("普通更新未完成，恢复旧版本");
                    try { await WaitForParentAsync(options); Restart(options.Restart!); }
                    catch (Exception restartError) { Log($"恢复旧版本失败：{restartError.Message}"); }
                }
                return 1;
            }
        }
    }

    private static async Task RunAsync(Options options)
    {
        var url = options.Url ?? throw new InvalidOperationException("缺少下载地址");
        var restart = options.Restart ?? throw new InvalidOperationException("缺少重启路径");
        var expectedHash = options.Sha256 ?? throw new InvalidOperationException("缺少安装包哈希");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme is not ("http" or "https")) throw new InvalidOperationException("下载地址必须使用 HTTP 或 HTTPS");
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)) throw new InvalidOperationException("SHA-256 必须为 64 位十六进制字符串");
        if (!Path.IsPathFullyQualified(restart) || !File.Exists(restart)) throw new InvalidOperationException("桌面端重启路径无效");
        var fileName = Path.GetFileName(options.FileName ?? throw new InvalidOperationException("缺少安装包文件名"));
        if (fileName != options.FileName || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("自动更新仅支持 MSI 安装包，请手动下载 ZIP 或 EXE");
        var directory = Path.Combine(Path.GetTempPath(), "VideoPlatform-Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
        var package = Path.Combine(directory, fileName);
        await using (var source = await Http.GetStreamAsync(url))
        await using (var target = File.Create(package))
            await source.CopyToAsync(target);
        string actual;
        await using (var hashStream = File.OpenRead(package)) actual = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包 SHA-256 校验失败");
        Log("安装包 SHA-256 校验通过");
        if (options.VerifyOnly) return;
        // Windows Installer 的修复和升级回滚可能再次读取原始安装包。
        var cacheDirectory = Path.Combine(Path.GetDirectoryName(LogPath)!, "packages");
        Directory.CreateDirectory(cacheDirectory);
        var cachedPackage = Path.Combine(cacheDirectory, $"{actual}.msi");
        File.Copy(package, cachedPackage, true);
        await WaitForParentAsync(options);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var installerLog = Path.Combine(Path.GetDirectoryName(LogPath)!, $"msi-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        using var installer = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/i \"{cachedPackage}\" /passive /norestart /L*v \"{installerLog}\"") { UseShellExecute = true });
        if (installer is null) throw new InvalidOperationException("无法启动 Windows Installer");
        await installer.WaitForExitAsync();
        if (!InstallSucceeded(installer.ExitCode)) throw new InvalidOperationException($"MSI 安装失败，退出码 {installer.ExitCode}，详细日志：{installerLog}");
        if (installer.ExitCode is 3010 or 1641) Log("安装成功，Windows 需要重启以完成更新");
        Log("更新安装成功，正在重新启动桌面端");
        Restart(restart);
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (Exception ex) { Log($"清理临时安装包失败：{ex.Message}"); }
        }
    }

    internal static bool InstallSucceeded(int exitCode) => exitCode is 0 or 3010 or 1641;

    private static void Restart(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) })
            ?? throw new InvalidOperationException("无法重新启动桌面端");
    }

    private static async Task WaitForParentAsync(Options options)
    {
        if (options.ParentPid is not > 0) throw new InvalidOperationException("缺少有效的桌面端进程 ID");
        Process parent;
        try { parent = Process.GetProcessById(options.ParentPid.Value); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await parent.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { throw new TimeoutException("等待桌面端退出超时"); }
        }
    }

    private static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform", "updater.log");

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    private sealed record Options(string? Url, string? FileName, string? Sha256, string? Parent, int? ParentPid, string? Restart, bool Force, bool VerifyOnly)
    {
        public static Options Parse(string[] args)
        {
            string? Value(string key) { var index = Array.FindIndex(args, item => item.Equals(key, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
            return new Options(Value("--url"), Value("--file-name"), Value("--sha256"), Value("--parent"), int.TryParse(Value("--parent-pid"), out var pid) ? pid : null, Value("--restart"), args.Any(item => item.Equals("--force", StringComparison.OrdinalIgnoreCase)), args.Contains("--verify-only"));
        }
    }
}
