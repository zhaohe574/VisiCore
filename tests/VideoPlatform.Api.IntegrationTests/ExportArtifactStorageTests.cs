using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using VideoPlatform.Api;
using Xunit;

namespace VideoPlatform.Api.IntegrationTests;

public sealed class ExportArtifactStorageTests
{
    [Fact(DisplayName = "归档目录可以是合法文件系统根且仍拒绝路径越界")]
    public async Task RootDirectoryPreservesContainment()
    {
        var root = Path.GetPathRoot(Path.GetTempPath()) ?? throw new InvalidOperationException("无法确定临时目录根路径。");
        var store = new LocalExportArtifactStore(Options.Create(new ExportArtifactStorageOptions { RootDirectory = root }));

        Assert.Null(await store.OpenReadAsync($"exports/{Guid.NewGuid():N}/missing.mp4", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenReadAsync("../outside.mp4", CancellationToken.None));
    }

    [Fact(DisplayName = "中心归档存储流式校验 SHA-256 并禁止路径由上传方指定")]
    public async Task StoreValidatesHashAndUsesGeneratedStorageKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "video-platform-export-tests", Guid.NewGuid().ToString("N"));
        var bytes = "verified-export-content"u8.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var store = new LocalExportArtifactStore(Options.Create(new ExportArtifactStorageOptions { RootDirectory = root }));
        try
        {
            await using var content = new MemoryStream(bytes, writable: false);
            var stored = await store.StoreAsync(Guid.NewGuid(), content, bytes.Length, sha256, CancellationToken.None);

            Assert.StartsWith("exports/", stored.StorageKey, StringComparison.Ordinal);
            Assert.Equal(bytes.Length, stored.SizeBytes);
            Assert.Equal(sha256, stored.Sha256);
            await using var restored = Assert.IsAssignableFrom<Stream>(await store.OpenReadAsync(stored.StorageKey, CancellationToken.None));
            using var memory = new MemoryStream();
            await restored.CopyToAsync(memory);
            Assert.Equal(bytes, memory.ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "中心归档存储拒绝长度或哈希不一致的上传")]
    public async Task StoreRejectsInvalidIntegrityMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "video-platform-export-tests", Guid.NewGuid().ToString("N"));
        var bytes = "invalid-export-content"u8.ToArray();
        var store = new LocalExportArtifactStore(Options.Create(new ExportArtifactStorageOptions { RootDirectory = root }));
        try
        {
            await using var content = new MemoryStream(bytes, writable: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(
                Guid.NewGuid(),
                content,
                bytes.Length,
                new string('0', 64),
                CancellationToken.None));
            Assert.False(Directory.Exists(root) && Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "中心归档只清理 exports 命名空间内超过阈值的中断上传临时文件")]
    public async Task DeleteStaleTemporaryFilesOnlyDeletesExpiredPartialFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "video-platform-export-tests", Guid.NewGuid().ToString("N"));
        var exportDirectory = Path.Combine(root, "exports", Guid.NewGuid().ToString("N"));
        var stale = Path.Combine(exportDirectory, "stale.mp4.partial");
        var recent = Path.Combine(exportDirectory, "recent.mp4.partial");
        var unrelated = Path.Combine(root, "unrelated.mp4.partial");
        try
        {
            Directory.CreateDirectory(exportDirectory);
            await File.WriteAllTextAsync(stale, "stale");
            await File.WriteAllTextAsync(recent, "recent");
            await File.WriteAllTextAsync(unrelated, "unrelated");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

            var store = new LocalExportArtifactStore(Options.Create(new ExportArtifactStorageOptions
            {
                RootDirectory = root,
                TemporaryFileRetentionMinutes = 60
            }));
            var result = await store.DeleteStaleTemporaryFilesAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Equal(1, result.Deleted);
            Assert.Equal(0, result.Failed);
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
