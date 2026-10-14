using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Pon;

public sealed class EsightPonClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<EsightPonClient>? _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private DateTimeOffset _lastAuthTime = DateTimeOffset.MinValue;
    private string _baseUrl = "https://10.37.5.2:32102";
    private string _username = "visicore";
    private string _password = "";
    private string? _authToken;

    public EsightPonClient(PlatformOptions? options = null, ILogger<EsightPonClient>? logger = null)
    {
        _logger = logger;
        var ssl = new System.Net.Security.SslClientAuthenticationOptions();
        // 默认使用系统信任链；仅当运维登记了证书指纹时才启用固定指纹放行。
        var pinned = TlsTrust.PinnedCallback(options?.TrustedCertificateThumbprints ?? []);
        if (pinned is not null) ssl.RemoteCertificateValidationCallback = pinned;
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            SslOptions = ssl,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) VisiCore/2.0");
    }

    public void Configure(string baseUrl, string username, string password)
    {
        var rawUrl = baseUrl.TrimEnd('/');
        // 若传入的是旧交互管理端口 31943，自适应重定向至北向 OpenAPI 网关 32102
        if (rawUrl.Contains(":31943"))
        {
            rawUrl = rawUrl.Replace(":31943", ":32102");
        }
        _baseUrl = rawUrl;
        _username = username;
        _password = password;
        _authToken = null;
        _lastAuthTime = DateTimeOffset.MinValue;
    }

    public async Task<(bool ok, string message)> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(false, ct);
            var olts = await QueryOltDevicesAsync(ct);
            var count = olts.Count;
            return (true, $"华为 iMaster NeoSight 官方北向 OpenAPI 网关 (32102) 连接正常，已发现 {count} 台 OLT 网元");
        }
        catch (Exception ex)
        {
            return (false, $"华为网管服务响应异常：{ex.Message}");
        }
    }

    public async Task EnsureAuthenticatedAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && _lastAuthTime != DateTimeOffset.MinValue && !string.IsNullOrWhiteSpace(_authToken) && DateTimeOffset.UtcNow - _lastAuthTime < TimeSpan.FromMinutes(20))
            return;

        await _authLock.WaitAsync(ct);
        try
        {
            if (!force && _lastAuthTime != DateTimeOffset.MinValue && !string.IsNullOrWhiteSpace(_authToken) && DateTimeOffset.UtcNow - _lastAuthTime < TimeSpan.FromMinutes(20))
                return;

            _logger?.LogInformation("正在连接华为 NeoSight 官方 OpenAPI 网关进行认证：{BaseUrl} (用户: {User})", _baseUrl, _username);

            var sessionUrl = $"{_baseUrl}/rest/openapi/sm/session";
            var payload = new JsonObject
            {
                ["userid"] = _username,
                ["value"] = _password
            };

            using var req = new HttpRequestMessage(HttpMethod.Put, sessionUrl)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();

            var resJson = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct)
                ?? throw new PlatformException(502, "esight.auth_failed", "网管未返回认证响应");

            var code = resJson["code"]?.GetValue<int?>() ?? -1;
            if (code != 0)
            {
                var desc = resJson["description"]?.ToString() ?? "认证失败";
                throw new PlatformException(401, "esight.auth_failed", $"华为 OpenAPI 登录失败：{desc}");
            }

            _authToken = resJson["data"]?.ToString()
                ?? throw new PlatformException(502, "esight.token_missing", "网管未返回有效会话令牌");
            _lastAuthTime = DateTimeOffset.UtcNow;
            _logger?.LogInformation("华为 iMaster NeoSight 官方北向 OpenAPI 会话创建成功，令牌已就绪");
        }
        catch (Exception ex) when (ex is not PlatformException)
        {
            _logger?.LogError(ex, "连接华为 NeoSight OpenAPI 网关发生异常");
            throw new PlatformException(502, "esight.connection_error", $"连接华为网管平台失败：{ex.Message}");
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<JsonNode?> SendOpenApiAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(false, ct);
        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : $"{_baseUrl}{path}";

        void ApplyHeaders(HttpRequestMessage req)
        {
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(_authToken))
            {
                req.Headers.Add("X-Auth-Token", _authToken);
                req.Headers.Add("Openid", _authToken);
            }
        }

        HttpResponseMessage res;
        try
        {
            using var req = new HttpRequestMessage(method, url);
            ApplyHeaders(req);
            if (body is not null)
            {
                var json = body is string s ? s : JsonSerializer.Serialize(body);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            res = await _http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            throw new PlatformException(502, "esight.request_failed", $"请求网管接口失败：{ex.Message}");
        }

        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger?.LogWarning("网管 OpenAPI 令牌失效或被拒，正在重新认证重试...");
            await EnsureAuthenticatedAsync(true, ct);

            using var retryReq = new HttpRequestMessage(method, url);
            ApplyHeaders(retryReq);
            if (body is not null)
            {
                var json = body is string s ? s : JsonSerializer.Serialize(body);
                retryReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            res = await _http.SendAsync(retryReq, ct);
        }

        var content = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger?.LogWarning("网管 OpenAPI 接口返回错误状态码 {Status}：{Content}", res.StatusCode, content);
            throw new PlatformException((int)res.StatusCode, "esight.api_error", $"网管接口调用失败 [{res.StatusCode}]: {content}");
        }

        try
        {
            return JsonNode.Parse(content);
        }
        catch
        {
            return JsonValue.Create(content);
        }
    }

    public async Task<JsonArray> QueryOltDevicesAsync(CancellationToken ct = default)
    {
        var res = await SendOpenApiAsync(HttpMethod.Get, "/rest/openapi/ponmgr/devices/olt", ct: ct);
        if (res?["data"] is JsonArray arr)
        {
            return arr;
        }
        return new JsonArray();
    }

    public async Task<JsonArray> QueryOnuDevicesAsync(CancellationToken ct = default)
    {
        var res = await SendOpenApiAsync(HttpMethod.Get, "/rest/openapi/ponmgr/devices/onu", ct: ct);
        if (res?["data"] is JsonArray arr)
        {
            return arr;
        }
        return new JsonArray();
    }

    public async Task ModifyOnuAliasAsync(
        string sn,
        string newAlias,
        string? neDn = null,
        int? onuId = null,
        string? onuName = null,
        long? ifIndex = null,
        string? onuDesc = null,
        CancellationToken ct = default)
    {
        string path = "/rest/openapi/ponmgr/onu/modifyaliasname";
        object body;

        if (!string.IsNullOrWhiteSpace(neDn))
        {
            var condition = new Dictionary<string, object?>
            {
                ["neDn"] = neDn,
                ["onuId"] = onuId,
                ["ifIndex"] = ifIndex,
                ["onuAlias"] = newAlias,
                ["onuDesc"] = onuDesc ?? "",
                ["onuName"] = onuName ?? "",
                ["shouldCreateSceneTask"] = true
            };
            var condJson = JsonSerializer.Serialize(condition);
            path = $"/rest/openapi/ponmgr/onu/modifyaliasname?modifyCondition={Uri.EscapeDataString(condJson)}";

            body = new Dictionary<string, object?>
            {
                ["sn"] = sn,
                ["onuAliasName"] = newAlias,
                ["modifyCondition"] = condJson,
                ["shouldCreateSceneTask"] = true
            };
        }
        else
        {
            body = new
            {
                sn,
                onuAliasName = newAlias
            };
        }

        var res = await SendOpenApiAsync(HttpMethod.Post, path, body, ct);
        var errCode = res?["errorCode"]?.ToString() ?? res?["code"]?.ToString();
        if (errCode != "0" && errCode != "200" && errCode != null)
        {
            var reason = res?["failedReason"]?.ToString() ?? res?["description"]?.ToString() ?? "未知错误";
            throw new PlatformException(502, "esight.modify_failed", $"华为网管修改别名失败：{reason} (errorCode: {errCode})");
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_authToken)) return;

        try
        {
            var sessionUrl = $"{_baseUrl}/rest/openapi/sm/session";
            using var req = new HttpRequestMessage(HttpMethod.Delete, sessionUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Add("X-Auth-Token", _authToken);
            req.Headers.Add("Openid", _authToken);
            using var res = await _http.SendAsync(req, ct);
        }
        catch
        {
            // 退出清理异常静默处理
        }
        finally
        {
            _authToken = null;
            _lastAuthTime = DateTimeOffset.MinValue;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await LogoutAsync(CancellationToken.None);
        _http.Dispose();
        _authLock.Dispose();
    }
}
