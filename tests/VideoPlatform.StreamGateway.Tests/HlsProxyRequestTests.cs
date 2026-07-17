using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using VideoPlatform.Core;
using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class HlsProxyRequestTests
{
    [Fact(DisplayName = "代理路径会严格解析实时流会话、票据和资源")]
    public void ValidLivePathIsParsed()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);

        var parsed = HlsProxyRequest.TryParse(
            $"/hls/{sessionId:N}/{ticket}/live/{cameraId:N}/sub/index.m3u8",
            out var request);

        Assert.True(parsed);
        Assert.Equal(sessionId, request.SessionId);
        Assert.Equal(ticket, request.Ticket);
        Assert.Equal($"live/{cameraId:N}/sub", request.StreamKey);
        Assert.Equal("index.m3u8", request.ResourcePath);
    }

    [Fact(DisplayName = "代理路径拒绝回放会话不匹配、非法票据和目录穿越")]
    public void InvalidPathsAreRejected()
    {
        var sessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var ticket = new string('a', 43);

        Assert.False(HlsProxyRequest.TryParse(
            $"/hls/{sessionId:N}/{ticket}/playback/{otherSessionId:N}/index.m3u8",
            out _));
        Assert.False(HlsProxyRequest.TryParse(
            $"/hls/{sessionId:N}/not.valid/live/{Guid.NewGuid():N}/sub/index.m3u8",
            out _));
        Assert.False(HlsProxyRequest.TryParse(
            $"/hls/{sessionId:N}/{ticket}/live/{Guid.NewGuid():N}/sub/../secret.m3u8",
            out _));
        Assert.False(HlsProxyRequest.TryParse(
            $"/hls/{sessionId:N}/{ticket}/live/{Guid.NewGuid():N}/main/%2e%2e/%2e%2e/internal/live-source/index.m3u8",
            out _));
    }

    [Fact(DisplayName = "撤销后代理会拒绝下一次 HLS 请求且不再访问上游")]
    public async Task RevocationRejectsNextRequestBeforeUpstream()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var consumeCount = 0;
        var upstreamCount = 0;
        var options = new GatewayOptions
        {
            GatewayName = "integration",
            CenterBaseUri = "https://center.test/",
            CenterControlToken = "center-control-token-at-least-32-bytes",
            DeviceWorkerAccessToken = "device-worker-token-at-least-32-bytes",
            CommandToken = "gateway-command-token-at-least-32-bytes"
        };
        var controlClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(_ =>
            {
                consumeCount++;
                if (consumeCount > 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new GatewayStreamSession(
                        sessionId,
                        streamKey,
                        cameraId,
                        CameraPermission.LiveView,
                        "sub",
                        DateTimeOffset.UtcNow.AddMinutes(2),
                        true,
                        null))
                };
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(streamKey, "fingerprint");
        var authorizationStore = new StreamAuthorizationStore(controlClient, registry);
        var hlsClient = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            upstreamCount++;
            Assert.Equal($"/{streamKey}/index.m3u8?part=3", request.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("#EXTM3U")
            };
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8888/")
        };
        var proxy = new HlsProxyService(hlsClient, authorizationStore, new MediaMtxOptions());
        var path = $"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8";

        var first = CreateContext(path, "?part=3");
        await proxy.ProxyAsync(first);
        Assert.Equal(StatusCodes.Status200OK, first.Response.StatusCode);
        Assert.Equal("#EXTM3U", Encoding.UTF8.GetString(((MemoryStream)first.Response.Body).ToArray()));
        Assert.Equal(1, upstreamCount);

        await authorizationStore.RevokeAsync(sessionId, CancellationToken.None);

        var afterRevocation = CreateContext(path, "?part=3");
        await proxy.ProxyAsync(afterRevocation);
        Assert.Equal(StatusCodes.Status401Unauthorized, afterRevocation.Response.StatusCode);
        Assert.Equal(1, upstreamCount);
    }

    [Fact(DisplayName = "同一 IP 的错误票据不能复用已建立的 HLS 授权缓存")]
    public async Task WrongTicketFromSameIpDoesNotReuseAuthorizationCache()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var validTicket = new string('v', 43);
        var wrongTicket = new string('x', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var consumeCount = 0;
        var upstreamCount = 0;
        var options = new GatewayOptions
        {
            GatewayName = "integration",
            CenterBaseUri = "https://center.test/",
            CenterControlToken = "center-control-token-at-least-32-bytes",
            DeviceWorkerAccessToken = "device-worker-token-at-least-32-bytes",
            CommandToken = "gateway-command-token-at-least-32-bytes"
        };
        var controlClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(_ =>
            {
                consumeCount++;
                return consumeCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new GatewayStreamSession(
                            sessionId,
                            streamKey,
                            cameraId,
                            CameraPermission.LiveView,
                            "sub",
                            DateTimeOffset.UtcNow.AddMinutes(2),
                            true,
                            null))
                    }
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(streamKey, "fingerprint");
        var authorizationStore = new StreamAuthorizationStore(controlClient, registry);
        var hlsClient = new HttpClient(new DelegateHttpMessageHandler(_ =>
        {
            upstreamCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("#EXTM3U")
            };
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8888/")
        };
        var proxy = new HlsProxyService(hlsClient, authorizationStore, new MediaMtxOptions());

        var valid = CreateContext(
            $"/hls/{sessionId:N}/{validTicket}/{streamKey}/index.m3u8",
            string.Empty);
        await proxy.ProxyAsync(valid);
        Assert.Equal(StatusCodes.Status200OK, valid.Response.StatusCode);

        var forged = CreateContext(
            $"/hls/{sessionId:N}/{wrongTicket}/{streamKey}/index.m3u8",
            string.Empty);
        await proxy.ProxyAsync(forged);

        Assert.Equal(StatusCodes.Status401Unauthorized, forged.Response.StatusCode);
        Assert.Equal(2, consumeCount);
        Assert.Equal(1, upstreamCount);
    }

    [Fact(DisplayName = "首次 HLS 播放列表生成前的短暂 404 会在网关内重试")]
    public async Task InitialPlaylistNotFoundIsRetried()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var upstreamCount = 0;
        var options = new GatewayOptions
        {
            GatewayName = "integration",
            CenterBaseUri = "https://center.test/",
            CenterControlToken = "center-control-token-at-least-32-bytes",
            DeviceWorkerAccessToken = "device-worker-token-at-least-32-bytes",
            CommandToken = "gateway-command-token-at-least-32-bytes"
        };
        var controlClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new GatewayStreamSession(
                    sessionId,
                    streamKey,
                    cameraId,
                    CameraPermission.LiveView,
                    "sub",
                    DateTimeOffset.UtcNow.AddMinutes(2),
                    true,
                    null))
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(streamKey, "fingerprint");
        var authorizationStore = new StreamAuthorizationStore(controlClient, registry);
        var hlsClient = new HttpClient(new DelegateHttpMessageHandler(_ =>
        {
            upstreamCount++;
            return upstreamCount == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U") };
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8888/")
        };
        var proxy = new HlsProxyService(hlsClient, authorizationStore, new MediaMtxOptions
        {
            PlaylistStartupRetrySeconds = 1,
            PlaylistStartupRetryMilliseconds = 50
        });
        var path = $"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8";

        var context = CreateContext(path, string.Empty);
        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("#EXTM3U", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.Equal(2, upstreamCount);
    }

    [Fact(DisplayName = "首次 HLS 清单的上游超时会在启动窗口内重试")]
    public async Task InitialPlaylistTimeoutIsRetried()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/main";
        var upstreamCount = 0;
        var proxy = CreateAuthorizedProxy(sessionId, streamKey, _ =>
        {
            upstreamCount++;
            if (upstreamCount == 1)
            {
                throw new TaskCanceledException("模拟首个 HLS 清单等待超时。");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U") };
        }, new MediaMtxOptions
        {
            PlaylistStartupRetrySeconds = 1,
            PlaylistStartupRetryMilliseconds = 50
        });
        var context = CreateContext($"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8", string.Empty);

        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(2, upstreamCount);
    }

    [Fact(DisplayName = "首次 HLS 清单持续超时会返回网关超时")]
    public async Task InitialPlaylistTimeoutReturnsGatewayTimeout()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/main";
        var proxy = CreateAuthorizedProxy(sessionId, streamKey, _ =>
            throw new TaskCanceledException("模拟 HLS 清单持续超时。"), new MediaMtxOptions
            {
                PlaylistStartupRetrySeconds = 1,
                PlaylistStartupRetryMilliseconds = 50
            });
        var context = CreateContext($"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8", string.Empty);

        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter);
    }

    [Fact(DisplayName = "MediaMTX 同源 cookieCheck 重定向会保留内部 Basic 凭据")]
    public async Task SafeCookieRedirectPreservesInternalAuthorization()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var requestCount = 0;
        var internalAuthorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "hlsproxy:internal-hls-password-at-least-32-bytes")));
        var proxy = CreateAuthorizedProxy(
            sessionId,
            streamKey,
            request =>
            {
                requestCount++;
                Assert.Equal(internalAuthorization.Scheme, request.Headers.Authorization?.Scheme);
                Assert.Equal(internalAuthorization.Parameter, request.Headers.Authorization?.Parameter);
                if (requestCount == 1)
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri(
                        $"/{streamKey}/index.m3u8?cookieCheck=1",
                        UriKind.Relative);
                    redirect.Headers.TryAddWithoutValidation("Set-Cookie", "cookieCheck=1");
                    return redirect;
                }

                Assert.Equal($"/{streamKey}/index.m3u8?cookieCheck=1", request.RequestUri!.PathAndQuery);
                Assert.False(request.Headers.Contains("Cookie"));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("#EXTM3U")
                };
                response.Headers.TryAddWithoutValidation("Set-Cookie", "hlsSession=internal-secret");
                return response;
            },
            new MediaMtxOptions(),
            client => client.DefaultRequestHeaders.Authorization = internalAuthorization);
        var context = CreateContext(
            $"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8",
            string.Empty);
        context.Request.Headers.Authorization = "Basic external-client-credential";

        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(2, requestCount);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact(DisplayName = "HLS 上游重定向不得把内部凭据发送到其他来源")]
    public async Task CrossOriginRedirectIsRejected()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var requestCount = 0;
        var proxy = CreateAuthorizedProxy(sessionId, streamKey, _ =>
        {
            requestCount++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("http://example.test/stolen/index.m3u8");
            return redirect;
        }, new MediaMtxOptions());
        var context = CreateContext(
            $"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8",
            string.Empty);

        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Equal(1, requestCount);
    }

    [Fact(DisplayName = "MediaMTX 内部鉴权失败不会伪装成用户票据 401")]
    public async Task UpstreamAuthenticationFailureBecomesBadGateway()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var proxy = CreateAuthorizedProxy(
            sessionId,
            streamKey,
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new MediaMtxOptions());
        var context = CreateContext(
            $"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8",
            string.Empty);

        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
    }

    [Fact(DisplayName = "HLS 上游循环重定向会在一次跟随后返回 502")]
    public async Task RedirectLoopIsBounded()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var requestCount = 0;
        var proxy = CreateAuthorizedProxy(sessionId, streamKey, _ =>
        {
            requestCount++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri(
                $"/{streamKey}/index.m3u8?cookieCheck=1",
                UriKind.Relative);
            return redirect;
        }, new MediaMtxOptions());
        var context = CreateContext(
            $"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8",
            string.Empty);

        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Equal(2, requestCount);
    }

    [Fact(DisplayName = "首次 HLS 播放列表在等待窗口结束后会返回 404")]
    public async Task InitialPlaylistNotFoundTimesOut()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        var upstreamCount = 0;
        var proxy = CreateAuthorizedProxy(sessionId, streamKey, _ =>
        {
            upstreamCount++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, new MediaMtxOptions
        {
            PlaylistStartupRetrySeconds = 1,
            PlaylistStartupRetryMilliseconds = 50
        });

        var stopwatch = Stopwatch.StartNew();
        var context = CreateContext($"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8", string.Empty);
        await proxy.ProxyAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900));
        Assert.True(upstreamCount > 1);
    }

    [Fact(DisplayName = "取消首次 HLS 播放列表等待不会继续访问上游")]
    public async Task InitialPlaylistRetryHonorsRequestCancellation()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var ticket = new string('a', 43);
        var streamKey = $"live/{cameraId:N}/sub";
        using var cancellation = new CancellationTokenSource();
        var upstreamCount = 0;
        var proxy = CreateAuthorizedProxy(sessionId, streamKey, _ =>
        {
            upstreamCount++;
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, new MediaMtxOptions
        {
            PlaylistStartupRetrySeconds = 1,
            PlaylistStartupRetryMilliseconds = 50
        });
        var context = CreateContext($"/hls/{sessionId:N}/{ticket}/{streamKey}/index.m3u8", string.Empty);
        context.RequestAborted = cancellation.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proxy.ProxyAsync(context));
        Assert.Equal(1, upstreamCount);
    }

    private static HlsProxyService CreateAuthorizedProxy(
        Guid sessionId,
        string streamKey,
        Func<HttpRequestMessage, HttpResponseMessage> upstreamResponse,
        MediaMtxOptions mediaMtxOptions,
        Action<HttpClient>? configureClient = null)
    {
        var options = new GatewayOptions
        {
            GatewayName = "integration",
            CenterBaseUri = "https://center.test/",
            CenterControlToken = "center-control-token-at-least-32-bytes",
            DeviceWorkerAccessToken = "device-worker-token-at-least-32-bytes",
            CommandToken = "gateway-command-token-at-least-32-bytes"
        };
        var controlClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new GatewayStreamSession(
                    sessionId,
                    streamKey,
                    Guid.NewGuid(),
                    CameraPermission.LiveView,
                    "sub",
                    DateTimeOffset.UtcNow.AddMinutes(2),
                    true,
                    null))
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(streamKey, "fingerprint");
        var authorizationStore = new StreamAuthorizationStore(controlClient, registry);
        var hlsClient = new HttpClient(new DelegateHttpMessageHandler(upstreamResponse))
        {
            BaseAddress = new Uri("http://127.0.0.1:8888/")
        };
        configureClient?.Invoke(hlsClient);
        return new HlsProxyService(hlsClient, authorizationStore, mediaMtxOptions);
    }

    private static DefaultHttpContext CreateContext(string path, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
