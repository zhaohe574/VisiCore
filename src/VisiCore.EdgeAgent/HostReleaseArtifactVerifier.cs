using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using VisiCore.Core;

namespace VisiCore.EdgeAgent;

/// <summary>
/// Host Agent 独立下载、落盘并校验发行制品。业务 Agent 只传递受签名清单，
/// 不能传入本地路径、下载参数或任何命令。
/// </summary>
public sealed class HostReleaseArtifactVerifier(HostAgentOptions options)
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    private static readonly HashSet<string> LinuxBundleEntries = new(StringComparer.Ordinal)
    {
        "compose.yaml",
        ".env.example",
        "bootstrap.json.example"
    };

    public async Task<HostArtifactVerificationResult> DownloadAndVerifyAsync(
        EdgeReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!TryGetArtifactUri(manifest, out var artifactUri, out var failureKind))
        {
            return HostArtifactVerificationResult.Failed(failureKind);
        }

        var fileName = Path.GetFileName(artifactUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, artifactUri.Segments.LastOrDefault()?.Trim('/'), StringComparison.Ordinal) ||
            (manifest.TargetPlatform == "windows" && !fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) ||
            (manifest.TargetPlatform == "linux" && !fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)))
        {
            return HostArtifactVerificationResult.Failed("artifact_name_invalid");
        }

        var releaseDirectory = Path.Combine(
            Path.GetFullPath(options.ReleaseArtifactDirectory),
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest.ReleaseId))).ToLowerInvariant());
        var artifactPath = Path.Combine(releaseDirectory, fileName);
        var temporaryPath = Path.Combine(releaseDirectory, $".{Guid.NewGuid():N}.download");
        try
        {
            Directory.CreateDirectory(releaseDirectory);
            var response = await GetArtifactResponseAsync(artifactUri, cancellationToken);
            if (response is null)
            {
                return HostArtifactVerificationResult.Failed("artifact_redirect_untrusted");
            }
            using (response)
            {
            if (!response.IsSuccessStatusCode)
            {
                return HostArtifactVerificationResult.Failed("artifact_download_failed");
            }
            if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > options.MaximumArtifactBytes)
            {
                return HostArtifactVerificationResult.Failed("artifact_too_large");
            }

            var downloadedBytes = 0L;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            var buffer = new byte[81_920];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                downloadedBytes += read;
                if (downloadedBytes > options.MaximumArtifactBytes)
                {
                    return HostArtifactVerificationResult.Failed("artifact_too_large");
                }
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await destination.FlushAsync(cancellationToken);

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(actualHash),
                    System.Text.Encoding.ASCII.GetBytes(manifest.ArtifactSha256)))
            {
                return HostArtifactVerificationResult.Failed("artifact_hash_mismatch");
            }

            File.Move(temporaryPath, artifactPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(artifactPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            if (manifest.TargetPlatform == "windows")
            {
                return HostArtifactVerificationResult.Success(new HostVerifiedReleaseArtifact(
                    artifactPath,
                    null,
                    manifest.ArtifactSha256));
            }

            var composeFilePath = await ExtractLinuxBundleAsync(artifactPath, releaseDirectory, cancellationToken);
            return composeFilePath is null
                ? HostArtifactVerificationResult.Failed("artifact_bundle_invalid")
                : HostArtifactVerificationResult.Success(new HostVerifiedReleaseArtifact(
                    artifactPath,
                    composeFilePath,
                    manifest.ArtifactSha256));
            }
        }
        catch (HttpRequestException)
        {
            return HostArtifactVerificationResult.Failed("artifact_download_failed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HostArtifactVerificationResult.Failed("artifact_download_timeout");
        }
        catch (IOException)
        {
            return HostArtifactVerificationResult.Failed("artifact_storage_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return HostArtifactVerificationResult.Failed("artifact_storage_failed");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<bool> VerifyPersistedAsync(
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(artifact.ArtifactPath) ||
            string.IsNullOrWhiteSpace(artifact.ArtifactSha256) ||
            (artifact.ComposeFilePath is not null && !File.Exists(artifact.ComposeFilePath)))
        {
            return false;
        }

        try
        {
            await using var stream = new FileStream(
                artifact.ArtifactPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(actualHash),
                System.Text.Encoding.ASCII.GetBytes(artifact.ArtifactSha256));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool TryGetArtifactUri(EdgeReleaseManifest manifest, out Uri artifactUri, out string failureKind)
    {
        artifactUri = null!;
        failureKind = "artifact_source_invalid";
        if (!Uri.TryCreate(manifest.ArtifactUrl, UriKind.Absolute, out var resolvedUri) ||
            !resolvedUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsAllowedArtifactHost(resolvedUri))
        {
            return false;
        }
        artifactUri = resolvedUri;
        return true;
    }

    private async Task<HttpResponseMessage?> GetArtifactResponseAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; redirectCount <= 3; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is < 300 or > 399)
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                return null;
            }
            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (!currentUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !IsAllowedArtifactHost(currentUri))
            {
                return null;
            }
        }
        return null;
    }

    private bool IsAllowedArtifactHost(Uri uri) =>
        options.AllowedArtifactHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> ExtractLinuxBundleAsync(
        string artifactPath,
        string releaseDirectory,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(releaseDirectory, "bundle");
        var temporaryDirectory = Path.Combine(releaseDirectory, $".bundle-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            await using var source = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var reader = new TarReader(gzip, leaveOpen: false);
            var foundEntries = new HashSet<string>(StringComparer.Ordinal);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = entry.Name.Replace('\\', '/');
                if (entry.EntryType is TarEntryType.Directory)
                {
                    continue;
                }
                if (entry.EntryType is not TarEntryType.RegularFile ||
                    !LinuxBundleEntries.Contains(entryName) ||
                    entry.DataStream is null ||
                    !foundEntries.Add(entryName))
                {
                    return null;
                }

                var targetPath = Path.GetFullPath(Path.Combine(temporaryDirectory, entryName));
                if (!targetPath.StartsWith(temporaryDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    return null;
                }
                await using var destination = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await entry.DataStream.CopyToAsync(destination, cancellationToken);
            }

            if (!foundEntries.Contains("compose.yaml"))
            {
                return null;
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            Directory.Move(temporaryDirectory, outputDirectory);
            return Path.Combine(outputDirectory, "compose.yaml");
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}

public sealed record HostVerifiedReleaseArtifact(
    string ArtifactPath,
    string? ComposeFilePath,
    string ArtifactSha256);

public sealed record HostArtifactVerificationResult(
    bool Succeeded,
    HostVerifiedReleaseArtifact? Artifact,
    string? FailureKind)
{
    public static HostArtifactVerificationResult Success(HostVerifiedReleaseArtifact artifact) => new(true, artifact, null);
    public static HostArtifactVerificationResult Failed(string failureKind) => new(false, null, failureKind);
}
