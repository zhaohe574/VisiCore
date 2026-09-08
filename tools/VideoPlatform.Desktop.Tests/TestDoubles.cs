using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using LibVLCSharp.Shared;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.Tests;

internal sealed class MemoryCredentials : ICredentialStore
{
    public SavedSession? Value;
    public SavedSession? Load() => Value;
    public void Save(SavedSession session) => Value = session;
    public void Clear() => Value = null;
}
internal sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    public static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    public static HttpResponseMessage Empty() => new(HttpStatusCode.NoContent);
}
internal sealed class InlineDispatcher : IUiDispatcher { public Task InvokeAsync(Func<Task> action) => action(); }
internal sealed class FakeDialogs : IUserInteraction
{
    public string? SaveFile(string suggestedName, string filter) => null;
    public bool Confirm(string message) => true;
    public void Shutdown() { }
}
internal sealed class FakePlayerFactory : IPlayerFactory
{
    public List<FakePlayer> Players { get; } = [];
    public IVideoPlayer Create() { var player = new FakePlayer(); Players.Add(player); return player; }
}
internal sealed class FakePlayer : IVideoPlayer
{
    public string? Url;
    public bool Disposed;
    public bool Paused;
    public MediaPlayer? NativePlayer => null;
    public bool Muted { get; set; } = true;
    public event Action<string>? Failed;
    public event Action? Connected;
    public void Fail() => Failed?.Invoke("模拟连接中断");
    public Task PlayAsync(string url, CancellationToken cancellationToken = default) { Url = url; Connected?.Invoke(); return Task.CompletedTask; }
    public Task StopAsync() => Task.CompletedTask;
    public void Pause(bool paused) => Paused = paused;
    public bool Capture(string path) => false;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
internal sealed class FakeApi : IPlatformApi
{
    public ConcurrentQueue<(HttpMethod Method, string Path, object? Body)> Calls { get; } = new();
    public Dictionary<string, object?> GetResults { get; } = [];
    public Func<string, object?, Task<object>>? Post;
    public Func<HttpMethod, string, object?, Task>? Send;
    public Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((HttpMethod.Get, path, null));
        return Task.FromResult(GetResults.TryGetValue(path, out var value) ? (T?)value : default);
    }
    public async Task<T> PostAsync<T>(string path, object? body = null, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((HttpMethod.Post, path, body));
        return (T)(Post is null ? Fixtures.Media(body is LiveRequest live ? live.ChannelId : ((PlaybackRequest)body!).ChannelId) : await Post(path, body));
    }
    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((method, path, body));
        if (Send is not null) await Send(method, path, body);
    }
    public Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
    public Task DownloadAsync(string path, string destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
internal static class Fixtures
{
    public static User User(params string[] permissions) => new(1, "tester", "测试值守员", "", "active", permissions, [1]);
    public static Channel Channel(long id = 101, long device = 1) => new(id, device, $"录像机 {device}", 1, $"门区 {id}", null, "测试设备", "online", null, true, "H265");
    public static MediaSession Media(long id = 101) => new(Guid.NewGuid().ToString(), id, 2, "playing", DateTimeOffset.UtcNow.AddMinutes(2), $"rtsp://local/device-{id}", $"https://platform.test/media/device-{id}.flv");
    public static LoginResponse Login(string token = "stable-token") => new(User("live.view", "channel.read"), token, DateTimeOffset.UtcNow.AddHours(8));
}
