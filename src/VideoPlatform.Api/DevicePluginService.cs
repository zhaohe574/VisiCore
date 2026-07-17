using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class DevicePluginService(
    PlatformDbContext dbContext,
    IOptions<DevicePluginTrustOptions>? trustOptions = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex IdentifierPattern = new("^[a-z0-9][a-z0-9.-]{1,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant);
    private const string PlatformVersion = "1.0.0";
    private readonly IReadOnlyDictionary<string, string> _trustedPublicKeys = trustOptions?.Value.PublicKeys ?? new Dictionary<string, string>();

    public static readonly Guid StandardOnvifPluginId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid DirectRtspPluginId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public static IReadOnlyList<(Guid Id, DevicePluginManifest Manifest)> BuiltInPlugins { get; } =
    [
        (StandardOnvifPluginId, new DevicePluginManifest(
            "onvif-standard",
            "标准 ONVIF",
            "1.0.0",
            "onvif",
            DevicePluginRuntimeTypes.Onvif,
            "onvif-standard",
            [DeviceKinds.Camera, DeviceKinds.Recorder, DeviceKinds.Matrix, DeviceKinds.Encoder, DeviceKinds.Gateway],
            [
                new("Onvif", "ONVIF 管理端点", 80, SupportsTls: true),
                new("Rtsp", "RTSP 码流端点", 554)
            ],
            new(true, true, true, true, false, true),
            Description: "面向任意品牌的标准 ONVIF Profile S / G 接入。")),
        (DirectRtspPluginId, new DevicePluginManifest(
            "direct-rtsp",
            "通用 RTSP 直连",
            "1.0.0",
            "rtsp",
            DevicePluginRuntimeTypes.DirectRtsp,
            "direct-rtsp",
            [DeviceKinds.Camera, DeviceKinds.Recorder, DeviceKinds.Matrix, DeviceKinds.Encoder, DeviceKinds.Gateway, DeviceKinds.Other],
            [new("Rtsp", "RTSP 码流端点", 554)],
            new(true, false, false, false, false, false),
            Description: "通过管理员提供的主、子码流地址接入任意品牌 RTSP 设备。"))
    ];

    public async Task EnsureBuiltInPluginsAsync(CancellationToken cancellationToken)
    {
        var existing = await dbContext.DevicePlugins.ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, manifest) in BuiltInPlugins)
        {
            var entity = existing.SingleOrDefault(item => item.Id == id || item.Key == manifest.Key);
            if (entity is null)
            {
                entity = new DevicePluginEntity
                {
                    Id = id,
                    Enabled = true,
                    IsBuiltIn = true,
                    InstalledAt = now
                };
                dbContext.DevicePlugins.Add(entity);
            }
            ApplyManifest(entity, manifest, now);
            entity.IsBuiltIn = true;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DevicePluginEntity> InstallAsync(DevicePluginManifest manifest, CancellationToken cancellationToken)
    {
        var normalized = NormalizeAndValidate(manifest);
        ValidateExternalPackage(normalized);
        var existing = await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Key == normalized.Key, cancellationToken);
        if (existing?.IsBuiltIn == true)
        {
            throw new InvalidOperationException("内置协议插件不能通过安装接口覆盖。");
        }
        if (await dbContext.DevicePlugins.AnyAsync(
                item => item.AdapterType == normalized.AdapterType && (existing == null || item.Id != existing.Id),
                cancellationToken))
        {
            throw new InvalidOperationException("适配器标识已被其他协议插件占用。");
        }

        if (existing is not null)
        {
            var devices = await dbContext.Recorders.AsNoTracking()
                .Where(item => item.DevicePluginId == existing.Id)
                .ToListAsync(cancellationToken);
            if (devices.Count > 0)
            {
                var previous = ParseManifest(existing);
                if (!previous.RuntimeType.Equals(normalized.RuntimeType, StringComparison.OrdinalIgnoreCase) ||
                    !previous.AdapterType.Equals(normalized.AdapterType, StringComparison.OrdinalIgnoreCase) ||
                    devices.Any(item => !normalized.SupportedDeviceKinds.Contains(item.DeviceKind, StringComparer.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("插件已被设备使用，升级不能改变运行时、适配器标识或移除现有设备类型。 ");
                }
                var deviceIds = devices.Select(item => item.Id).ToList();
                var endpoints = await dbContext.RecorderEndpoints.AsNoTracking()
                    .Where(item => deviceIds.Contains(item.RecorderId))
                    .ToListAsync(cancellationToken);
                foreach (var device in devices)
                {
                    var registrations = endpoints.Where(item => item.RecorderId == device.Id)
                        .Select(item => new RecorderEndpointRegistration(
                            item.Protocol, item.Host, item.Port, item.CredentialReference, item.UseTls, item.CertificateThumbprint))
                        .ToList();
                    if (!RecorderRegistrationValidator.TryValidateForPlugin(normalized, registrations, out _))
                    {
                        throw new InvalidOperationException("插件升级后的端点声明与现有设备配置不兼容。 ");
                    }
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new DevicePluginEntity
            {
                Id = Guid.NewGuid(),
                Enabled = true,
                InstalledAt = now
            };
            dbContext.DevicePlugins.Add(existing);
        }
        ApplyManifest(existing, normalized, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<DevicePluginEntity> SetEnabledAsync(Guid pluginId, bool enabled, CancellationToken cancellationToken)
    {
        var plugin = await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Id == pluginId, cancellationToken)
            ?? throw new KeyNotFoundException("协议插件不存在。");
        if (!enabled && await dbContext.Recorders.AnyAsync(item => item.DevicePluginId == pluginId, cancellationToken))
        {
            throw new InvalidOperationException("协议插件仍被接入设备使用，不能停用。");
        }
        plugin.Enabled = enabled;
        plugin.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return plugin;
    }

    public async Task RemoveAsync(Guid pluginId, CancellationToken cancellationToken)
    {
        var plugin = await dbContext.DevicePlugins.SingleOrDefaultAsync(item => item.Id == pluginId, cancellationToken)
            ?? throw new KeyNotFoundException("协议插件不存在。");
        if (plugin.IsBuiltIn)
        {
            throw new InvalidOperationException("内置协议插件不能卸载。");
        }
        if (await dbContext.Recorders.AnyAsync(item => item.DevicePluginId == pluginId, cancellationToken))
        {
            throw new InvalidOperationException("协议插件仍被接入设备使用，不能卸载。");
        }
        dbContext.DevicePlugins.Remove(plugin);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static DevicePluginManifest ParseManifest(DevicePluginEntity entity) =>
        JsonSerializer.Deserialize<DevicePluginManifest>(entity.ManifestJson, JsonOptions)
        ?? throw new InvalidOperationException($"协议插件 {entity.Key} 的 manifest 无法读取。");

    public static DevicePluginManifest NormalizeAndValidate(DevicePluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Capabilities is null)
        {
            throw new ArgumentException("插件必须声明完整的能力集合。", nameof(manifest));
        }
        var key = manifest.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        var adapterType = manifest.AdapterType?.Trim().ToLowerInvariant() ?? string.Empty;
        var protocolType = manifest.ProtocolType?.Trim().ToLowerInvariant() ?? string.Empty;
        var runtimeType = manifest.RuntimeType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IdentifierPattern.IsMatch(key) || !IdentifierPattern.IsMatch(adapterType) || !IdentifierPattern.IsMatch(protocolType))
        {
            throw new ArgumentException("插件标识、适配器标识和协议类型必须使用小写字母、数字、点或连字符。", nameof(manifest));
        }
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Trim().Length > 32)
        {
            throw new ArgumentException("插件名称或版本无效。", nameof(manifest));
        }
        if (!DevicePluginRuntimeTypes.Known.Contains(runtimeType))
        {
            throw new ArgumentException("插件运行时必须是 onvif、direct-rtsp 或 external-edge。", nameof(manifest));
        }

        if ((manifest.SupportedDeviceKinds ?? []).Any(item => item is null) ||
            (manifest.Endpoints ?? []).Any(item => item is null) ||
            (manifest.Models ?? []).Any(item => item is null))
        {
            throw new ArgumentException("插件的设备类型、端点或型号列表不能包含空项。", nameof(manifest));
        }

        var kinds = (manifest.SupportedDeviceKinds ?? [])
            .Select(item => item.Trim().ToLowerInvariant())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (kinds.Count == 0 || kinds.Any(item => !DeviceKinds.Known.Contains(item)))
        {
            throw new ArgumentException("插件必须声明至少一种有效设备类型。", nameof(manifest));
        }
        var endpoints = (manifest.Endpoints ?? [])
            .Select(item => item with
            {
                Protocol = NormalizeProtocol(item.Protocol),
                Label = item.Label?.Trim() ?? string.Empty
            })
            .ToList();
        if (endpoints.Count == 0 || endpoints.Any(item => string.IsNullOrWhiteSpace(item.Label) || item.DefaultPort is < 1 or > 65535) ||
            endpoints.Select(item => item.Protocol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != endpoints.Count)
        {
            throw new ArgumentException("插件端点定义无效或存在重复协议。", nameof(manifest));
        }

        var requiredProtocols = runtimeType switch
        {
            DevicePluginRuntimeTypes.Onvif => new[] { "Onvif", "Rtsp" },
            DevicePluginRuntimeTypes.DirectRtsp => new[] { "Rtsp" },
            DevicePluginRuntimeTypes.ExternalEdge => endpoints.Where(item => item.Required).Select(item => item.Protocol).ToArray(),
            _ => []
        };
        if (requiredProtocols.Any(required => !endpoints.Any(item => item.Required && item.Protocol == required)))
        {
            throw new ArgumentException("插件缺少运行时要求的必填协议端点。", nameof(manifest));
        }
        if (runtimeType == DevicePluginRuntimeTypes.DirectRtsp &&
            (manifest.Capabilities.ChannelDiscovery || manifest.Capabilities.Playback || manifest.Capabilities.Ptz ||
             manifest.Capabilities.Export || manifest.Capabilities.ClockSynchronization))
        {
            throw new ArgumentException("direct-rtsp 运行时只能声明实时预览能力。", nameof(manifest));
        }

        if (!Version.TryParse(manifest.MinimumPlatformVersion, out var minimumPlatformVersion) ||
            minimumPlatformVersion > Version.Parse(PlatformVersion))
        {
            throw new ArgumentException($"插件要求的平台版本 {manifest.MinimumPlatformVersion} 不受当前版本 {PlatformVersion} 支持。", nameof(manifest));
        }

        return manifest with
        {
            Key = key,
            Name = manifest.Name.Trim(),
            Version = manifest.Version.Trim(),
            ProtocolType = protocolType,
            RuntimeType = runtimeType,
            AdapterType = adapterType,
            SupportedDeviceKinds = kinds,
            Endpoints = endpoints,
            Vendor = NullIfWhiteSpace(manifest.Vendor),
            Models = manifest.Models?.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Description = NullIfWhiteSpace(manifest.Description),
            MinimumPlatformVersion = minimumPlatformVersion.ToString(3),
            ConfigurationSchema = NullIfWhiteSpace(manifest.ConfigurationSchema)
        };
    }

    private static string NormalizeProtocol(string? value)
    {
        if (!Enum.TryParse<RecorderEndpointProtocol>(value?.Trim(), true, out var protocol) ||
            !Enum.IsDefined(protocol))
        {
            throw new ArgumentException($"不支持的端点协议：{value}。", nameof(value));
        }
        return protocol.ToString();
    }

    private void ValidateExternalPackage(DevicePluginManifest manifest)
    {
        if (manifest.Package is null)
        {
            throw new ArgumentException("外部设备插件必须提供已签名的软件包描述。", nameof(manifest));
        }
        if (manifest.RuntimeType != DevicePluginRuntimeTypes.ExternalEdge)
        {
            throw new ArgumentException("外部设备插件必须使用 external-edge 运行时。", nameof(manifest));
        }

        var package = manifest.Package;
        if (string.IsNullOrWhiteSpace(package.ImageReference) ||
            !package.ImageDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !Sha256Pattern.IsMatch(package.ImageDigest[7..]) ||
            !Sha256Pattern.IsMatch(package.PackageSha256) ||
            string.IsNullOrWhiteSpace(package.SigningKeyId) ||
            string.IsNullOrWhiteSpace(package.Signature))
        {
            throw new ArgumentException("外部设备插件的软件包引用、摘要或签名无效。", nameof(manifest));
        }
        if (!_trustedPublicKeys.TryGetValue(package.SigningKeyId, out var publicKeyPem) || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new UnauthorizedAccessException("设备插件签名密钥未受当前部署信任。");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(package.Signature);
        }
        catch (FormatException)
        {
            throw new ArgumentException("设备插件签名不是有效的 Base64 数据。", nameof(manifest));
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            throw new InvalidOperationException("受信任设备插件公钥格式无效。", exception);
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            manifest.Key,
            manifest.Name,
            manifest.Version,
            manifest.MinimumPlatformVersion,
            manifest.ProtocolType,
            manifest.RuntimeType,
            manifest.AdapterType,
            manifest.SupportedDeviceKinds,
            manifest.Endpoints,
            manifest.Capabilities,
            manifest.Vendor,
            manifest.Models,
            manifest.Description,
            manifest.ConfigurationSchema,
            package.ImageReference,
            package.ImageDigest,
            package.PackageSha256,
            package.SigningKeyId
        }, JsonOptions);
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new UnauthorizedAccessException("设备插件签名校验失败。");
        }
    }

    private static void ApplyManifest(DevicePluginEntity entity, DevicePluginManifest source, DateTimeOffset now)
    {
        var manifest = NormalizeAndValidate(source);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        entity.Key = manifest.Key;
        entity.Name = manifest.Name;
        entity.Version = manifest.Version;
        entity.ProtocolType = manifest.ProtocolType;
        entity.RuntimeType = manifest.RuntimeType;
        entity.AdapterType = manifest.AdapterType;
        entity.Vendor = manifest.Vendor;
        entity.Description = manifest.Description;
        entity.ManifestJson = json;
        entity.PackageHash = manifest.Package?.PackageSha256.ToUpperInvariant() ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        entity.UpdatedAt = now;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
