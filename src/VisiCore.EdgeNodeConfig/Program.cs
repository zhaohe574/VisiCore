using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using VisiCore.EdgeAgent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("center", client => client.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddHttpClient("agent", client => client.Timeout = TimeSpan.FromSeconds(4));
var app = builder.Build();

app.MapGet("/", () => Results.Content(ConfigurationPage.Html, "text/html; charset=utf-8"));
app.MapGet("/api/status", async (IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    try
    {
        using var response = await factory.CreateClient("agent").GetAsync("http://edge-agent:8080/api/v1/edge-agent/identity", cancellationToken);
        return response.IsSuccessStatusCode
            ? Results.Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json")
            : Results.Json(new { available = false });
    }
    catch (HttpRequestException)
    {
        return Results.Json(new { available = false });
    }
});

app.MapPost("/api/test", async (EdgeNodeConfigurationPageRequest request, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    if (!TryNormalizeCenterUri(request.ControlPlaneBaseUri, out var uri, out var failureKind))
    {
        return Results.BadRequest(new { failureKind });
    }
    try
    {
        using var response = await factory.CreateClient("center").GetAsync(new Uri(uri, "healthz"), cancellationToken);
        return response.IsSuccessStatusCode
            ? Results.Ok(new { succeeded = true })
            : Results.BadRequest(new { failureKind = "center_health_failed" });
    }
    catch (HttpRequestException)
    {
        return Results.BadRequest(new { failureKind = "center_connection_failed" });
    }
    catch (TaskCanceledException)
    {
        return Results.BadRequest(new { failureKind = "center_connection_timeout" });
    }
});

app.MapPost("/api/host-test", async (EdgeNodeConfigurationPageRequest request, CancellationToken cancellationToken) =>
{
    string? hostFailureKind = null;
    string? centerFailureKind = null;
    if (!TryValidateHostInput(request.Host, out hostFailureKind) || !TryNormalizeCenterUri(request.ControlPlaneBaseUri, out var uri, out centerFailureKind))
    {
        return Results.BadRequest(new { failureKind = hostFailureKind ?? centerFailureKind });
    }
    var tokenPath = Environment.GetEnvironmentVariable("VISICORE_CONFIG_TOKEN_PATH") ?? "/var/run/visicore-config/access.token";
    var socketPath = Environment.GetEnvironmentVariable("VISICORE_CONFIG_SOCKET_PATH") ?? "/var/run/visicore-config/host-agent.sock";
    if (!File.Exists(tokenPath)) return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "configuration_host_unavailable");
    try
    {
        var command = new EdgeNodeConfigurationCommand(
            (await File.ReadAllTextAsync(tokenPath, cancellationToken)).Trim(),
            "test",
            uri.AbsoluteUri,
            Host: request.Host?.ToContract());
        var result = await HostConfigurationSocketClient.SendAsync(socketPath, command, cancellationToken);
        return result.Succeeded ? Results.Ok(new { succeeded = true }) : Results.BadRequest(new { failureKind = result.FailureKind });
    }
    catch (IOException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "configuration_host_unavailable");
    }
    catch (SocketException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "configuration_host_unavailable");
    }
});

app.MapPost("/api/apply", async (EdgeNodeConfigurationPageRequest request, CancellationToken cancellationToken) =>
{
    if (!TryNormalizeCenterUri(request.ControlPlaneBaseUri, out var uri, out var centerFailureKind) || string.IsNullOrWhiteSpace(request.EnrollmentCode))
    {
        return Results.BadRequest(new { failureKind = centerFailureKind ?? "enrollment_code_required" });
    }
    if (!TryValidateHostInput(request.Host, out var hostFailureKind))
    {
        return Results.BadRequest(new { failureKind = hostFailureKind });
    }

    var tokenPath = Environment.GetEnvironmentVariable("VISICORE_CONFIG_TOKEN_PATH") ?? "/var/run/visicore-config/access.token";
    var socketPath = Environment.GetEnvironmentVariable("VISICORE_CONFIG_SOCKET_PATH") ?? "/var/run/visicore-config/host-agent.sock";
    if (!File.Exists(tokenPath)) return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "configuration_host_unavailable");
    try
    {
        var command = new EdgeNodeConfigurationCommand(
            (await File.ReadAllTextAsync(tokenPath, cancellationToken)).Trim(),
            "apply",
            uri.AbsoluteUri,
            request.EnrollmentCode.Trim(),
            request.Host?.ToContract());
        var result = await HostConfigurationSocketClient.SendAsync(socketPath, command, cancellationToken);
        return result.Succeeded
            ? Results.Ok(new { succeeded = true, hostRestarting = result.HostRestarting })
            : Results.BadRequest(new { failureKind = result.FailureKind ?? "configuration_apply_failed" });
    }
    catch (IOException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "configuration_host_unavailable");
    }
    catch (SocketException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "configuration_host_unavailable");
    }
});

await app.RunAsync();

static bool TryNormalizeCenterUri(string? value, out Uri uri, out string? failureKind)
{
    uri = null!;
    failureKind = null;
    if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
    {
        failureKind = "control_plane_invalid";
        return false;
    }
    uri = new Uri(parsed.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    return true;
}

static bool TryValidateHostInput(EdgeNodeHostConfigurationPageInput? host, out string? failureKind)
{
    failureKind = null;
    if (host is null) return true;
    var hosts = host.AllowedArtifactHosts?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray() ?? [];
    if (hosts.Any(value => value.Length > 253 || value.Contains('/') || value.Contains(':')) || host.MaximumArtifactBytes is < 1_048_576 or > 8L * 1024 * 1024 * 1024 || host.ExecutionTimeoutSeconds is < 30 or > 3600)
    {
        failureKind = "host_configuration_invalid";
        return false;
    }
    if (!string.IsNullOrWhiteSpace(host.SigningPublicKeyPem))
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(host.SigningPublicKeyPem);
        }
        catch (CryptographicException)
        {
            failureKind = "host_public_key_invalid";
            return false;
        }
    }
    if (host.AllowExecution && (string.IsNullOrWhiteSpace(host.SigningPublicKeyPem) || string.IsNullOrWhiteSpace(host.SigningPublicKeyId) || hosts.Length == 0))
    {
        failureKind = "host_execution_configuration_incomplete";
        return false;
    }
    return true;
}

sealed record EdgeNodeConfigurationPageRequest(string? ControlPlaneBaseUri, string? EnrollmentCode = null, EdgeNodeHostConfigurationPageInput? Host = null);

sealed record EdgeNodeHostConfigurationPageInput(bool Enabled, bool AllowExecution, string? SigningPublicKeyId, string? SigningPublicKeyPem, string[]? AllowedArtifactHosts, long MaximumArtifactBytes, int ExecutionTimeoutSeconds)
{
    public EdgeNodeHostConfigurationInput ToContract() => new(Enabled, AllowExecution, SigningPublicKeyId, SigningPublicKeyPem, AllowedArtifactHosts ?? [], MaximumArtifactBytes, ExecutionTimeoutSeconds);
}

static class HostConfigurationSocketClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<EdgeNodeConfigurationCommandResult> SendAsync(string socketPath, EdgeNodeConfigurationCommand command, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        await WriteAsync(stream, command, cancellationToken);
        return await ReadAsync<EdgeNodeConfigurationCommandResult>(stream, cancellationToken) ?? new EdgeNodeConfigurationCommandResult(false, "configuration_host_invalid_response");
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        if (!await ReadExactlyAsync(stream, prefix, cancellationToken)) return default;
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 1 or > 131_072) return default;
        var payload = new byte[length];
        return !await ReadExactlyAsync(stream, payload, cancellationToken) ? default : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}

static class ConfigurationPage
{
public const string Html = """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>VisiCore 边缘节点配置</title><style>body{max-width:760px;margin:32px auto;padding:0 18px;font-family:system-ui;color:#17212b}fieldset{margin:16px 0;padding:18px;border:1px solid #ccd4dc}label{display:block;margin:10px 0 4px}input,textarea{box-sizing:border-box;width:100%;padding:9px}button{margin:14px 8px 0 0;padding:9px 14px}small,#status{color:#596675;white-space:pre-wrap}</style></head><body><h1>VisiCore 边缘节点配置</h1><p id="status">正在读取节点状态。</p><fieldset><legend>连接与配对</legend><label>中心 HTTPS 地址</label><input id="center" placeholder="https://visicore.example.com/"><label>一次性配对凭证</label><input id="code" type="password" autocomplete="off"><small>测试不会发送或消耗配对凭证。确认配对后才会应用配置并启动节点。</small><br><button onclick="testCenter()">测试中心连接</button><button onclick="applyConfig()">确认配对并生效</button></fieldset><fieldset><legend>Host Agent 高级设置</legend><label><input id="hostEnabled" type="checkbox" checked style="width:auto"> 启用 Host Agent 验证服务</label><label><input id="allowExecution" type="checkbox" style="width:auto"> 允许实际受控升级</label><label>发行签名 PEM 文件</label><input id="pem" type="file" accept=".pem,text/plain"><label>公钥标识</label><input id="keyId" placeholder="release-key-2026"><label>受信下载域名（每行一个）</label><textarea id="hosts" rows="3">github.com&#10;objects.githubusercontent.com</textarea><label>最大制品大小（MiB）</label><input id="max" type="number" value="2048" min="1" max="8192"><label>执行超时（秒）</label><input id="timeout" type="number" value="600" min="30" max="3600"><button onclick="testHost()">测试 Host 设置</button></fieldset><script>const setStatus=x=>document.getElementById('status').textContent=x;let pemText='';document.getElementById('pem').onchange=async e=>{pemText=e.target.files[0]?await e.target.files[0].text():''};function requestBody(withCode=false){return{controlPlaneBaseUri:center.value,enrollmentCode:withCode?code.value:null,host:{enabled:hostEnabled.checked,allowExecution:allowExecution.checked,signingPublicKeyId:keyId.value,signingPublicKeyPem:pemText,allowedArtifactHosts:hosts.value.split(/\\n/),maximumArtifactBytes:Number(max.value)*1024*1024,executionTimeoutSeconds:Number(timeout.value)}}}async function call(url,p){let r=await fetch(url,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(p)});let j=await r.json().catch(()=>({}));if(!r.ok)throw Error(j.failureKind||j.title||'请求失败');return j}async function testCenter(){try{await call('/api/test',requestBody());setStatus('中心连接测试通过。')}catch(e){setStatus('测试失败：'+e.message)}}async function testHost(){try{await call('/api/host-test',requestBody());setStatus('Host 设置校验通过。')}catch(e){setStatus('测试失败：'+e.message)}}async function applyConfig(){try{let r=await call('/api/apply',requestBody(true));setStatus(r.hostRestarting?'配置已应用，Host Agent 正在重启；节点正在登记。':'配置已应用，节点正在登记。');setTimeout(refreshStatus,3000)}catch(e){setStatus('配置未生效：'+e.message)}}async function refreshStatus(){try{let r=await fetch('/api/status');let j=await r.json();setStatus(j.isEnrolled?'节点已登记：'+j.agentId:'节点待配对或正在同步。')}catch{setStatus('节点状态暂不可用。')}}refreshStatus();</script></body></html>
""";
}
