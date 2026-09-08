using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.LoadTests;

public sealed record SimulatedSession(Guid Id, long DeviceId, int Channel, string Kind, string Stream, int StreamType, string Profile,
    DateTimeOffset? Start = null, DateTimeOffset? End = null);

public sealed class SimulatedAdapter(string key) : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SimulatedSession> _sessions = new();
    private readonly Dictionary<string, int> _upstreams = new();
    private readonly ConcurrentDictionary<string, bool> _viewers = new();
    private int _liveStarts;
    private int _peakLive;
    private int _peakPlayback;
    public string Url { get; private set; } = "";
    public Guid BootId { get; } = Guid.NewGuid();
    public void RegisterViewer(string id) => _viewers[id] = true;

    public object Snapshot()
    {
        lock (_gate) return new
        {
            simulated = true, liveSessions = _sessions.Values.Count(s => s.Kind == "live"), liveUpstreams = _upstreams.Count,
            liveUpstreamStarts = _liveStarts, peakLiveSessions = _peakLive, playbackSessions = _sessions.Values.Count(s => s.Kind == "playback"),
            peakPlaybackSessions = _peakPlayback, playbackUpstreams = _sessions.Values.Where(s => s.Kind == "playback").Select(s => s.Stream).Distinct().Count(), viewers = _viewers.Count
        };
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/internal") && context.Request.Headers["X-Adapter-Key"] != key)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { code = "sim.key", message = "模拟适配器内部密钥不匹配。" }, ct);
                return;
            }
            await next(context);
        });
        _app.MapGet("/health", () => new { simulated = true, status = "ok", bootId = BootId });
        _app.MapPut("/internal/devices/{deviceId:long}", () => Results.NoContent());
        _app.MapPost("/internal/devices/{deviceId:long}/live", (long deviceId, JsonObject body) =>
        {
            var id = Guid.Parse(body.Text("sessionId"));
            var channel = (int)body.Id("channel");
            var streamType = (int)body.Id("streamType");
            var profile = body.Text("profile");
            var stream = $"vp2_d{deviceId}_c{channel}_s{streamType}_{profile}";
            lock (_gate)
            {
                if (_sessions.TryGetValue(id, out var existing)) return Results.Ok(Summary(existing));
                if (!_upstreams.ContainsKey(stream)) { _upstreams[stream] = 0; _liveStarts++; }
                _upstreams[stream]++;
                var session = new SimulatedSession(id, deviceId, channel, "live", stream, streamType, profile);
                _sessions.Add(id, session);
                _peakLive = Math.Max(_peakLive, _sessions.Values.Count(s => s.Kind == "live"));
                return Results.Ok(Summary(session));
            }
        });
        _app.MapPost("/internal/devices/{deviceId:long}/playback", (long deviceId, JsonObject body) =>
        {
            var id = Guid.Parse(body.Text("sessionId"));
            lock (_gate)
            {
                if (_sessions.TryGetValue(id, out var existing)) return Results.Ok(Summary(existing));
                var session = new SimulatedSession(id, deviceId, (int)body.Id("channel"), "playback", $"vp2_playback_{id:N}", 1, body.Text("profile"), body.Time("start"), body.Time("end"));
                _sessions.Add(id, session);
                _peakPlayback = Math.Max(_peakPlayback, _sessions.Values.Count(s => s.Kind == "playback"));
                return Results.Ok(Summary(session));
            }
        });
        _app.MapGet("/internal/devices/{deviceId:long}/playback/{id:guid}", (long deviceId, Guid id) =>
        {
            lock (_gate) return _sessions.TryGetValue(id, out var session) && session.DeviceId == deviceId && session.Kind == "playback"
                ? Results.Ok(Summary(session)) : Results.NotFound(new { code = "sim.missing", message = "模拟回放不存在。" });
        });
        foreach (var kind in new[] { "live", "playback" })
            _app.MapDelete($"/internal/devices/{{deviceId:long}}/{kind}/{{id:guid}}", (long deviceId, Guid id) =>
            {
                lock (_gate)
                {
                    if (_sessions.TryGetValue(id, out var session) && session.DeviceId == deviceId && session.Kind == kind)
                    {
                        _sessions.Remove(id);
                        if (kind == "live" && --_upstreams[session.Stream] == 0) _upstreams.Remove(session.Stream);
                    }
                }
                return Results.NoContent();
            });
        _app.MapGet("/internal/sessions", () =>
        {
            lock (_gate) return new
            {
                bootId = BootId, devices = new[] { new { id = 1, deviceId = 1, enabled = true } },
                live = _sessions.Values.Where(s => s.Kind == "live").Select(s => new { id = s.Id, sessionId = s.Id, s.DeviceId, s.Channel, s.Stream, state = "playing" }).ToArray(),
                playback = _sessions.Values.Where(s => s.Kind == "playback").Select(s => new { id = s.Id, sessionId = s.Id, s.DeviceId, s.Channel, s.Stream, state = "playing" }).ToArray(), exports = Array.Empty<object>()
            };
        });
        _app.MapPost("/index/api/kick_session", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            if (form["secret"] != key) return Results.Ok(new { code = -1 });
            _viewers.TryRemove(form["id"].ToString(), out _);
            return Results.Ok(new { code = 0 });
        });
        _app.MapPost("/index/api/getAllSession", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return Results.Ok(new { code = form["secret"] == key ? 0 : -1, data = _viewers.Keys.Select(id => new { id }).ToArray() });
        });
        await _app.StartAsync(ct);
        Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    private static object Summary(SimulatedSession session) => new
    {
        id = session.Id, stream = session.Stream, state = "playing", streamType = session.StreamType, codec = "h264", transcoded = false,
        start = session.Start, end = session.End, currentTime = session.Start, progress = 0, speed = 1,
        segments = session.Start is null ? Array.Empty<object>() : new object[] { new { start = session.Start, end = session.End } }
    };

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
    }
}
