using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using VideoPlatform.Infrastructure;

internal sealed class ReleaseRegression(IServiceProvider services, HttpClient manager)
{
    private readonly Database db = services.GetRequiredService<Database>();
    private readonly PlatformOptions options = services.GetRequiredService<PlatformOptions>();
    private const string Version = "2.0.0";
    public int Passed { get; private set; }

    public async Task RunAsync()
    {
        var login = await services.GetRequiredService<SessionStore>().LoginAsync(new("admin", "Admin-Test-Password-2026!", "desktop", Version), "127.0.0.1");
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        using var anonymous = new HttpClient { BaseAddress = manager.BaseAddress };
        // 固定内容只验证上传存储和下载字节，不将测试数据宣称为可安装的 MSI。
        byte[] zip = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        byte[] msi = Enumerable.Range(0, 2048).Select(i => (byte)(255 - i % 251)).ToArray();
        JsonObject? zipRow = null, msiRow = null;

        await CheckAsync("发布迁移允许同版本 ZIP 后接 MSI，文件大小与哈希正确", async () =>
        {
            Require((await db.OneAsync("select count(*) as count from schema_migrations where version like '%003_release_packages.sql'")).Id("count") == 1, "新发布迁移未执行");
            zipRow = await UploadAsync("desktop.zip", zip);
            msiRow = await UploadAsync("desktop.msi", msi);
            Require(zipRow.Id() != msiRow.Id(), "同版本双包没有独立记录");
            Require((await db.OneAsync("select count(*) as count from releases where version=@version", new { version = Version })).Id("count") == 2, "双包未真实持久化");
            foreach (var (row, bytes) in new[] { (zipRow, zip), (msiRow, msi) })
            {
                Require(row.Id("fileSize") == bytes.Length && row.Text("sha256").Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase), "上传响应大小或 SHA256 不正确");
                Require(row.Text("status") == "draft" && row["storageName"] is null, "上传初始状态或存储名隐藏错误");
                var stored = (await db.OneAsync("select storage_name from releases where id=@id", new { id = row.Id() }))!;
                Require((await File.ReadAllBytesAsync(Path.Combine(options.ReleasesPath, stored.Text("storageName")))).SequenceEqual(bytes), "上传文件字节未正确持久化");
            }
        });

        await CheckAsync("同版本同类型及大小写扩展名重复上传返回 409 且无孤立文件", async () =>
        {
            var files = Directory.GetFiles(options.ReleasesPath).Order().ToArray();
            foreach (var name in new[] { "another.zip", "another.ZIP", "another.msi", "another.MSI" })
            {
                using var form = Form(name, zip);
                using var response = await manager.PostAsync("/api/v2/releases", form);
                await StatusAsync(response, HttpStatusCode.Conflict);
                Require((await response.Content.ReadFromJsonAsync<JsonObject>()).Text("code") == "data.duplicate", "重复包未映射为标准唯一冲突");
            }
            Require(Directory.GetFiles(options.ReleasesPath).Order().SequenceEqual(files), "重复上传残留文件");
            Require((await db.OneAsync("select count(*) as count from releases")).Id("count") == 2, "重复上传产生额外记录");
            Require((await db.OneAsync("select count(*) as count from audit_logs where action='release.upload'")).Id("count") == 2, "失败上传错误记为成功审计");
        });

        await CheckAsync("草稿不公开、匿名上传拒绝、ZIP 和 MSI 可分别发布", async () =>
        {
            using var latest = await anonymous.GetAsync("/api/v2/public/releases/latest");
            await StatusAsync(latest, HttpStatusCode.NoContent);
            foreach (var row in new[] { zipRow!, msiRow! })
            {
                using var download = await anonymous.GetAsync($"/api/v2/releases/{row.Id()}/download");
                await StatusAsync(download, HttpStatusCode.Unauthorized);
            }
            using var form = Form("anonymous.zip", zip);
            using var denied = await anonymous.PostAsync("/api/v2/releases", form);
            await StatusAsync(denied, HttpStatusCode.Unauthorized);
            foreach (var row in new[] { msiRow!, zipRow! })
            {
                using var published = await manager.PostAsJsonAsync($"/api/v2/releases/{row.Id()}/publish", new { minimumVersion = "1.0.0", forceUpdate = false });
                await StatusAsync(published, HttpStatusCode.NoContent);
            }
            var rows = await db.QueryAsync("select status,published_at from releases");
            Require(rows.All(row => row.Text("status") == "published" && row["publishedAt"] is not null), "双包发布状态未持久化");
        });

        await CheckAsync("latest 默认优先 MSI、packages 返回双包且按类型筛选正确", async () =>
        {
            foreach (var path in new[] { "/api/v2/public/releases/latest", "/api/desktop-releases/latest" })
            {
                var latest = await LatestAsync(anonymous, path + "?currentVersion=1.0.0");
                Require(latest.Id() == msiRow.Id() && latest.Flag("updateAvailable"), "latest 未优先返回 MSI 或更新提示错误");
                var packages = latest["packages"]!.AsArray();
                Require(packages.Count == 2 && packages.Select(row => row.Id()).Order().SequenceEqual(new[] { zipRow.Id(), msiRow.Id() }.Order()), "packages 缺少同版本双包");
                Require(packages.All(row => row!["storageName"] is null && Uri.TryCreate(row.Text("downloadUrl"), UriKind.Absolute, out _)), "公开包信息泄露存储名或缺少下载链接");
            }
            foreach (var (type, row) in new[] { ("zip", zipRow!), ("msi", msiRow!) })
            {
                var latest = await LatestAsync(anonymous, $"/api/v2/public/releases/latest?packageType={type}&currentVersion=2.0.0");
                Require(latest.Id() == row.Id() && !latest.Flag("updateAvailable") && latest["packages"]!.AsArray().Count == 2, "类型筛选或相同版本判断错误");
            }
        });

        await CheckAsync("双包公开下载哈希及 Range、后缀 Range、越界 416 正确", async () =>
        {
            foreach (var (row, bytes) in new[] { (zipRow!, zip), (msiRow!, msi) })
            {
                var path = $"/api/v2/releases/{row.Id()}/download";
                using var full = await anonymous.GetAsync(path);
                await StatusAsync(full, HttpStatusCode.OK);
                Require((await full.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes), "下载完整文件不一致");
                Require(full.Content.Headers.ContentDisposition?.FileNameStar == row.Text("fileName"), "下载文件名不正确");
                foreach (var suffix in new[] { false, true })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, path);
                    request.Headers.Range = suffix ? new RangeHeaderValue(null, 16) : new RangeHeaderValue(10, 25);
                    using var range = await anonymous.SendAsync(request);
                    await StatusAsync(range, HttpStatusCode.PartialContent);
                    var from = suffix ? bytes.Length - 16 : 10;
                    var to = suffix ? bytes.Length - 1 : 25;
                    Require(range.Content.Headers.ContentRange?.From == from && range.Content.Headers.ContentRange?.To == to && range.Content.Headers.ContentRange?.Length == bytes.Length, "Content-Range 不正确");
                    Require((await range.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes[from..(to + 1)]), "Range 字节错误");
                }
                using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, path);
                invalidRequest.Headers.Range = new RangeHeaderValue(bytes.Length + 1, null);
                using var invalidRange = await anonymous.SendAsync(invalidRequest);
                await StatusAsync(invalidRange, HttpStatusCode.RequestedRangeNotSatisfiable);
                Require((await db.OneAsync("select download_count from releases where id=@id", new { id = row.Id() })).Id("downloadCount") >= 3, "下载计数未持久化");
            }
        });

        await CheckAsync("撤回 MSI 后 latest 回退 ZIP，撤回双包后公开下载及 latest 均不可用", async () =>
        {
            using var revokeMsi = await manager.PostAsync($"/api/v2/releases/{msiRow.Id()}/revoke", null);
            await StatusAsync(revokeMsi, HttpStatusCode.NoContent);
            var latest = await LatestAsync(anonymous, "/api/v2/public/releases/latest");
            Require(latest.Id() == zipRow.Id() && latest["packages"]!.AsArray().Count == 1, "撤回 MSI 后未仅保留 ZIP");
            using var noMsi = await anonymous.GetAsync("/api/v2/public/releases/latest?packageType=msi");
            await StatusAsync(noMsi, HttpStatusCode.NoContent);
            using var revokeZip = await manager.PostAsync($"/api/v2/releases/{zipRow.Id()}/revoke", null);
            await StatusAsync(revokeZip, HttpStatusCode.NoContent);
            using var none = await anonymous.GetAsync("/api/v2/public/releases/latest");
            await StatusAsync(none, HttpStatusCode.NoContent);
            foreach (var row in new[] { zipRow!, msiRow! })
            {
                using var denied = await anonymous.GetAsync($"/api/v2/releases/{row.Id()}/download");
                await StatusAsync(denied, HttpStatusCode.Unauthorized);
                using var retained = await manager.GetAsync($"/api/v2/releases/{row.Id()}/download");
                await StatusAsync(retained, HttpStatusCode.OK);
            }
            Require((await db.QueryAsync("select status from releases")).All(row => row.Text("status") == "revoked"), "撤回状态未持久化");
        });
    }

    private static MultipartFormDataContent Form(string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(Version), "version");
        form.Add(new StringContent("双包数据库回归"), "releaseNotes");
        form.Add(new ByteArrayContent(bytes), "file", name);
        return form;
    }

    private async Task<JsonObject> UploadAsync(string name, byte[] bytes)
    {
        using var form = Form(name, bytes);
        using var response = await manager.PostAsync("/api/v2/releases", form);
        await StatusAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static async Task<JsonObject> LatestAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        await StatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static async Task StatusAsync(HttpResponseMessage response, HttpStatusCode expected)
        => Require(response.StatusCode == expected, $"发布接口预期 {(int)expected}，实际 {(int)response.StatusCode}：{(response.StatusCode == expected ? "" : await response.Content.ReadAsStringAsync())}");

    private async Task CheckAsync(string name, Func<Task> check)
    {
        await check();
        Passed++;
        Console.WriteLine("通过：" + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
