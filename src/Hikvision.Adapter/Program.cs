using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Security.Cryptography;
using System.Text;

if (Environment.GetEnvironmentVariable("HIK_ADAPTER_REVIEW_SELF_TEST") == "1")
{
    await AdapterReviewChecks.RunAsync(); return;
}

if (Environment.GetEnvironmentVariable("HIK_ALARM_SELF_TEST") == "1")
{
    AlarmParser.SelfTest(); Console.WriteLine("报警指针复制与解析自检通过。"); return;
}
if (Environment.GetEnvironmentVariable("HIK_PLAYBACK_TIMELINE_SELF_TEST") == "1")
{
    PlaybackTimeline.SelfTest(); Console.WriteLine("录像时间线自检通过。"); return;
}

await AdapterHost.RunAsync(args);

internal static class AdapterHost
{
    public static async Task RunAsync(string[] args)
    {
        var key = Environment.GetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("缺少适配器内部访问密钥 HIK_ADAPTER_INTERNAL_KEY。");
        var url = Environment.GetEnvironmentVariable("HIK_ADAPTER_API_URL") ?? "http://127.0.0.1:5090";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidOperationException("适配器只能监听明确的回环 HTTP 地址。");
        var root = Environment.GetEnvironmentVariable("HIK_ADAPTER_DATA_ROOT") ?? (OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform", "adapter-v2") : "/var/lib/video-platform/adapter-v2");
        var exportRoot = Environment.GetEnvironmentVariable("HIK_EXPORT_ROOT") ?? (OperatingSystem.IsWindows() ? Path.Combine(root, "exports") : "/var/lib/video-platform/exports");
        var simulated = Environment.GetEnvironmentVariable("HIK_ADAPTER_SIMULATOR") == "1";
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(url);
        var app = builder.Build();
        using var sdk = new SdkRuntime();
        using var zlm = new ZlmClient();
        var budget = new TranscodeBudget();
        await using var exports = new ExportService(exportRoot);
        await using var registry = new DeviceRegistry(sdk, zlm, budget, exports, Path.Combine(root, "alarms"), simulated);
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        app.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers["X-Adapter-Key"].ToString();
            if (string.IsNullOrEmpty(supplied) || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { code = "ADAPTER_UNAUTHORIZED", message = "内部访问密钥无效。", traceId = context.TraceIdentifier }); return;
            }
            try { await next(context); }
            catch (Exception ex)
            {
                if (context.Response.HasStarted) throw;
                var status = ex switch { AdapterException adapter => adapter.Status, ArgumentException => 400, BadHttpRequestException => 400, OperationCanceledException => 504, TimeoutException => 504, _ => 502 };
                var message = ex switch { AdapterException or ArgumentException or TimeoutException => ex.Message, OperationCanceledException => "设备操作超时。", _ => "适配器处理失败，请检查设备状态和服务配置。" };
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { code = ex is AdapterException error ? error.Code : status == 400 ? "INVALID_REQUEST" : "ADAPTER_ERROR", message, traceId = context.TraceIdentifier });
            }
        });
        AdapterEndpoints.Map(app, registry, exports);
        if (!simulated)
        {
            try { await zlm.CleanupOrphansAsync(); }
            catch { Console.Error.WriteLine("启动时未能清理 ZLMediaKit 遗留流，请检查媒体服务；设备注册仍可继续。"); }
        }
        var polling = registry.PollAsync(app.Lifetime.ApplicationStopping);
        try { await app.RunAsync(); }
        finally { await polling; await app.DisposeAsync(); }
    }
}
