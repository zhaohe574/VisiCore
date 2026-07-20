using System.Text.Json.Nodes;
using VisiCore.Setup;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class EncryptedBackupArchiveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"visicore-backup-tests-{Guid.NewGuid():N}");

    public EncryptedBackupArchiveTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task 加密归档会移除内部数据库连接并可由恢复密钥解开()
    {
        var dump = Path.Combine(directory, "database.dump");
        var config = Path.Combine(directory, "config");
        var archive = Path.Combine(directory, "platform.vcbackup");
        await File.WriteAllTextAsync(dump, "database-dump");
        Directory.CreateDirectory(config);
        await File.WriteAllTextAsync(Path.Combine(config, "runtime.json"), "{\"ConnectionStrings\":{\"Platform\":\"Host=secret\"},\"Gateway\":{\"GatewayName\":\"core\"}}");
        await File.WriteAllTextAsync(Path.Combine(config, "internal-postgres-password"), "must-not-export");

        await EncryptedBackupArchive.CreateAsync(archive, new string('k', 64), dump, config, CancellationToken.None);
        var restored = await EncryptedBackupArchive.VerifyAndExtractAsync(archive, new string('k', 64), Path.Combine(directory, "restored"), CancellationToken.None);

        var runtime = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(restored.ConfigurationDirectory, "runtime.json")))!.AsObject();
        Assert.Null(runtime["ConnectionStrings"]?["Platform"]);
        Assert.Equal("database-dump", await File.ReadAllTextAsync(restored.DatabaseDumpPath));
        Assert.False(File.Exists(Path.Combine(restored.ConfigurationDirectory, "internal-postgres-password")));
    }

    [Fact]
    public async Task 篡改归档会被认证校验拒绝()
    {
        var dump = Path.Combine(directory, "database.dump");
        var config = Path.Combine(directory, "config");
        var archive = Path.Combine(directory, "platform.vcbackup");
        await File.WriteAllTextAsync(dump, "database-dump");
        Directory.CreateDirectory(config);
        await File.WriteAllTextAsync(Path.Combine(config, "runtime.json"), "{}");
        await EncryptedBackupArchive.CreateAsync(archive, new string('k', 64), dump, config, CancellationToken.None);

        await using (var stream = new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = 40;
            var value = stream.ReadByte();
            stream.Position = 40;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            EncryptedBackupArchive.VerifyAndExtractAsync(archive, new string('k', 64), Path.Combine(directory, "restored"), CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
