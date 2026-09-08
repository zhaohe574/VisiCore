using System.Net;
using System.Net.Http;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task ConcurrentRefreshUsesSingleRequestWithStableToken()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("refresh")) { Interlocked.Increment(ref refreshes); entered.TrySetResult(); await release.Task; }
            return StubHandler.Ok(Fixtures.Login());
        }));
        var service = new SessionService(http, new MemoryCredentials());
        await service.LoginAsync("tester", "test-password");
        var first = service.RefreshAsync(); await entered.Task;
        var requests = Enumerable.Range(0, 30).Select(_ => service.RefreshAsync()).Append(first).ToArray();
        release.SetResult();
        Assert.All(await Task.WhenAll(requests), Assert.True);
        Assert.Equal(1, refreshes);
        Assert.Equal("stable-token", await service.GetTokenAsync());
    }

    [Fact]
    public async Task LogoutDiscardsLateRefreshAndEncryptedCredentialWrite()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new StubHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("logout")) return StubHandler.Empty();
            if (path.EndsWith("refresh")) { entered.SetResult(); await release.Task; }
            return StubHandler.Ok(Fixtures.Login());
        }));
        var credentials = new MemoryCredentials();
        var service = new SessionService(http, credentials);
        await service.LoginAsync("tester", "test-password");
        var refreshing = service.RefreshAsync(); await entered.Task;
        await service.LogoutAsync(); release.SetResult();
        Assert.False(await refreshing);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.Null(credentials.Value);
    }

    [Fact]
    public async Task LateLoginCannotRestoreLoggedOutAccount()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new StubHandler(async _ => { entered.SetResult(); await release.Task; return StubHandler.Ok(Fixtures.Login()); }));
        var store = new MemoryCredentials(); var service = new SessionService(http, store);
        var login = service.LoginAsync("tester", "test-password"); await entered.Task;
        await service.LogoutAsync(); release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => login);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task ManyUnauthorizedRequestsShareOneRefreshEvenWhenTokenDoesNotRotate()
    {
        const int count = 16;
        var allRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = 0; var refreshed = false; var refreshes = 0;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login")) return StubHandler.Ok(Fixtures.Login());
            if (path.EndsWith("refresh"))
            {
                Interlocked.Increment(ref refreshes); refreshStarted.TrySetResult(); await completeRefresh.Task; refreshed = true; return StubHandler.Ok(Fixtures.Login());
            }
            if (!refreshed)
            {
                if (Interlocked.Increment(ref requested) == count) allRequested.SetResult();
                await allRequested.Task;
                return new(HttpStatusCode.Unauthorized) { Content = System.Net.Http.Json.JsonContent.Create(new ApiError("auth.expired", "会话需要续期", "test")) };
            }
            return StubHandler.Ok(Fixtures.User("live.view"));
        }));
        var service = new SessionService(http, new MemoryCredentials()); await service.LoginAsync("tester", "test-password");
        var api = new PlatformApi(service);
        var requests = Enumerable.Range(0, count).Select(_ => api.GetAsync<User>("auth/me")).ToArray();
        await allRequested.Task; await refreshStarted.Task; completeRefresh.SetResult();
        Assert.All(await Task.WhenAll(requests), user => Assert.Equal(1, user!.Id));
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task RevokedRefreshClearsSessionAndNotifiesShell()
    {
        using var http = new HttpClient(new StubHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login")
            ? StubHandler.Ok(Fixtures.Login()) : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = System.Net.Http.Json.JsonContent.Create(new ApiError("auth.revoked", "会话已撤销", "test")) })));
        var store = new MemoryCredentials(); var service = new SessionService(http, store); var invalidations = 0;
        service.Invalidated += () => ++invalidations;
        await service.LoginAsync("tester", "test-password");
        await Assert.ThrowsAsync<PlatformException>(() => service.RefreshAsync());
        Assert.False(service.IsAuthenticated); Assert.Null(store.Value); Assert.Equal(1, invalidations);
    }

    [Fact]
    public async Task PlatformErrorPreservesChineseMessageAndTraceIdentifier()
    {
        using var http = new HttpClient(new StubHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login")
            ? StubHandler.Ok(Fixtures.Login()) : new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = System.Net.Http.Json.JsonContent.Create(new ApiError("media.quota", "已达到媒体配额", "trace-123")) })));
        var service = new SessionService(http, new MemoryCredentials()); await service.LoginAsync("tester", "test-password");
        var error = await Assert.ThrowsAsync<PlatformException>(() => new PlatformApi(service).GetAsync<User>("auth/me"));
        Assert.Equal("media.quota", error.Code); Assert.Equal("trace-123", error.TraceId); Assert.Equal("已达到媒体配额", error.Message);
    }
}
