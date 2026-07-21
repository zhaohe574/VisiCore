using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var jobPath = GetRequiredOption(args, "--job");
if (!Path.IsPathFullyQualified(jobPath) || !File.Exists(jobPath))
{
    return 2;
}

WindowsUpdateJob? job;
try
{
    job = JsonSerializer.Deserialize<WindowsUpdateJob>(await File.ReadAllTextAsync(jobPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
catch (JsonException)
{
    return 2;
}
if (job is null || job.OperationId == Guid.Empty || !IsUnderHostState(jobPath) || !IsUnderHostState(job.ReceiptPath) ||
    !Path.IsPathFullyQualified(job.InstallerPath) || !File.Exists(job.InstallerPath) || !job.InstallerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
    !Path.IsPathFullyQualified(job.WindowsInstallerExecutablePath) || !File.Exists(job.WindowsInstallerExecutablePath) ||
    !string.Equals(job.WindowsInstallerExecutablePath, Path.Combine(Environment.SystemDirectory, "msiexec.exe"), StringComparison.OrdinalIgnoreCase))
{
    return 2;
}

await using var installerStream = File.OpenRead(job.InstallerPath);
var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream)).ToLowerInvariant();
if (!CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.ASCII.GetBytes(actualHash),
        System.Text.Encoding.ASCII.GetBytes(job.InstallerSha256.ToLowerInvariant())))
{
    await WriteReceiptAsync(job, false, "windows_update_hash_mismatch");
    return 3;
}

var exitCode = await RunInstallerAsync(job);
if (exitCode is 0 or 3010)
{
    await WriteReceiptAsync(job, true, exitCode == 3010 ? "reboot_required" : null);
    return exitCode;
}

await WriteReceiptAsync(job, false, "windows_installer_exit_nonzero");
return exitCode == 0 ? 4 : exitCode;

static string GetRequiredOption(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : string.Empty;
}

static bool IsUnderHostState(string path)
{
    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeHostAgent");
    var absolute = Path.GetFullPath(path);
    var absoluteRoot = Path.GetFullPath(root);
    return absolute.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

static async Task<int> RunInstallerAsync(WindowsUpdateJob job)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = job.WindowsInstallerExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }
    };
    process.StartInfo.ArgumentList.Add("/i");
    process.StartInfo.ArgumentList.Add(job.InstallerPath);
    process.StartInfo.ArgumentList.Add("/qn");
    process.StartInfo.ArgumentList.Add("/norestart");
    if (!process.Start()) return 4;
    await process.WaitForExitAsync();
    return process.ExitCode;
}

static async Task WriteReceiptAsync(WindowsUpdateJob job, bool succeeded, string? failureKind)
{
    var directory = Path.GetDirectoryName(job.ReceiptPath)!;
    Directory.CreateDirectory(directory);
    var temporaryPath = Path.Combine(directory, $".{job.OperationId:N}.{Guid.NewGuid():N}.tmp");
    try
    {
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(new
        {
            operationId = job.OperationId,
            succeeded,
            failureKind,
            completedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.Move(temporaryPath, job.ReceiptPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
    }
}

public sealed record WindowsUpdateJob(
    Guid OperationId,
    string InstallerPath,
    string InstallerSha256,
    string WindowsInstallerExecutablePath,
    string ReceiptPath);
