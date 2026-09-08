using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public interface ICredentialStore
{
    SavedSession? Load();
    void Save(SavedSession session);
    void Clear();
}

public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = "VideoPlatform.Desktop.v2"u8.ToArray();
    private readonly string _path = Path.Combine(ClientFiles.Directory, "session.bin");
    public SavedSession? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<SavedSession>(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            ClientFiles.Log($"读取加密登录状态失败：{ex.Message}");
            Clear();
            return null;
        }
    }
    public void Save(SavedSession session)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            Directory.CreateDirectory(ClientFiles.Directory);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporary, _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Clear() { if (File.Exists(_path)) File.Delete(_path); }
}

public static class ClientFiles
{
    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform");
    public static ClientSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(Directory, "desktop-v2.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) { Log($"读取客户端设置失败：{ex.Message}"); return new(); }
    }
    public static void SaveSettings(ClientSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, "desktop-v2.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings));
        File.Move(path + ".tmp", path, true);
    }
    public static void Log(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, "desktop.log");
            lock (LogLock)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}{Environment.NewLine}");
            }
        }
        // 日志属于尽力记录，不能因为目录权限或文件被占用而影响值守操作。
        catch (Exception) { }
    }
    private static readonly object LogLock = new();
}

public interface IUiDispatcher { Task InvokeAsync(Func<Task> action); }
public sealed class UiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Func<Task> action) => Application.Current.Dispatcher.CheckAccess()
        ? action() : Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
}
public interface IUserInteraction
{
    string? SaveFile(string suggestedName, string filter);
    bool Confirm(string message);
    void Shutdown();
}
public sealed class UserInteraction : IUserInteraction
{
    public string? SaveFile(string suggestedName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = Path.GetFileName(suggestedName), Filter = filter, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
    public bool Confirm(string message) => MessageBox.Show(Application.Current.MainWindow, message, "VisiCore（视枢）", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public void Shutdown() => Application.Current.MainWindow.Close();
}
