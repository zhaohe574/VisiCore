using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VisiCore.Persistence;

namespace VisiCore.Setup;

/// <summary>
/// 负责首次安装。该服务只接受调用方在内存中提供的敏感信息，绝不记录密码或连接字符串。
/// </summary>
public sealed class InstallationService(string configurationPath)
{
    private const string DefaultDatabaseName = "visicore";
    private const string DefaultPlatformAdministratorUsername = "admin";

    public InstallationStatus GetStatus()
    {
        if (File.Exists(configurationPath))
        {
            return new InstallationStatus(InstallationState.Completed, CreateDefaults());
        }

        return File.Exists(GetLockPath())
            ? new InstallationStatus(InstallationState.Initializing, CreateDefaults())
            : new InstallationStatus(InstallationState.Unconfigured, CreateDefaults());
    }

    public async Task InitializeAsync(InstallationRequest request, Uri browserOrigin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browserOrigin);
        EnsureConfigurationPath();
        if (File.Exists(configurationPath))
        {
            throw new InstallationConflictException("安装已完成，拒绝覆盖现有运行配置。");
        }

        await using var installationLock = AcquireLock();
        try
        {
            if (File.Exists(configurationPath))
            {
                throw new InstallationConflictException("安装已完成，拒绝覆盖现有运行配置。");
            }

            var input = ValidateRequest(request, browserOrigin);
            await ValidatePostgreSqlAsync(input.Database, cancellationToken);
            await ValidateMediaMtxAsync(input.Media, cancellationToken);

            var administratorConnection = CreateConnectionString(
                input.Database.Host,
                input.Database.Port,
                "postgres",
                input.Database.AdministratorUsername,
                input.Database.AdministratorPassword,
                input.Database.TlsMode);
            var platformConnection = CreateConnectionString(
                input.Database.Host,
                input.Database.Port,
                input.Database.Name,
                input.Database.AdministratorUsername,
                input.Database.AdministratorPassword,
                input.Database.TlsMode);

            var databaseCreated = false;
            try
            {
                await using (var connection = new NpgsqlConnection(administratorConnection))
                {
                    await connection.OpenAsync(cancellationToken);
                    await ExecuteAsync(connection, $"CREATE DATABASE {QuoteIdentifier(input.Database.Name)};", cancellationToken);
                    databaseCreated = true;
                }

                var services = new ServiceCollection();
                services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(platformConnection));
                services.AddSingleton<Argon2PasswordHasher>();
                await using var provider = services.BuildServiceProvider();
                await using var scope = provider.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
                await dbContext.Database.MigrateAsync(cancellationToken);
                if (await dbContext.Users.AnyAsync(cancellationToken))
                {
                    throw new InstallationConflictException("新建数据库在迁移后已包含账号，初始化已拒绝。");
                }

                var hasher = scope.ServiceProvider.GetRequiredService<Argon2PasswordHasher>();
                dbContext.Users.Add(new UserEntity
                {
                    Id = Guid.NewGuid(),
                    Username = input.PlatformAdministratorUsername,
                    PasswordHash = hasher.Hash(input.PlatformAdministratorPassword),
                    IsSystemAdministrator = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);

                await WriteConfigurationAsync(CreateRuntimeConfiguration(platformConnection, input.PlatformBaseUri, input.Media), cancellationToken);
            }
            catch
            {
                // 即使浏览器已断开，也必须完成本次创建资源的回滚，不能复用已取消的请求令牌。
                await RollbackAsync(administratorConnection, input.Database.Name, databaseCreated, CancellationToken.None);
                throw;
            }
            finally
            {
                ClearSecret(input.Database.AdministratorPassword);
                ClearSecret(input.PlatformAdministratorPassword);
            }
        }
        finally
        {
            try
            {
                File.Delete(GetLockPath());
            }
            catch
            {
                // 锁文件仅用于拒绝并发安装；下次启动会通过配置文件判定完成状态。
            }
        }
    }

    private static InstallationDefaults CreateDefaults() => new(
        5432,
        "disable",
        DefaultDatabaseName,
        "same-host",
        "http://mediamtx:9997/",
        "http://mediamtx:8888/",
        DefaultPlatformAdministratorUsername);

    public async Task TestPostgreSqlAsync(PostgreSqlTestRequest request, CancellationToken cancellationToken)
    {
        EnsureUnconfigured();
        await ValidatePostgreSqlAsync(ValidateDatabase(request), cancellationToken);
    }

    public async Task TestMediaMtxAsync(MediaMtxTestRequest request, CancellationToken cancellationToken)
    {
        EnsureUnconfigured();
        await ValidateMediaMtxAsync(ValidateMedia(request.MediaMode, request.MediaApiBaseUri, request.MediaHlsBaseUri), cancellationToken);
    }

    private ValidatedInstallationRequest ValidateRequest(InstallationRequest request, Uri browserOrigin)
    {
        var database = ValidateDatabase(new PostgreSqlTestRequest(
            request.DatabaseHost,
            request.DatabasePort,
            request.PostgresTlsMode,
            request.DatabaseAdministratorUsername,
            request.DatabaseAdministratorPassword,
            request.DatabaseName));
        if (!PlatformUsernamePolicy.TryNormalize(request.PlatformAdministratorUsername, out var platformAdministratorUsername, out var validationError))
        {
            throw new InstallationValidationException($"系统管理员{validationError}");
        }
        var platformAdministratorPassword = RequireSecret(request.PlatformAdministratorPassword, "系统管理员密码", 12);
        var platformBaseUri = ValidatePlatformBaseUri(request.PublicBaseUri, request.AllowInsecureLanHttp, browserOrigin);
        var media = ValidateMedia(request.MediaMode, request.MediaApiBaseUri, request.MediaHlsBaseUri);

        return new ValidatedInstallationRequest(
            database,
            platformBaseUri,
            media,
            platformAdministratorUsername,
            platformAdministratorPassword);
    }

    private static ValidatedPostgreSqlSettings ValidateDatabase(PostgreSqlTestRequest request)
    {
        var host = RequireText(request.DatabaseHost, "PostgreSQL 主机", 255);
        var port = request.DatabasePort is > 0 and <= 65535
            ? request.DatabasePort
            : throw new InstallationValidationException("PostgreSQL 端口必须在 1 至 65535 之间。");
        var tlsMode = request.PostgresTlsMode?.Trim().ToLowerInvariant() switch
        {
            "disable" => SslMode.Disable,
            "require" => SslMode.Require,
            "verify-full" => SslMode.VerifyFull,
            _ => throw new InstallationValidationException("PostgreSQL TLS 模式仅支持 disable、require 或 verify-full。")
        };
        return new ValidatedPostgreSqlSettings(
            host,
            port,
            tlsMode,
            RequireIdentifier(request.DatabaseAdministratorUsername, "PostgreSQL 管理账号"),
            RequireSecret(request.DatabaseAdministratorPassword, "PostgreSQL 管理密码", 1),
            RequireIdentifier(request.DatabaseName, "数据库名"));
    }

    private static MediaConfiguration ValidateMedia(string? mediaMode, string? mediaApiBaseUri, string? mediaHlsBaseUri)
    {
        var mode = mediaMode?.Trim().ToLowerInvariant();
        if (mode is not ("same-host" or "remote"))
        {
            throw new InstallationValidationException("MediaMTX 模式仅支持 same-host 或 remote。");
        }

        var requireHttps = mode == "remote";
        var apiBaseUri = ValidateMediaUri(mediaApiBaseUri, "MediaMTX Control API 地址", requireHttps);
        var hlsBaseUri = ValidateMediaUri(mediaHlsBaseUri, "MediaMTX HLS 地址", requireHttps);
        var trustedHosts = mode == "same-host" ? new[] { apiBaseUri.Host } : [];
        return new MediaConfiguration(apiBaseUri.ToString(), hlsBaseUri.ToString(), trustedHosts);
    }

    private static Uri ValidatePlatformBaseUri(string? value, bool allowInsecureLanHttp, Uri browserOrigin)
    {
        if (!TryNormalizeAbsoluteUri(value, out var uri) || uri.AbsolutePath != "/")
        {
            throw new InstallationValidationException("公共访问地址必须是未包含路径、查询或凭据的绝对地址。");
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || !allowInsecureLanHttp)
        {
            throw new InstallationValidationException("公共访问地址必须使用 HTTPS；局域网 HTTP 需明确确认明文传输风险。");
        }

        if (!browserOrigin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.GetLeftPart(UriPartial.Authority), browserOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallationValidationException("局域网 HTTP 公共地址必须与当前浏览器访问来源完全一致。");
        }

        return uri;
    }

    private static Uri ValidateMediaUri(string? value, string label, bool requireHttps)
    {
        if (!TryNormalizeAbsoluteUri(value, out var uri) ||
            (requireHttps && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            (!requireHttps && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InstallationValidationException(requireHttps
                ? $"{label}必须使用合法 HTTPS 地址。"
                : $"{label}必须使用合法 HTTP 或 HTTPS 地址。 ");
        }

        return uri;
    }

    private static bool TryNormalizeAbsoluteUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        uri = new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/", UriKind.Absolute);
        return true;
    }

    private static string RequireText(string? value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw new InstallationValidationException($"{label}不能为空或长度超限。");
        }

        return value.Trim();
    }

    private static string RequireIdentifier(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value.Trim(), "^[A-Za-z_][A-Za-z0-9_]{0,62}$"))
        {
            throw new InstallationValidationException($"{label}必须以字母或下划线开头，且仅可包含字母、数字和下划线，长度不超过 63 位。");
        }

        return value.Trim();
    }

    private static string RequireSecret(string? value, string label, int minimumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length < minimumLength || value.Length > 256)
        {
            throw new InstallationValidationException($"{label}长度无效。");
        }

        return value;
    }

    private static async Task ValidateMediaMtxAsync(MediaConfiguration media, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        await ProbeHttpEndpointAsync(client, new Uri(new Uri(media.ApiBaseUri), "v3/config/global/get"), timeout.Token);
        await ProbeHttpEndpointAsync(client, new Uri(media.HlsBaseUri), timeout.Token);
    }

    private static async Task ValidatePostgreSqlAsync(ValidatedPostgreSqlSettings settings, CancellationToken cancellationToken)
    {
        var connectionString = CreateConnectionString(
            settings.Host,
            settings.Port,
            "postgres",
            settings.AdministratorUsername,
            settings.AdministratorPassword,
            settings.TlsMode);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (await ExistsAsync(connection, "SELECT 1 FROM pg_database WHERE datname = @name", settings.Name, cancellationToken))
        {
            throw new InstallationConflictException("目标数据库已存在。视枢仅支持全新安装，未修改任何数据。");
        }

        await using var permissionCommand = new NpgsqlCommand(
            "SELECT rolsuper OR rolcreatedb FROM pg_roles WHERE rolname = current_user;",
            connection);
        if (await permissionCommand.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new InstallationValidationException("PostgreSQL 管理账号不具备创建数据库权限。");
        }
    }

    private static async Task ProbeHttpEndpointAsync(HttpClient client, Uri address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is < 100 or > 599)
        {
            throw new InstallationValidationException("MediaMTX 返回了无效 HTTP 状态。");
        }
    }

    private static string CreateConnectionString(string host, int port, string database, string username, string password, SslMode sslMode) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = sslMode,
            Pooling = true
        }.ConnectionString;

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string sql, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RollbackAsync(
        string administratorConnection,
        string databaseName,
        bool databaseCreated,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(administratorConnection);
            await connection.OpenAsync(cancellationToken);
            if (databaseCreated)
            {
                await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);", cancellationToken);
            }
        }
        catch (Exception exception)
        {
            throw new InstallationRollbackException("初始化失败且自动回滚未完成，请由 PostgreSQL 管理员检查本次创建的数据库。", exception);
        }
    }

    private async Task WriteConfigurationAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(configurationPath) ?? throw new InstallationValidationException("运行配置目录无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(configurationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporaryPath, configurationPath, overwrite: false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(configurationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private RuntimeConfiguration CreateRuntimeConfiguration(string applicationConnection, Uri platformBaseUri, MediaConfiguration media)
    {
        var publicStreamUri = new Uri(platformBaseUri, "stream/");
        var controlToken = CreateSecret();
        var commandToken = CreateSecret();
        var publisherPassword = CreateSecret();
        var hlsReaderPassword = CreateSecret();
        var notificationWebhookMasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var usesInsecureHttp = platformBaseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        var trustedOrigins = usesInsecureHttp ? new[] { platformBaseUri.GetLeftPart(UriPartial.Authority) } : [];
        var trustedHosts = usesInsecureHttp ? new[] { platformBaseUri.Host } : [];
        return new RuntimeConfiguration
        {
            ConnectionStrings = new RuntimeConnectionStrings { Platform = applicationConnection },
            ExportArtifactStorage = new RuntimeExportArtifactStorage { RootDirectory = "/var/lib/visicore/exports" },
            StreamGateway = new RuntimeStreamGateway
            {
                GatewayName = "core",
                PublicBaseUri = publicStreamUri.ToString(),
                ControlToken = controlToken,
                CommandBaseUri = "http://127.0.0.1:15095/",
                CommandToken = commandToken,
                AllowInsecureLoopbackHttpForDevelopment = usesInsecureHttp,
                AllowInsecureLoopbackHttpForInternalRuntime = true,
                TrustedDevelopmentHttpHosts = trustedHosts,
                TrustedInsecureHttpOrigins = trustedOrigins
            },
            Gateway = new RuntimeGateway
            {
                GatewayName = "core",
                CenterBaseUri = "http://127.0.0.1:8081/",
                CenterControlToken = controlToken,
                CommandToken = commandToken,
                AllowInsecureLoopbackCenterHttpForInternalRuntime = true,
                AllowInsecureClientHttpForDevelopment = usesInsecureHttp,
                TrustedDevelopmentHttpHosts = trustedHosts,
                TrustedInsecureHttpOrigins = trustedOrigins,
                TrustedForwardedProxyAddresses = ["127.0.0.1", "::1"],
                EnableDeviceAssignments = false
            },
            NotificationWebhook = new RuntimeNotificationWebhook { MasterKey = notificationWebhookMasterKey },
            MediaMtx = new RuntimeMediaMtx
            {
                ApiBaseUri = media.ApiBaseUri,
                HlsBaseUri = media.HlsBaseUri,
                PublisherUsername = "relay",
                PublisherPassword = publisherPassword,
                HlsReaderUsername = "hlsproxy",
                HlsReaderPassword = hlsReaderPassword,
                TrustedInternalHosts = media.TrustedInternalHosts
            }
        };
    }

    private FileStream AcquireLock()
    {
        var directory = Path.GetDirectoryName(configurationPath) ?? throw new InstallationValidationException("运行配置目录无效。");
        Directory.CreateDirectory(directory);
        try
        {
            var handle = new FileStream(GetLockPath(), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(GetLockPath(), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return handle;
        }
        catch (IOException)
        {
            throw new InstallationConflictException("已有初始化正在执行，请等待其完成后刷新页面。");
        }
    }

    private void EnsureConfigurationPath()
    {
        if (!Path.IsPathFullyQualified(configurationPath))
        {
            throw new InstallationValidationException("运行配置路径必须是绝对路径。");
        }
    }

    private void EnsureUnconfigured()
    {
        EnsureConfigurationPath();
        if (File.Exists(configurationPath))
        {
            throw new InstallationConflictException("安装已完成，拒绝再次测试初始化参数。");
        }
        if (File.Exists(GetLockPath()))
        {
            throw new InstallationConflictException("已有初始化正在执行，请等待其完成后刷新页面。");
        }
    }

    private string GetLockPath() => configurationPath + ".lock";
    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string CreateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void ClearSecret(string value) => CryptographicOperations.ZeroMemory(System.Text.Encoding.UTF8.GetBytes(value));
}

public sealed record InstallationRequest(
    string? DatabaseHost,
    int DatabasePort,
    string? PostgresTlsMode,
    string? DatabaseAdministratorUsername,
    string? DatabaseAdministratorPassword,
    string? DatabaseName,
    string? PublicBaseUri,
    string? MediaMode,
    string? MediaApiBaseUri,
    string? MediaHlsBaseUri,
    string? PlatformAdministratorUsername,
    string? PlatformAdministratorPassword,
    bool AllowInsecureLanHttp);

public sealed record PostgreSqlTestRequest(
    string? DatabaseHost,
    int DatabasePort,
    string? PostgresTlsMode,
    string? DatabaseAdministratorUsername,
    string? DatabaseAdministratorPassword,
    string? DatabaseName);

public sealed record MediaMtxTestRequest(
    string? MediaMode,
    string? MediaApiBaseUri,
    string? MediaHlsBaseUri);

public sealed record InstallationDefaults(
    int DatabasePort,
    string PostgresTlsMode,
    string DatabaseName,
    string MediaMode,
    string MediaApiBaseUri,
    string MediaHlsBaseUri,
    string PlatformAdministratorUsername);

public sealed record InstallationStatus(InstallationState State, InstallationDefaults Defaults);
public enum InstallationState { Unconfigured, Initializing, Completed }

public sealed class InstallationValidationException(string message) : Exception(message);
public sealed class InstallationConflictException(string message) : Exception(message);
public sealed class InstallationRollbackException(string message, Exception innerException) : Exception(message, innerException);

internal sealed record ValidatedInstallationRequest(
    ValidatedPostgreSqlSettings Database,
    Uri PlatformBaseUri,
    MediaConfiguration Media,
    string PlatformAdministratorUsername,
    string PlatformAdministratorPassword);

internal sealed record ValidatedPostgreSqlSettings(
    string Host,
    int Port,
    SslMode TlsMode,
    string AdministratorUsername,
    string AdministratorPassword,
    string Name);

public sealed record MediaConfiguration(string ApiBaseUri, string HlsBaseUri, string[] TrustedInternalHosts);

public sealed class RuntimeConfiguration
{
    public RuntimeConnectionStrings ConnectionStrings { get; init; } = new();
    public RuntimeExportArtifactStorage ExportArtifactStorage { get; init; } = new();
    public RuntimeStreamGateway StreamGateway { get; init; } = new();
    public RuntimeGateway Gateway { get; init; } = new();
    public RuntimeNotificationWebhook NotificationWebhook { get; init; } = new();
    public RuntimeMediaMtx MediaMtx { get; init; } = new();
}

public sealed class RuntimeConnectionStrings { public string Platform { get; init; } = string.Empty; }
public sealed class RuntimeExportArtifactStorage { public string RootDirectory { get; init; } = string.Empty; }
public sealed class RuntimeStreamGateway
{
    public string GatewayName { get; init; } = string.Empty;
    public string PublicBaseUri { get; init; } = string.Empty;
    public string ControlToken { get; init; } = string.Empty;
    public string CommandBaseUri { get; init; } = string.Empty;
    public string CommandToken { get; init; } = string.Empty;
    public bool AllowInsecureLoopbackHttpForDevelopment { get; init; }
    public bool AllowInsecureLoopbackHttpForInternalRuntime { get; init; }
    public string[] TrustedDevelopmentHttpHosts { get; init; } = [];
    public string[] TrustedInsecureHttpOrigins { get; init; } = [];
}
public sealed class RuntimeGateway
{
    public string GatewayName { get; init; } = string.Empty;
    public string CenterBaseUri { get; init; } = string.Empty;
    public string CenterControlToken { get; init; } = string.Empty;
    public string CommandToken { get; init; } = string.Empty;
    public bool AllowInsecureLoopbackCenterHttpForInternalRuntime { get; init; }
    public bool AllowInsecureClientHttpForDevelopment { get; init; }
    public string[] TrustedDevelopmentHttpHosts { get; init; } = [];
    public string[] TrustedInsecureHttpOrigins { get; init; } = [];
    public string[] TrustedForwardedProxyAddresses { get; init; } = [];
    public bool EnableDeviceAssignments { get; init; }
}
public sealed class RuntimeNotificationWebhook { public string MasterKey { get; init; } = string.Empty; }
public sealed class RuntimeMediaMtx
{
    public string ApiBaseUri { get; init; } = string.Empty;
    public string HlsBaseUri { get; init; } = string.Empty;
    public string PublisherUsername { get; init; } = string.Empty;
    public string PublisherPassword { get; init; } = string.Empty;
    public string HlsReaderUsername { get; init; } = string.Empty;
    public string HlsReaderPassword { get; init; } = string.Empty;
    public string[] TrustedInternalHosts { get; init; } = [];
}
