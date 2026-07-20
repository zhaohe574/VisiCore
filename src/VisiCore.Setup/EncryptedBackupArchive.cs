using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VisiCore.Setup;

/// <summary>
/// 平台离线备份格式。采用 PBKDF2 派生 AES-CBC 密钥，再使用独立 HMAC 做 Encrypt-then-MAC 校验，避免把敏感配置以明文放入备份卷。
/// </summary>
public static class EncryptedBackupArchive
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VCBK01");
    private const int SaltLength = 16;
    private const int IvLength = 16;
    private const int HmacLength = 32;
    private static readonly int HeaderLength = Magic.Length + SaltLength + IvLength;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task CreateAsync(
        string archivePath,
        string recoveryKey,
        string databaseDumpPath,
        string configurationDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databaseDumpPath)) throw new FileNotFoundException("数据库转储文件不存在。", databaseDumpPath);
        var archiveDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
        Directory.CreateDirectory(archiveDirectory);
        var temporaryZip = Path.Combine(archiveDirectory, $".{Guid.NewGuid():N}.zip");
        var temporaryCipher = Path.Combine(archiveDirectory, $".{Guid.NewGuid():N}.cipher");
        try
        {
            await CreateZipAsync(temporaryZip, databaseDumpPath, configurationDirectory, cancellationToken);
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var iv = RandomNumberGenerator.GetBytes(IvLength);
            var keys = DeriveKeys(recoveryKey, salt);
            await EncryptAsync(temporaryZip, temporaryCipher, keys.EncryptionKey, iv, cancellationToken);
            await WriteEnvelopeAsync(archivePath, temporaryCipher, salt, iv, keys.AuthenticationKey, cancellationToken);
        }
        finally
        {
            DeleteIfExists(temporaryZip);
            DeleteIfExists(temporaryCipher);
        }
    }

    public static async Task<bool> IsSupportedAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath)) return false;
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length <= HeaderLength + HmacLength) return false;
        var header = new byte[Magic.Length];
        await ReadExactlyAsync(stream, header, cancellationToken);
        return header.AsSpan().SequenceEqual(Magic);
    }

    public static async Task<BackupArchiveContents> VerifyAndExtractAsync(
        string archivePath,
        string recoveryKey,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists || archiveInfo.Length <= HeaderLength + HmacLength)
        {
            throw new InvalidDataException("备份文件不存在或格式无效。 ");
        }

        Directory.CreateDirectory(destinationDirectory);
        var temporaryCipher = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.cipher");
        var temporaryZip = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.zip");
        try
        {
            byte[] salt;
            byte[] iv;
            byte[] expectedMac;
            await using (var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous))
            {
                var header = new byte[HeaderLength];
                await ReadExactlyAsync(source, header, cancellationToken);
                if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                {
                    throw new InvalidDataException("备份文件不是受支持的视枢加密归档。 ");
                }
                salt = header.AsSpan(Magic.Length, SaltLength).ToArray();
                iv = header.AsSpan(Magic.Length + SaltLength, IvLength).ToArray();
                var cipherLength = source.Length - HeaderLength - HmacLength;
                await using var cipher = new FileStream(temporaryCipher, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous);
                using var mac = new HMACSHA256(DeriveKeys(recoveryKey, salt).AuthenticationKey);
                mac.TransformBlock(header, 0, header.Length, null, 0);
                var buffer = new byte[131_072];
                long remaining = cipherLength;
                while (remaining > 0)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                    if (read == 0) throw new InvalidDataException("备份密文不完整。 ");
                    await cipher.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    mac.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }
                expectedMac = new byte[HmacLength];
                await ReadExactlyAsync(source, expectedMac, cancellationToken);
                mac.TransformFinalBlock([], 0, 0);
                if (!CryptographicOperations.FixedTimeEquals(expectedMac, mac.Hash!))
                {
                    throw new InvalidDataException("恢复密钥错误或备份文件已被篡改。 ");
                }
            }

            var keys = DeriveKeys(recoveryKey, salt);
            await DecryptAsync(temporaryCipher, temporaryZip, keys.EncryptionKey, iv, cancellationToken);
            return await ExtractZipAsync(temporaryZip, destinationDirectory, cancellationToken);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("恢复密钥错误或备份文件已损坏。", exception);
        }
        finally
        {
            DeleteIfExists(temporaryCipher);
            DeleteIfExists(temporaryZip);
        }
    }

    private static async Task CreateZipAsync(string zipPath, string databaseDumpPath, string configurationDirectory, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        var manifest = new BackupArchiveManifest(1, DateTimeOffset.UtcNow, "database.dump", "config/runtime.json");
        await WriteEntryAsync(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        await CopyFileEntryAsync(archive, "database.dump", databaseDumpPath, cancellationToken);

        if (!Directory.Exists(configurationDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(configurationDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(configurationDirectory, path).Replace('\\', '/');
            if (IsExcludedConfigurationFile(relative)) continue;
            if (string.Equals(relative, "runtime.json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSanitizedRuntimeConfigurationAsync(archive, path, cancellationToken);
                continue;
            }
            await CopyFileEntryAsync(archive, $"config/{relative}", path, cancellationToken);
        }
    }

    private static async Task WriteSanitizedRuntimeConfigurationAsync(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject()
            ?? throw new InvalidDataException("运行配置不是有效 JSON。 ");
        if (node["ConnectionStrings"] is JsonObject connectionStrings)
        {
            connectionStrings.Remove("Platform");
        }
        await WriteEntryAsync(archive, "config/runtime.json", node.ToJsonString(JsonOptions), cancellationToken);
    }

    private static bool IsExcludedConfigurationFile(string relativePath) =>
        relativePath.Equals("internal-postgres-password", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Equals("backup-recovery.key", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);

    private static async Task<BackupArchiveContents> ExtractZipAsync(string zipPath, string destinationDirectory, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("备份缺少清单。 ");
        BackupArchiveManifest manifest;
        await using (var stream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupArchiveManifest>(stream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("备份清单无效。 ");
        }
        if (manifest.SchemaVersion != 1 || manifest.DatabaseDumpPath != "database.dump" || manifest.RuntimeConfigurationPath != "config/runtime.json")
        {
            throw new InvalidDataException("备份版本不受支持。 ");
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var target = GetSafeExtractionPath(destinationDirectory, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous);
            await source.CopyToAsync(destination, cancellationToken);
        }

        var databaseDumpPath = Path.Combine(destinationDirectory, "database.dump");
        var configurationPath = Path.Combine(destinationDirectory, "config", "runtime.json");
        if (!File.Exists(databaseDumpPath) || !File.Exists(configurationPath))
        {
            throw new InvalidDataException("备份缺少数据库或运行配置内容。 ");
        }
        return new BackupArchiveContents(manifest, databaseDumpPath, Path.Combine(destinationDirectory, "config"));
    }

    private static string GetSafeExtractionPath(string directory, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
        {
            throw new InvalidDataException("备份包含无效路径。 ");
        }
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(directory, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("备份包含越界路径。 ");
        return target;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string entryName, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static async Task CopyFileEntryAsync(ZipArchive archive, string entryName, string sourcePath, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task EncryptAsync(string sourcePath, string destinationPath, byte[] key, byte[] iv, CancellationToken cancellationToken)
    {
        using var algorithm = Aes.Create();
        algorithm.Key = key;
        algorithm.IV = iv;
        algorithm.Mode = CipherMode.CBC;
        algorithm.Padding = PaddingMode.PKCS7;
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous);
        await using var crypto = new CryptoStream(destination, algorithm.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
        await source.CopyToAsync(crypto, cancellationToken);
        crypto.FlushFinalBlock();
    }

    private static async Task DecryptAsync(string sourcePath, string destinationPath, byte[] key, byte[] iv, CancellationToken cancellationToken)
    {
        using var algorithm = Aes.Create();
        algorithm.Key = key;
        algorithm.IV = iv;
        algorithm.Mode = CipherMode.CBC;
        algorithm.Padding = PaddingMode.PKCS7;
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous);
        await using var crypto = new CryptoStream(source, algorithm.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: false);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous);
        await crypto.CopyToAsync(destination, cancellationToken);
    }

    private static async Task WriteEnvelopeAsync(string archivePath, string cipherPath, byte[] salt, byte[] iv, byte[] authenticationKey, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        salt.CopyTo(header, Magic.Length);
        iv.CopyTo(header, Magic.Length + SaltLength);
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous);
        await output.WriteAsync(header, cancellationToken);
        using var mac = new HMACSHA256(authenticationKey);
        mac.TransformBlock(header, 0, header.Length, null, 0);
        await using var cipher = new FileStream(cipherPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous);
        var buffer = new byte[131_072];
        int read;
        while ((read = await cipher.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            mac.TransformBlock(buffer, 0, read, null, 0);
        }
        mac.TransformFinalBlock([], 0, 0);
        await output.WriteAsync(mac.Hash!, cancellationToken);
    }

    private static (byte[] EncryptionKey, byte[] AuthenticationKey) DeriveKeys(string recoveryKey, byte[] salt)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey) || recoveryKey.Trim().Length < 32)
        {
            throw new InvalidDataException("恢复密钥无效。 ");
        }
        var material = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(recoveryKey.Trim()), salt, 210_000, HashAlgorithmName.SHA512, 64);
        return (material[..32], material[32..]);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) throw new InvalidDataException("备份文件不完整。 ");
            offset += read;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed record BackupArchiveManifest(int SchemaVersion, DateTimeOffset CreatedAt, string DatabaseDumpPath, string RuntimeConfigurationPath);
public sealed record BackupArchiveContents(BackupArchiveManifest Manifest, string DatabaseDumpPath, string ConfigurationDirectory);
