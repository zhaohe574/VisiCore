using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed class SslService(Database db, IAccessService access, PlatformOptions options, AuditStore audit)
{
    public async Task<SslOverviewDto> OverviewAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.read", ct);
        var certRows = await db.QueryAsync("select * from ssl_certificates order by created_at desc", ct: ct);
        var certs = certRows.Select(ToCertDto).ToArray();
        var activeCert = certs.FirstOrDefault(c => c.IsActive);
        var expiringCount = certs.Count(c => c.Status is "expiring_soon" or "expired");

        var domainRows = await db.QueryAsync("select d.*, c.name as cert_name, c.common_name as cert_common_name, c.valid_to as cert_valid_to, c.dns_names as cert_dns_names from ssl_domains d left join ssl_certificates c on c.id=d.certificate_id order by d.is_primary desc, d.created_at", ct: ct);
        var domains = domainRows.Select(ToDomainDto).ToArray();
        var primaryDomain = domains.FirstOrDefault(d => d.IsPrimary) ?? domains.FirstOrDefault();

        var certFileExists = File.Exists(options.SslCertFile);
        var keyFileExists = File.Exists(options.SslKeyFile);
        var fileSynced = false;
        if (activeCert != null && certFileExists && keyFileExists)
        {
            try
            {
                var diskCertText = await File.ReadAllTextAsync(options.SslCertFile, ct);
                using var diskCert = X509Certificate2.CreateFromPem(diskCertText);
                var diskThumb = diskCert.GetCertHashString(HashAlgorithmName.SHA256);
                fileSynced = string.Equals(diskThumb, activeCert.Thumbprint, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                fileSynced = false;
            }
        }

        return new SslOverviewDto(
            ActiveCertificate: activeCert,
            TotalCertificates: certs.Length,
            ExpiringCertificates: expiringCount,
            TotalDomains: domains.Length,
            PrimaryDomain: primaryDomain,
            HttpsEnforced: domains.All(d => d.ForceHttps),
            CertFilePath: options.SslCertFile,
            KeyFilePath: options.SslKeyFile,
            FileSynced: fileSynced
        );
    }

    public async Task<IReadOnlyList<SslCertificateDto>> CertificatesAsync(Actor actor, string? search, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.read", ct);
        var filter = string.IsNullOrWhiteSpace(search)
            ? "1=1"
            : "(c.name ilike @search or c.common_name ilike @search or exists (select 1 from unnest(c.dns_names) d where d ilike @search))";
        var rows = await db.QueryAsync($@"
            select c.*, (select count(*) from ssl_domains d where d.certificate_id = c.id) as bound_domains
            from ssl_certificates c
            where {filter}
            order by c.is_active desc, c.created_at desc
        ", new { search = $"%{search?.Trim()}%" }, ct);

        return rows.Select(ToCertDto).ToArray();
    }

    public async Task<SslCertificateDto> CertificateByIdAsync(Actor actor, long id, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.read", ct);
        var row = await db.OneAsync(@"
            select c.*, (select count(*) from ssl_domains d where d.certificate_id = c.id) as bound_domains
            from ssl_certificates c
            where c.id = @id
        ", new { id }, ct);
        Rules.Require(row != null, "指定证书不存在", "ssl.cert.not_found", 404);
        return ToCertDto(row!, includeCertPem: true);
    }

    public async Task<SslCertificateDto> UploadCertificateAsync(Actor actor, SslCertificateUploadRequest request, string? ip, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.manage", ct);
        var name = Rules.Text(request.Name, "证书名称", 128);
        var certPem = request.CertPem?.Trim() ?? "";
        var keyPem = request.KeyPem?.Trim() ?? "";

        Rules.Require(!string.IsNullOrWhiteSpace(certPem), "证书内容 (CRT/PEM) 不能为空");
        Rules.Require(!string.IsNullOrWhiteSpace(keyPem), "私钥内容 (KEY) 不能为空");

        var parsed = ParseCertificate(certPem, keyPem);

        return await db.TransactionAsync(async tx =>
        {
            var existingCount = (await tx.OneAsync("select count(*) as count from ssl_certificates where thumbprint=@thumb", new { thumb = parsed.Thumbprint }, ct)).Id("count");
            Rules.Require(existingCount == 0, "该证书（相同指纹）已上传存在，无需重复导入", "data.duplicate", 409);

            var totalCerts = (await tx.OneAsync("select count(*) as count from ssl_certificates", ct: ct)).Id("count");
            var setActive = totalCerts == 0; // 首张证书默认设为生效

            var inserted = await tx.OneAsync(@"
                insert into ssl_certificates(
                    name, cert_pem, key_pem, subject_dn, issuer_dn, common_name, dns_names,
                    serial_number, thumbprint, valid_from, valid_to, is_active
                ) values (
                    @name, @certPem, @keyPem, @subjectDn, @issuerDn, @commonName, @dnsNames,
                    @serialNumber, @thumbprint, @validFrom, @validTo, @setActive
                ) returning *
            ", new
            {
                name,
                certPem,
                keyPem,
                subjectDn = parsed.SubjectDn,
                issuerDn = parsed.IssuerDn,
                commonName = parsed.CommonName,
                dnsNames = parsed.DnsNames,
                serialNumber = parsed.SerialNumber,
                thumbprint = parsed.Thumbprint,
                validFrom = parsed.ValidFrom,
                validTo = parsed.ValidTo,
                setActive
            }, ct);

            var created = ToCertDto(inserted!, includeCertPem: false);

            if (setActive)
            {
                await SyncToDiskAsync(certPem, keyPem, ct);
            }

            await audit.WriteAsync(actor.UserId, "ssl.certificate.upload", $"ssl_certificates/{created.Id}", $"上传并导入 SSL 证书：{name} ({parsed.CommonName})", ip, ct);
            return created;
        }, ct);
    }

    public async Task<SslCertificateDto> SetActiveCertificateAsync(Actor actor, long id, string? ip, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.manage", ct);
        var cert = await db.OneAsync("select * from ssl_certificates where id = @id", new { id }, ct);
        Rules.Require(cert != null, "指定证书不存在", "ssl.cert.not_found", 404);

        var certPem = cert.Text("certPem");
        var keyPem = cert.Text("keyPem");
        var name = cert.Text("name");
        var commonName = cert.Text("commonName");

        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("update ssl_certificates set is_active = (id = @id), updated_at = now()", new { id }, ct);
            await audit.WriteAsync(actor.UserId, "ssl.certificate.activate", $"ssl_certificates/{id}", $"切换当前生效 SSL 证书为：{name} ({commonName})", ip, ct);
            return true;
        }, ct);

        await SyncToDiskAsync(certPem, keyPem, ct);
        return await CertificateByIdAsync(actor, id, ct);
    }

    public async Task DeleteCertificateAsync(Actor actor, long id, string? ip, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.manage", ct);
        var cert = await db.OneAsync("select * from ssl_certificates where id = @id", new { id }, ct);
        Rules.Require(cert != null, "指定证书不存在", "ssl.cert.not_found", 404);
        Rules.Require(!cert.Flag("isActive"), "不能删除当前生效中的 SSL 证书，请先激活其他证书", "ssl.cert.active", 400);

        var name = cert.Text("name");
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("update ssl_domains set certificate_id = null where certificate_id = @id", new { id }, ct);
            await tx.ExecuteAsync("delete from ssl_certificates where id = @id", new { id }, ct);
            await audit.WriteAsync(actor.UserId, "ssl.certificate.delete", $"ssl_certificates/{id}", $"删除 SSL 证书：{name}", ip, ct);
            return true;
        }, ct);
    }

    public async Task<(string FileName, byte[] Bytes)> DownloadCertificateAsync(Actor actor, long id, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.read", ct);
        var cert = await db.OneAsync("select name, common_name, cert_pem from ssl_certificates where id = @id", new { id }, ct);
        Rules.Require(cert != null, "指定证书不存在", "ssl.cert.not_found", 404);

        var commonName = cert.Text("commonName", "certificate");
        var safeName = string.Concat(commonName.Split(Path.GetInvalidFileNameChars())).Replace('*', '_');
        var fileName = $"{safeName}.crt";
        var bytes = Encoding.UTF8.GetBytes(cert.Text("certPem"));
        return (fileName, bytes);
    }

    public async Task<IReadOnlyList<SslDomainDto>> DomainsAsync(Actor actor, string? search, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.read", ct);
        var filter = string.IsNullOrWhiteSpace(search)
            ? "1=1"
            : "(d.domain ilike @search or d.description ilike @search)";
        var rows = await db.QueryAsync($@"
            select d.*, c.name as cert_name, c.common_name as cert_common_name,
                   c.valid_to as cert_valid_to, c.dns_names as cert_dns_names
            from ssl_domains d
            left join ssl_certificates c on c.id = d.certificate_id
            where {filter}
            order by d.is_primary desc, d.created_at
        ", new { search = $"%{search?.Trim()}%" }, ct);

        return rows.Select(ToDomainDto).ToArray();
    }

    public async Task<SslDomainDto> SaveDomainAsync(Actor actor, long? id, SslDomainRequest request, string? ip, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.manage", ct);
        var domain = Rules.Domain(request.Domain);
        var description = request.Description?.Trim() ?? "";
        Rules.Require(description.Length <= 255, "备注描述不能超过 255 个字符");
        Rules.Require(request.Port is >= 1 and <= 65535, "端口必须在 1 到 65535 之间");
        Rules.Require(request.Protocol is "https" or "http", "协议必须为 https 或 http");

        var result = await db.TransactionAsync(async tx =>
        {
            var conflict = id.HasValue
                ? await tx.OneAsync("select id from ssl_domains where domain = @domain and id <> @id", new { domain, id = id.Value }, ct)
                : await tx.OneAsync("select id from ssl_domains where domain = @domain", new { domain }, ct);
            Rules.Require(conflict == null, $"域名或网址 {domain} 已存在", "data.duplicate", 409);

            if (request.CertificateId.HasValue)
            {
                var certExists = await tx.OneAsync("select id from ssl_certificates where id = @cid", new { cid = request.CertificateId.Value }, ct);
                Rules.Require(certExists != null, "关联的 SSL 证书不存在");
            }

            var totalDomains = (await tx.OneAsync("select count(*) as count from ssl_domains", ct: ct)).Id("count");
            var isPrimary = request.IsPrimary || (totalDomains == 0);

            if (isPrimary)
            {
                if (id.HasValue)
                {
                    await tx.ExecuteAsync("update ssl_domains set is_primary = false where id <> @id", new { id = id.Value }, ct);
                }
                else
                {
                    await tx.ExecuteAsync("update ssl_domains set is_primary = false", ct: ct);
                }
            }

            JsonObject? row;
            if (id.HasValue)
            {
                row = await tx.OneAsync(@"
                    update ssl_domains set
                        domain = @domain, port = @port, protocol = @protocol,
                        description = @description, is_primary = @isPrimary,
                        force_https = @forceHttps, hsts_enabled = @hstsEnabled,
                        certificate_id = cast(@certificateId as bigint), enabled = @enabled,
                        updated_at = now()
                    where id = @id
                    returning *
                ", new
                {
                    id = id.Value,
                    domain,
                    port = request.Port,
                    protocol = request.Protocol,
                    description,
                    isPrimary,
                    forceHttps = request.ForceHttps,
                    hstsEnabled = request.HstsEnabled,
                    certificateId = request.CertificateId,
                    enabled = request.Enabled
                }, ct);
                Rules.Require(row != null, "指定域名记录不存在", "ssl.domain.not_found", 404);
                await audit.WriteAsync(actor.UserId, "ssl.domain.update", $"ssl_domains/{id.Value}", $"更新受控域名：{domain} ({request.Port})", ip, ct);
            }
            else
            {
                row = await tx.OneAsync(@"
                    insert into ssl_domains (
                        domain, port, protocol, description, is_primary, force_https,
                        hsts_enabled, certificate_id, enabled
                    ) values (
                        @domain, @port, @protocol, @description, @isPrimary, @forceHttps,
                        @hstsEnabled, cast(@certificateId as bigint), @enabled
                    ) returning *
                ", new
                {
                    domain,
                    port = request.Port,
                    protocol = request.Protocol,
                    description,
                    isPrimary,
                    forceHttps = request.ForceHttps,
                    hstsEnabled = request.HstsEnabled,
                    certificateId = request.CertificateId,
                    enabled = request.Enabled
                }, ct);
                await audit.WriteAsync(actor.UserId, "ssl.domain.create", $"ssl_domains/{row.Id()}", $"添加受控域名：{domain} ({request.Port})", ip, ct);
            }

            var full = await tx.OneAsync(@"
                select d.*, c.name as cert_name, c.common_name as cert_common_name,
                       c.valid_to as cert_valid_to, c.dns_names as cert_dns_names
                from ssl_domains d
                left join ssl_certificates c on c.id = d.certificate_id
                where d.id = @id
            ", new { id = row.Id() }, ct);
            return ToDomainDto(full!);
        }, ct);

        await RefreshPlatformOptionsAsync(ct);
        return result;
    }

    public async Task<SslDomainDto> SetPrimaryDomainAsync(Actor actor, long id, string? ip, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.manage", ct);
        var result = await db.TransactionAsync(async tx =>
        {
            var exists = await tx.OneAsync("select * from ssl_domains where id = @id", new { id }, ct);
            Rules.Require(exists != null, "指定域名不存在", "ssl.domain.not_found", 404);

            await tx.ExecuteAsync("update ssl_domains set is_primary = (id = @id), updated_at = now()", new { id }, ct);
            await audit.WriteAsync(actor.UserId, "ssl.domain.set_primary", $"ssl_domains/{id}", $"设为主访问域名：{exists.Text("domain")}", ip, ct);

            var full = await tx.OneAsync(@"
                select d.*, c.name as cert_name, c.common_name as cert_common_name,
                       c.valid_to as cert_valid_to, c.dns_names as cert_dns_names
                from ssl_domains d
                left join ssl_certificates c on c.id = d.certificate_id
                where d.id = @id
            ", new { id }, ct);
            return ToDomainDto(full!);
        }, ct);

        await RefreshPlatformOptionsAsync(ct);
        return result;
    }

    public async Task DeleteDomainAsync(Actor actor, long id, string? ip, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.manage", ct);
        var domain = await db.OneAsync("select * from ssl_domains where id = @id", new { id }, ct);
        Rules.Require(domain != null, "指定域名不存在", "ssl.domain.not_found", 404);

        var domainName = domain.Text("domain");
        var wasPrimary = domain.Flag("isPrimary");
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("delete from ssl_domains where id = @id", new { id }, ct);
            if (wasPrimary)
            {
                await tx.ExecuteAsync("update ssl_domains set is_primary = true where id = (select id from ssl_domains order by id limit 1)", ct: ct);
            }
            await audit.WriteAsync(actor.UserId, "ssl.domain.delete", $"ssl_domains/{id}", $"删除受控域名：{domainName}", ip, ct);
            return true;
        }, ct);

        await RefreshPlatformOptionsAsync(ct);
    }

    public async Task RefreshPlatformOptionsAsync(CancellationToken ct = default)
    {
        var primary = await db.OneAsync(@"
            select domain, port, protocol
            from ssl_domains
            where enabled is true
            order by is_primary desc, id
            limit 1
        ", ct: ct);

        if (primary != null)
        {
            options.UpdateFromDomain(
                primary.Text("protocol", "https"),
                primary.Text("domain"),
                (int)(primary.Id("port") > 0 ? primary.Id("port") : 443)
            );
        }
    }

    public async Task<NginxConfigDto> GenerateNginxConfigAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "ssl.read", ct);
        var domains = await db.QueryAsync("select * from ssl_domains where enabled is true order by is_primary desc, id", ct: ct);
        var serverNames = domains.Count > 0
            ? string.Join(" ", domains.Select(d => d.Text("domain")).Distinct())
            : "_";

        var httpsPort = domains.FirstOrDefault(d => d.Text("protocol") == "https")?.Id("port") ?? 443;
        var hasHsts = domains.Any(d => d.Flag("hstsEnabled"));

        var sb = new StringBuilder();
        sb.AppendLine("# ================================================================");
        sb.AppendLine("# VisiCore (视枢) 平台反向代理与 SSL 站点配置");
        sb.AppendLine("# 由后台 [SSL管控] 模块自动生成");
        sb.AppendLine("# ================================================================");
        sb.AppendLine();
        sb.AppendLine("server {");
        sb.AppendLine("    listen 80;");
        sb.AppendLine($"    server_name {serverNames};");
        sb.AppendLine("    return 301 https://$host$request_uri;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("server {");
        sb.AppendLine($"    listen {httpsPort} ssl;");
        sb.AppendLine("    http2 on;");
        sb.AppendLine($"    server_name {serverNames};");
        sb.AppendLine();
        sb.AppendLine($"    ssl_certificate {options.SslCertFile.Replace('\\', '/')};");
        sb.AppendLine($"    ssl_certificate_key {options.SslKeyFile.Replace('\\', '/')};");
        sb.AppendLine("    ssl_protocols TLSv1.2 TLSv1.3;");
        sb.AppendLine("    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES128-GCM-SHA256:DHE-RSA-AES256-GCM-SHA384;");
        sb.AppendLine("    ssl_prefer_server_ciphers off;");
        sb.AppendLine("    ssl_session_timeout 1d;");
        sb.AppendLine("    ssl_session_cache shared:SSL:10m;");
        sb.AppendLine("    ssl_session_tickets off;");
        sb.AppendLine();
        if (hasHsts)
        {
            sb.AppendLine("    # HSTS 安全头 (1年)");
            sb.AppendLine("    add_header Strict-Transport-Security \"max-age=31536000; includeSubDomains\" always;");
        }
        sb.AppendLine("    add_header X-Content-Type-Options nosniff always;");
        sb.AppendLine("    add_header Referrer-Policy same-origin always;");
        sb.AppendLine();
        sb.AppendLine("    root /opt/video-platform-v2/current/web;");
        sb.AppendLine("    index index.html;");
        sb.AppendLine("    client_max_body_size 1024m;");
        sb.AppendLine();
        sb.AppendLine("    location = /health {");
        sb.AppendLine("        proxy_pass http://127.0.0.1:5082;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    location /api/ {");
        sb.AppendLine("        proxy_pass http://127.0.0.1:5082;");
        sb.AppendLine("        proxy_http_version 1.1;");
        sb.AppendLine("        proxy_set_header Host $host;");
        sb.AppendLine("        proxy_set_header X-Real-IP $remote_addr;");
        sb.AppendLine("        proxy_set_header X-Forwarded-Proto $scheme;");
        sb.AppendLine("        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        sb.AppendLine("        proxy_read_timeout 120s;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    location /hubs/v2/ {");
        sb.AppendLine("        proxy_pass http://127.0.0.1:5082;");
        sb.AppendLine("        proxy_http_version 1.1;");
        sb.AppendLine("        proxy_set_header Upgrade $http_upgrade;");
        sb.AppendLine("        proxy_set_header Connection upgrade;");
        sb.AppendLine("        proxy_set_header Host $host;");
        sb.AppendLine("        proxy_set_header X-Forwarded-Proto $scheme;");
        sb.AppendLine("        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        sb.AppendLine("        proxy_read_timeout 3600s;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    location ~ ^/media/(live|playback)/[A-Za-z0-9_-]+\\.live\\.(flv|ts)$ {");
        sb.AppendLine("        rewrite ^/media/(.*)$ /$1 break;");
        sb.AppendLine("        proxy_pass http://127.0.0.1:18082;");
        sb.AppendLine("        proxy_http_version 1.1;");
        sb.AppendLine("        proxy_buffering off;");
        sb.AppendLine("        proxy_read_timeout 3600s;");
        sb.AppendLine("        proxy_set_header Host $host;");
        sb.AppendLine("        proxy_set_header X-Real-IP $remote_addr;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    location /media/ { return 404; }");
        sb.AppendLine("    location /internal/ { return 404; }");
        sb.AppendLine("    location /assets/ { expires 1y; add_header Cache-Control \"public, immutable\"; }");
        sb.AppendLine("    location / { try_files $uri $uri/ /index.html; }");
        sb.AppendLine("}");

        return new NginxConfigDto(
            Content: sb.ToString(),
            ConfigPath: OperatingSystem.IsWindows() ? "nginx/conf/conf.d/visicore.conf" : "/etc/nginx/conf.d/visicore.conf",
            ReloadCommand: OperatingSystem.IsWindows() ? "nginx.exe -s reload" : "sudo nginx -t && sudo systemctl reload nginx"
        );
    }

    private async Task SyncToDiskAsync(string certPem, string keyPem, CancellationToken ct)
    {
        try
        {
            var dir = options.SslPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(options.SslCertFile, certPem.Trim() + "\n", Encoding.UTF8, ct);
            await File.WriteAllTextAsync(options.SslKeyFile, keyPem.Trim() + "\n", Encoding.UTF8, ct);
        }
        catch (Exception ex)
        {
            // 在权限受限环境（例如 Linux 非 root 写入 /etc/nginx/ssl 时），回退写入至应用本地安全数据目录
            try
            {
                var fallbackDir = Path.Combine(options.DataPath, "ssl");
                if (!Directory.Exists(fallbackDir)) Directory.CreateDirectory(fallbackDir);
                await File.WriteAllTextAsync(Path.Combine(fallbackDir, "video-platform.crt"), certPem.Trim() + "\n", Encoding.UTF8, ct);
                await File.WriteAllTextAsync(Path.Combine(fallbackDir, "video-platform.key"), keyPem.Trim() + "\n", Encoding.UTF8, ct);
            }
            catch
            {
                throw new PlatformException(500, "ssl.disk_sync_failed", $"证书已激活，但同步至磁盘文件失败：{ex.Message}");
            }
        }
    }

    private static (string SubjectDn, string IssuerDn, string CommonName, string[] DnsNames, string SerialNumber, string Thumbprint, DateTimeOffset ValidFrom, DateTimeOffset ValidTo) ParseCertificate(string certPem, string keyPem)
    {
        try
        {
            using var cert = X509Certificate2.CreateFromPem(certPem, keyPem);
            var commonName = cert.GetNameInfo(X509NameType.SimpleName, false) ?? "";
            if (string.IsNullOrWhiteSpace(commonName)) commonName = cert.Subject;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(commonName) && !commonName.Contains('='))
            {
                names.Add(commonName.Trim());
            }

            var sanExt = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
            if (sanExt != null)
            {
                foreach (var dns in sanExt.EnumerateDnsNames()) names.Add(dns.Trim());
                foreach (var ip in sanExt.EnumerateIPAddresses()) names.Add(ip.ToString().Trim());
            }

            var thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
            var serial = cert.SerialNumber;
            var notBefore = cert.NotBefore.ToUniversalTime();
            var notAfter = cert.NotAfter.ToUniversalTime();

            return (
                SubjectDn: cert.Subject,
                IssuerDn: cert.Issuer,
                CommonName: commonName,
                DnsNames: names.OrderBy(n => n).ToArray(),
                SerialNumber: serial,
                Thumbprint: thumbprint,
                ValidFrom: notBefore,
                ValidTo: notAfter
            );
        }
        catch (CryptographicException ce)
        {
            throw new PlatformException(400, "ssl.cert.crypto_error", $"SSL 证书或私钥解析校验失败：{ce.Message}");
        }
        catch (Exception ex) when (ex is not PlatformException)
        {
            throw new PlatformException(400, "ssl.cert.invalid", $"证书格式无效：{ex.Message}");
        }
    }

    public static bool IsDomainMatched(string domain, IEnumerable<string> sans)
    {
        var target = domain.Trim().ToLowerInvariant();
        foreach (var san in sans)
        {
            var pattern = san.Trim().ToLowerInvariant();
            if (pattern == target) return true;
            if (pattern.StartsWith("*.") && target.Length > pattern.Length - 1)
            {
                var suffix = pattern[1..]; // ".example.com"
                if (target.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    !target[..^suffix.Length].Contains('.'))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static SslCertificateDto ToCertDto(JsonObject row) => ToCertDto(row, false);

    private static SslCertificateDto ToCertDto(JsonObject row, bool includeCertPem)
    {
        var validTo = row.Time("validTo");
        var validFrom = row.Time("validFrom");
        var now = DateTimeOffset.UtcNow;
        var remainingDays = (int)Math.Ceiling((validTo - now).TotalDays);

        var status = remainingDays <= 0
            ? "expired"
            : remainingDays <= 30
                ? "expiring_soon"
                : "valid";

        string[] dnsNames = [];
        if (row["dnsNames"] is JsonArray arr)
        {
            dnsNames = arr.Select(n => n?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        return new SslCertificateDto(
            Id: row.Id(),
            Name: row.Text("name"),
            CommonName: row.Text("commonName"),
            DnsNames: dnsNames,
            IssuerDn: row.Text("issuerDn"),
            SubjectDn: row.Text("subjectDn"),
            SerialNumber: row.Text("serialNumber"),
            Thumbprint: row.Text("thumbprint"),
            ValidFrom: validFrom,
            ValidTo: validTo,
            DaysRemaining: remainingDays,
            Status: status,
            IsActive: row.Flag("isActive"),
            BoundDomainCount: row.Id("boundDomains"),
            CreatedAt: row.Time("createdAt"),
            CertPem: includeCertPem ? row.Text("certPem") : null
        );
    }

    private static SslDomainDto ToDomainDto(JsonObject row)
    {
        string[] dnsNames = [];
        if (row["certDnsNames"] is JsonArray arr)
        {
            dnsNames = arr.Select(n => n?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        var domain = row.Text("domain");
        var certId = long.TryParse(row.Text("certificateId"), out var cid) && cid > 0 ? cid : (long?)null;
        var matchStatus = certId is null
            ? "no_cert"
            : IsDomainMatched(domain, dnsNames) ? "matched" : "mismatched";

        DateTimeOffset? certValidTo = !string.IsNullOrWhiteSpace(row.Text("certValidTo"))
            ? row.Time("certValidTo")
            : null;

        return new SslDomainDto(
            Id: row.Id(),
            Domain: domain,
            Port: (int)row.Id("port"),
            Protocol: row.Text("protocol", "https"),
            Description: row.Text("description"),
            IsPrimary: row.Flag("isPrimary"),
            ForceHttps: row.Flag("forceHttps"),
            HstsEnabled: row.Flag("hstsEnabled"),
            CertificateId: certId,
            CertificateName: string.IsNullOrWhiteSpace(row.Text("certName")) ? null : row.Text("certName"),
            CertificateCommonName: string.IsNullOrWhiteSpace(row.Text("certCommonName")) ? null : row.Text("certCommonName"),
            CertificateValidTo: certValidTo,
            CertMatchStatus: matchStatus,
            Enabled: row.Flag("enabled"),
            CreatedAt: row.Time("createdAt"),
            UpdatedAt: row.Time("updatedAt")
        );
    }
}
