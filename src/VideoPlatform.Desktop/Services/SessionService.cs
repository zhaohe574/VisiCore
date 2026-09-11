using System.Net;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using VideoPlatform.Desktop.Models;
using Generated = VideoPlatform.Client.Generated;

namespace VideoPlatform.Desktop.Services;

public sealed class PlatformException(string message, HttpStatusCode statusCode, string? code = null, string? traceId = null)
    : HttpRequestException(message, null, statusCode)
{
    public string? Code { get; } = code;
    public string? TraceId { get; } = traceId;
}

public sealed class SessionService(HttpClient http, ICredentialStore credentials)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private string? _token;
    private DateTimeOffset _expiresAt;
    private long _generation;
    private long _revision;
    private Task<bool>? _refresh;
    private string _server = "https://10.37.200.74";
    public string Server { get { lock (_sync) return _server; } }
    public long Generation { get { lock (_sync) return _generation; } }
    public User? CurrentUser { get; private set; }
    public bool IsAuthenticated { get { lock (_sync) return _token is not null; } }
    public event Action? Invalidated;

    public void SetServer(string server)
    {
        if (!Uri.TryCreate(server.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Scheme == "http" && !uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("请输入有效的平台 HTTP 或 HTTPS 根地址。");
        lock (_sync)
        {
            if (_token is not null && _server != uri.GetLeftPart(UriPartial.Authority)) throw new InvalidOperationException("请先退出登录再切换平台。");
            _server = uri.GetLeftPart(UriPartial.Authority);
        }
    }

    public async Task LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new ArgumentException("请输入账号和密码。");
        long generation;
        lock (_sync) generation = ++_generation;
        var response = await SendRawAsync((client, ct) => client.LoginAsync(new Generated.LoginRequest
        {
            Username = username.Trim(), Password = password, ClientType = "desktop", ClientVersion = UpdateService.CurrentVersion.ToString(3)
        }, cancellationToken: ct), null, cancellationToken);
        var login = ClientModelMapping.From(response);
        lock (_sync)
        {
            if (generation != _generation) throw new OperationCanceledException("本次登录已取消。");
            Apply(login);
        }
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var saved = credentials.Load();
        if (saved is null) return false;
        if (saved.ExpiresAt <= DateTimeOffset.UtcNow) { credentials.Clear(); return false; }
        lock (_sync)
        {
            if (!string.Equals(saved.Server, _server, StringComparison.OrdinalIgnoreCase)) return false;
            _token = saved.AccessToken;
            _expiresAt = saved.ExpiresAt;
            ++_generation;
        }
        try { await ReloadUserAsync(cancellationToken); return true; }
        catch { await LogoutAsync(cancellationToken); throw; }
    }

    public async Task<User> ReloadUserAsync(CancellationToken cancellationToken = default)
    {
        var generation = Generation;
        var response = await SendAuthorizedAsync((client, ct) => client.GetCurrentUserAsync(ct), cancellationToken);
        var user = ClientModelMapping.From(response);
        lock (_sync)
        {
            if (_generation != generation || _token is null) throw new OperationCanceledException("登录状态已改变。");
            CurrentUser = user;
        }
        return user;
    }

    public async Task<string?> GetTokenAsync()
    {
        if (!IsAuthenticated) return null;
        if (_expiresAt <= DateTimeOffset.UtcNow.AddMinutes(2)) await RefreshAsync();
        lock (_sync) return _token;
    }

    public Task<bool> RefreshAsync()
    {
        lock (_sync)
        {
            if (_token is null) return Task.FromResult(false);
            if (_refresh is { IsCompleted: false }) return _refresh;
            // 完成源先进入共享字段，阻止同步完成的响应绕过单飞保护。
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _refresh = completion.Task;
            _ = RefreshCoreAsync(_token, _generation, completion);
            return completion.Task;
        }
    }

    private async Task RefreshCoreAsync(string token, long generation, TaskCompletionSource<bool> completion)
    {
        try
        {
            var response = await SendRawAsync((client, ct) => client.RefreshSessionAsync(cancellationToken: ct), token, CancellationToken.None).ConfigureAwait(false);
            var login = ClientModelMapping.From(response);
            lock (_sync)
            {
                if (_generation != generation || _token != token) { completion.TrySetResult(false); return; }
                Apply(login);
            }
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            if (ex is PlatformException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }) Invalidate(generation);
            completion.TrySetException(ex);
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        string? token;
        lock (_sync)
        {
            token = _token;
            ++_generation;
            _token = null;
            _refresh = null;
            CurrentUser = null;
            credentials.Clear();
        }
        if (token is null) return;
        try
        {
            await SendRawAsync(async (client, ct) => { await client.LogoutAsync(cancellationToken: ct); return true; }, token, cancellationToken);
        }
        catch (Exception ex) { ClientFiles.Log($"服务器退出会话失败：{ex.Message}"); }
    }

    public async Task ClearMediaSessionsAsync(string kind = "live", bool all = true, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync();
        if (token is null) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Server}/api/v2/{kind}-sessions" + (all ? "?all=true" : ""));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            ClientFiles.Log($"清理{kind}会话失败：{ex.Message}");
        }
    }

    internal async Task<T> SendAuthorizedAsync<T>(Func<Generated.IVideoPlatformClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var generation = Generation;
        var token = await GetTokenAsync() ?? throw new PlatformException("登录已失效，请重新登录。", HttpStatusCode.Unauthorized);
        long revision;
        lock (_sync) revision = _revision;
        T response;
        try
        {
            try { response = await SendRawAsync(operation, token, cancellationToken); }
            catch (PlatformException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && generation == Generation)
            {
                if (revision == _revision) await RefreshAsync();
                string? current;
                lock (_sync) current = _token;
                if (generation != Generation || current is null) throw new OperationCanceledException("登录状态已改变。");
                response = await SendRawAsync(operation, current, cancellationToken);
            }
        }
        catch (PlatformException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Invalidate(generation); throw;
        }
        if (generation != Generation)
        {
            (response as IDisposable)?.Dispose();
            throw new OperationCanceledException("登录状态已改变，已忽略迟到响应。");
        }
        return response;
    }

    internal Task<T> SendPublicAsync<T>(Func<Generated.IVideoPlatformClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => SendRawAsync(operation, null, cancellationToken);

    private async Task<T> SendRawAsync<T>(Func<Generated.IVideoPlatformClient, CancellationToken, Task<T>> operation, string? token, CancellationToken cancellationToken)
    {
        using var clientHttp = new HttpClient(new GeneratedClientTransport(http, token))
        {
            BaseAddress = new Uri(Server + "/"), Timeout = Timeout.InfiniteTimeSpan
        };
        try { return await operation(new Generated.VideoPlatformClient(clientHttp), cancellationToken).ConfigureAwait(false); }
        catch (Generated.ApiException ex) { throw TranslateError(ex); }
    }
    private void Apply(LoginResponse login)
    {
        if (string.IsNullOrWhiteSpace(login.AccessToken)) throw new InvalidDataException("服务器未返回桌面端访问令牌。");
        credentials.Save(new SavedSession(_server, login.AccessToken, login.ExpiresAt));
        _token = login.AccessToken;
        ++_revision;
        _expiresAt = login.ExpiresAt;
        CurrentUser = login.User;
    }
    private void Invalidate(long generation)
    {
        lock (_sync)
        {
            if (_generation != generation) return;
            ++_generation;
            _token = null;
            CurrentUser = null;
            credentials.Clear();
        }
        Invalidated?.Invoke();
    }
    private static PlatformException TranslateError(Generated.ApiException exception)
    {
        ApiError? error = null;
        if (exception is Generated.ApiException<Generated.ErrorResponse> { Result: { } result })
            error = new(result.Code, result.Message, result.TraceId);
        else if (!string.IsNullOrWhiteSpace(exception.Response))
        {
            try { error = JsonSerializer.Deserialize<ApiError>(exception.Response, Json); }
            catch (JsonException) { }
        }
        var statusCode = (HttpStatusCode)exception.StatusCode;
        var message = error?.Message ?? statusCode switch
        {
            HttpStatusCode.Unauthorized => "登录已失效，请重新登录。",
            HttpStatusCode.Forbidden => "当前账号无权执行此操作。",
            HttpStatusCode.Conflict => "资源正在被使用，请稍后重试。",
            HttpStatusCode.TooManyRequests => "已达到服务端配额，请停止部分会话后重试。",
            _ when exception.StatusCode is >= 200 and < 300 => "平台响应不符合接口契约。",
            _ => $"平台请求失败，HTTP 状态码 {exception.StatusCode}。"
        };
        return new PlatformException(message, statusCode, error?.Code, error?.TraceId);
    }
}
