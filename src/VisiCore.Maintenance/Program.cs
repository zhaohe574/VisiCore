using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using VisiCore.Setup;

if (args.Length == 0 || !string.Equals(args[0], "restore", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("仅支持 restore 维护命令。 ");
    return 2;
}

var requestPath = GetRequiredOption(args, "--request");
var configurationDirectory = GetRequiredOption(args, "--config-directory");
var postgresPasswordPath = GetRequiredOption(args, "--postgres-password-file");
var backupKeyPath = GetRequiredOption(args, "--backup-key-file");
var request = JsonSerializer.Deserialize<BackupRestoreRequest>(await File.ReadAllTextAsync(requestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidDataException("恢复请求无效。 ");
if (!Path.IsPathFullyQualified(request.ArchivePath) || !File.Exists(request.ArchivePath) || string.IsNullOrWhiteSpace(request.RecoveryKey))
{
    throw new InvalidDataException("恢复请求缺少可用归档或恢复密钥。 ");
}

var workDirectory = Path.Combine(Path.GetTempPath(), $"visicore-restore-{Guid.NewGuid():N}");
Directory.CreateDirectory(workDirectory);
try
{
    var contents = await EncryptedBackupArchive.VerifyAndExtractAsync(request.ArchivePath, request.RecoveryKey, workDirectory, CancellationToken.None);
    var password = (await File.ReadAllTextAsync(postgresPasswordPath)).Trim();
    if (password.Length < 32) throw new InvalidDataException("内置 PostgreSQL 密码不可用。 ");

    await RunPostgreSqlToolAsync("dropdb", ["--if-exists", "--force", "--host=127.0.0.1", "--port=5432", "--username=visicore", "visicore"], password);
    await RunPostgreSqlToolAsync("createdb", ["--host=127.0.0.1", "--port=5432", "--username=visicore", "visicore"], password);
    await RunPostgreSqlToolAsync("pg_restore", ["--exit-on-error", "--no-owner", "--no-privileges", "--host=127.0.0.1", "--port=5432", "--username=visicore", "--dbname=visicore", contents.DatabaseDumpPath], password);

    await RestoreConfigurationAsync(contents.ConfigurationDirectory, configurationDirectory, password, request.RecoveryKey, postgresPasswordPath, backupKeyPath);
}
finally
{
    if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true);
}

return 0;

static string GetRequiredOption(string[] arguments, string option)
{
    var index = Array.FindIndex(arguments, value => string.Equals(value, option, StringComparison.OrdinalIgnoreCase));
    if (index < 0 || index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
    {
        throw new ArgumentException($"缺少{option}参数。 ");
    }
    return arguments[index + 1];
}

static async Task RunPostgreSqlToolAsync(string command, IReadOnlyList<string> arguments, string password)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(command)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.StartInfo.Environment["PGPASSWORD"] = password;
    foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
    process.Start();
    var standardError = await process.StandardError.ReadToEndAsync();
    var standardOutput = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        var detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        throw new InvalidOperationException($"PostgreSQL 恢复步骤 {command} 失败：{detail.Trim()[..Math.Min(detail.Trim().Length, 512)]}");
    }
}

static async Task RestoreConfigurationAsync(
    string sourceDirectory,
    string destinationDirectory,
    string password,
    string recoveryKey,
    string postgresPasswordPath,
    string backupKeyPath)
{
    var runtimePath = Path.Combine(sourceDirectory, "runtime.json");
    if (!File.Exists(runtimePath)) throw new InvalidDataException("备份缺少运行配置。 ");
    var runtime = JsonNode.Parse(await File.ReadAllTextAsync(runtimePath))?.AsObject()
        ?? throw new InvalidDataException("备份运行配置无效。 ");
    var connectionStrings = runtime["ConnectionStrings"]?.AsObject() ?? new JsonObject();
    connectionStrings["Platform"] = new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1",
        Port = 5432,
        Database = "visicore",
        Username = "visicore",
        Password = password,
        SslMode = SslMode.Disable,
        Pooling = true
    }.ConnectionString;
    runtime["ConnectionStrings"] = connectionStrings;

    Directory.CreateDirectory(destinationDirectory);
    foreach (var file in Directory.EnumerateFiles(destinationDirectory, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(destinationDirectory, file);
        if (relative.Equals(Path.GetFileName(postgresPasswordPath), StringComparison.OrdinalIgnoreCase) ||
            relative.Equals(Path.GetFileName(backupKeyPath), StringComparison.OrdinalIgnoreCase)) continue;
        File.Delete(file);
    }
    foreach (var directory in Directory.EnumerateDirectories(destinationDirectory, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length))
    {
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(sourceDirectory, sourceFile);
        if (relative.Equals("runtime.json", StringComparison.OrdinalIgnoreCase)) continue;
        var destination = Path.Combine(destinationDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourceFile, destination, overwrite: true);
    }
    await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "runtime.json"), runtime.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(backupKeyPath, recoveryKey.Trim());
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(postgresPasswordPath, UnixFileMode.UserRead);
        File.SetUnixFileMode(backupKeyPath, UnixFileMode.UserRead);
        File.SetUnixFileMode(Path.Combine(destinationDirectory, "runtime.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

public sealed record BackupRestoreRequest(string ArchivePath, string RecoveryKey, string? RequestedBy = null);
