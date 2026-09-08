using System.Diagnostics;
using System.IO;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public sealed class UpdateService(IPlatformApi api)
{
    public static Version CurrentVersion { get; } = ReadCurrentVersion();
    private static Version ReadCurrentVersion()
    {
        var version = typeof(UpdateService).Assembly.GetName().Version!;
        return new Version(version.Major, version.Minor, version.Build);
    }
    public Task<Release?> CheckAsync() => api.GetAsync<Release>("public/releases/latest");
    public static bool IsRequired(Release release) => Version.TryParse(release.Version, out var latest) && latest > CurrentVersion &&
        (release.ForceUpdate || Version.TryParse(release.MinimumVersion, out var minimum) && CurrentVersion < minimum);
    public void Launch(Release release, string server)
    {
        if (!release.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("自动更新需要 MSI 安装包，请在平台下载页获取安装版。");
        var download = new Uri(new Uri(server + "/"), release.DownloadUrl);
        if (download.Scheme != "https" && !download.IsLoopback) throw new InvalidOperationException("更新安装包必须通过 HTTPS 下载。");
        if (release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit)) throw new InvalidOperationException("版本信息缺少有效 SHA-256 校验值。");
        var updater = Path.Combine(AppContext.BaseDirectory, "VideoPlatform.Updater.exe");
        if (!File.Exists(updater)) throw new FileNotFoundException("安装目录中缺少独立更新器，请重新安装客户端。");
        var directory = Path.Combine(Path.GetTempPath(), "VideoPlatform-Updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "VideoPlatform.Updater*")) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        var start = new ProcessStartInfo(Path.Combine(directory, "VideoPlatform.Updater.exe")) { UseShellExecute = false, WorkingDirectory = directory, CreateNoWindow = true };
        foreach (var argument in new[] { "--url", download.AbsoluteUri, "--file-name", release.FileName, "--sha256", release.Sha256, "--file-size", release.FileSize.ToString(), "--parent", Environment.ProcessPath!, "--parent-pid", Environment.ProcessId.ToString(), "--restart", Environment.ProcessPath! }) start.ArgumentList.Add(argument);
        if (IsRequired(release)) start.ArgumentList.Add("--force");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动独立更新器。");
    }
}
