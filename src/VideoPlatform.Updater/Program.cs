using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VideoPlatform.Updater;

internal static class Program
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = TimeSpan.FromMinutes(30) };

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
                    if (MessageBox.Show($"自动更新失败：{ex.Message}\n是否重试？", "VisiCore（视枢）更新", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry) continue;
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
        var fileName = Path.GetFileName(options.FileName ?? throw new InvalidOperationException("缺少安装包文件名"));
        var downloadUri = UpdatePolicy.Validate(url, options.FileName!, expectedHash, restart);
        if (!string.Equals(Path.GetFullPath(options.Parent!), Path.GetFullPath(restart), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("更新父进程路径与重启路径不一致。");
        var directory = Path.Combine(Path.GetTempPath(), "VideoPlatform-Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
        var package = Path.Combine(directory, fileName);
        using var download = await Http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
        download.EnsureSuccessStatusCode();
        if (options.FileSize is { } expectedSize && download.Content.Headers.ContentLength is { } reportedSize && expectedSize != reportedSize) throw new InvalidDataException("服务器安装包长度与发布记录不一致。");
        await using (var source = await download.Content.ReadAsStreamAsync())
        await using (var target = File.Create(package))
            await source.CopyToAsync(target);
        var actual = await UpdatePolicy.VerifyAsync(package, expectedHash, options.FileSize);
        Log("安装包 SHA-256 校验通过");
        if (options.VerifyOnly) return;
        // Windows Installer 的修复和升级回滚可能再次读取原始安装包。
        var cacheDirectory = Path.Combine(Path.GetDirectoryName(LogPath)!, "packages");
        Directory.CreateDirectory(cacheDirectory);
        var cachedPackage = Path.Combine(cacheDirectory, $"{actual}.msi");
        File.Copy(package, cachedPackage, true);
        await UpdatePolicy.VerifyAsync(cachedPackage, expectedHash, options.FileSize);
        await WaitForParentAsync(options);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var installerLog = Path.Combine(Path.GetDirectoryName(LogPath)!, $"msi-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var installerUi = options.Quiet ? "/quiet" : "/passive";
        using var installer = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/i \"{cachedPackage}\" {installerUi} /norestart MSIRESTARTMANAGERCONTROL=Disable /L*v \"{installerLog}\"") { UseShellExecute = true });
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

    internal static bool InstallSucceeded(int exitCode) => UpdatePolicy.InstallSucceeded(exitCode);

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
            if (!parent.HasExited && !string.Equals(parent.MainModule?.FileName, options.Parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("父进程 ID 对应的程序已改变，更新已取消。");
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

    private sealed record Options(string? Url, string? FileName, string? Sha256, string? Parent, int? ParentPid, string? Restart, bool Force, bool VerifyOnly, long? FileSize, bool Quiet)
    {
        public static Options Parse(string[] args)
        {
            string? Value(string key) { var index = Array.FindIndex(args, item => item.Equals(key, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
            return new Options(Value("--url"), Value("--file-name"), Value("--sha256"), Value("--parent"), int.TryParse(Value("--parent-pid"), out var pid) ? pid : null, Value("--restart"), args.Any(item => item.Equals("--force", StringComparison.OrdinalIgnoreCase)), args.Contains("--verify-only"), long.TryParse(Value("--file-size"), out var size) ? size : null, args.Contains("--quiet"));
        }
    }
}
