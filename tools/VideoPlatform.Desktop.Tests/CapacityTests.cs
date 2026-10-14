using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using LibVLCSharp.Shared;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using VideoPlatform.Desktop.Views;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class CapacityFactAttribute : FactAttribute
{
    public CapacityFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VP_CAPACITY_MODE") is not ("live" or "playback")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VP_CANDIDATE_PASSWORD")))
            Skip = "需要显式选择容量模式并提供实机登录凭据。";
    }
}

[Collection(nameof(WpfTestCollection))]
public sealed class CapacityTests(WpfTestHost host)
{
    [CapacityFact]
    [Trait("Category", "Capacity")]
    public Task MultipleWindowsDecodeAndRecordCapacityEvidence()
    {
        var seconds = Duration();
        return host.RunAsync(() => VerifyAsync(seconds), TimeSpan.FromSeconds(seconds + 900));
    }

    private static int Duration() => int.TryParse(Environment.GetEnvironmentVariable("VP_CAPACITY_SECONDS"), out var seconds) && seconds > 0
        ? seconds : Environment.GetEnvironmentVariable("VP_CAPACITY_MODE") == "playback" ? 7200 : 86400;

    private static async Task VerifyAsync(int seconds)
    {
        var playback = Environment.GetEnvironmentVariable("VP_CAPACITY_MODE") == "playback";
        var expected = playback ? 4 : 25;
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("VP_CAPACITY_OUTPUT")
            ?? Path.Combine(AppContext.BaseDirectory, "../../../capacity-evidence", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")));
        Directory.CreateDirectory(directory);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true };
        using var http = new HttpClient(TransportPolicy.CreateHandler(Environment.GetEnvironmentVariable("VP_CANDIDATE_SOURCE_ADDRESS"))) { Timeout = TimeSpan.FromSeconds(60) };
        var session = new SessionService(http, new MemoryCredentials());
        using var players = new VlcPlayerFactory();
        var workspaces = new List<WorkspaceViewModel>();
        var windows = new List<Window>();
        var firstFrames = new List<object>();
        var positions = new Dictionary<Guid, long>();
        var responses = new List<double>();
        var samples = new List<(double Seconds, long Bytes, int Handles)>();
        var timer = new Stopwatch();
        Exception? failure = null;
        PtzService? ptz = null;
        var completed = false;
        try
        {
            session.SetServer(Environment.GetEnvironmentVariable("VP_CANDIDATE_URL") ?? "https://10.37.200.74:8443");
            await session.LoginAsync(Environment.GetEnvironmentVariable("VP_CANDIDATE_USER") ?? "admin", Environment.GetEnvironmentVariable("VP_CANDIDATE_PASSWORD")!);
            var api = new PlatformApi(session);
            ptz = new PtzService(api);
            var primary = new WorkspaceViewModel(api, players, ptz, new UiDispatcher(), new FakeDialogs());
            workspaces.Add(primary);
            await primary.SetAccessAsync(session.CurrentUser); await primary.RefreshAsync();
            var channels = primary.Channels.Values.Where(c => c.Online).OrderBy(c => c.Id).ToArray();
            var selected = new List<(Channel Channel, DateTimeOffset Start, DateTimeOffset End)>();
            foreach (var channel in channels)
            {
                if (!playback) selected.Add((channel, default, default));
                else
                {
                    var recordings = await api.PostAsync<Recording[]>("recordings/search", new RecordingRequest(channel.Id, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddMinutes(-5)));
                    // 使用真实录像索引，禁止用不存在的时间段替代容量验收。
                    var recording = recordings.OrderByDescending(r => r.End - r.Start).FirstOrDefault(r => r.End - r.Start >= TimeSpan.FromMinutes(5));
                    if (recording is not null) selected.Add((channel, recording.Start, recording.End));
                }
                if (selected.Count == expected) break;
            }
            Assert.Equal(expected, selected.Count);
            var counts = playback ? new[] { 2, 2 } : new[] { 16, 9 };
            var offset = 0;
            for (var index = 0; index < counts.Length; index++)
            {
                var workspace = index == 0 ? primary : primary.CreateSibling();
                if (index > 0) { workspaces.Add(workspace); await workspace.SetAccessAsync(session.CurrentUser); await workspace.RefreshAsync(); }
                await workspace.SetModeAsync(playback);
                await workspace.SetLayoutCommand.ExecuteAsync(playback ? "4" : counts[index].ToString());
                var window = new Window { Title = $"VisiCore 容量验收 · {(playback ? "回放" : "实时")}工作区 {index + 1}", Width = 960, Height = 720,
                    Left = index * 960, Top = 0, Content = new WorkspaceView { DataContext = workspace } };
                WindowPlacement.Restore(window, null);
                windows.Add(window); window.Show();
                for (var slot = 0; slot < counts[index]; slot++)
                {
                    var item = selected[offset++]; var tile = workspace.Tiles[slot];
                    var first = Stopwatch.StartNew();
                    await tile.StartAsync(item.Channel, playback, playback ? 1 : 2, item.Start, item.End);
                    while (tile.NativePlayer?.IsPlaying != true || ReadDecoded(tile) == 0)
                    {
                        Assert.True(first.Elapsed < TimeSpan.FromSeconds(45), $"通道 {item.Channel.Id} 未取得真实解码首帧。");
                        await Task.Delay(250); await tile.TickAsync(DateTimeOffset.UtcNow);
                    }
                    firstFrames.Add(new { workspace = workspace.Id, slot, channelId = item.Channel.Id, streamType = tile.StreamType,
                        firstFrameMs = first.Elapsed.TotalMilliseconds, item.Start, item.End, stream = Snapshot(tile) });
                    positions[tile.ConsumerId] = tile.PlaybackPositionMs ?? 0;
                }
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "streams.json"), JsonSerializer.Serialize(firstFrames, json));
            timer.Start();
            using var self = Process.GetCurrentProcess();
            var previousCpu = self.TotalProcessorTime; var previousTime = timer.Elapsed;
            var nextControls = TimeSpan.FromMinutes(5);
            var nextRefresh = TimeSpan.FromMinutes(15);
            using var writer = new StreamWriter(Path.Combine(directory, "samples.jsonl"), append: false) { AutoFlush = true };
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await Task.WhenAll(workspaces.Select(w => w.TickAsync()));
                self.Refresh();
                var cpu = self.TotalProcessorTime; var elapsed = timer.Elapsed;
                var cpuPercent = (cpu - previousCpu).TotalMilliseconds / (elapsed - previousTime).TotalMilliseconds / Environment.ProcessorCount * 100;
                previousCpu = cpu; previousTime = elapsed;
                var localResponse = await Task.Run(async () =>
                {
                    var action = Stopwatch.StartNew();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var workspace = workspaces[0];
                        workspace.SelectedTile = workspace.Tiles[(workspace.SelectedTile!.Index + 1) % counts[0]];
                    });
                    return action.Elapsed.TotalMilliseconds;
                });
                responses.Add(localResponse);
                samples.Add((elapsed.TotalSeconds, self.PrivateMemorySize64, self.HandleCount));
                var tiles = workspaces.SelectMany(w => w.Tiles.Take(counts[workspaces.IndexOf(w)])).ToArray();
                foreach (var tile in tiles)
                {
                    var position = tile.PlaybackPositionMs ?? -1;
                    Assert.NotNull(tile.SessionId);
                    Assert.True(tile.NativePlayer?.IsPlaying == true, $"通道 {tile.Channel?.Id} 非预期断流：{tile.StateLabel}");
                    Assert.True(position > positions[tile.ConsumerId], $"通道 {tile.Channel?.Id} 解码位置停滞。");
                    positions[tile.ConsumerId] = position;
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { observedAt = DateTimeOffset.UtcNow, elapsedSeconds = elapsed.TotalSeconds,
                    processId = self.Id, cpuPercent, self.WorkingSet64, self.PrivateMemorySize64, self.HandleCount, threads = self.Threads.Count,
                    localResponseMs = localResponse, players = tiles.Select(Snapshot).ToArray() }, json));
                if (playback && elapsed >= nextControls)
                {
                    foreach (var tile in tiles)
                    {
                        var state = tile.CaptureState()!;
                        await tile.ControlAsync(new("pause")); await Task.Delay(300);
                        await tile.ControlAsync(new("resume"));
                        await tile.ControlAsync(new("speed", Speed: 2));
                        await tile.ControlAsync(new("seek", Position: state.Start!.Value.AddSeconds(30)));
                        await tile.ControlAsync(new("speed", Speed: 1));
                        positions[tile.ConsumerId] = -1;
                    }
                    nextControls += TimeSpan.FromMinutes(5);
                }
                if (elapsed >= nextRefresh) { await session.RefreshAsync(); nextRefresh += TimeSpan.FromMinutes(15); }
                if (playback)
                    foreach (var tile in tiles)
                        if (tile.CaptureState() is { End: { } end, Position: { } position, Start: { } start } && end - position < TimeSpan.FromSeconds(45))
                        { await tile.ControlAsync(new("seek", Position: start)); positions[tile.ConsumerId] = -1; }
            }
            var sorted = responses.Order().ToArray();
            var p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
            Assert.True(p95 <= 300, $"本地操作第 95 百分位为 {p95:0.0} 毫秒，超过 300 毫秒。");
            completed = true;
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            foreach (var workspace in workspaces)
                try { await workspace.DisposeAsync(); } catch (Exception ex) { failure ??= ex; }
            foreach (var window in windows) window.Close();
            if (ptz is not null) await ptz.DisposeAsync();
            try { await session.LogoutAsync(); } catch (Exception ex) { failure ??= ex; }
            var sorted = responses.Order().ToArray();
            await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new
            {
                mode = playback ? "playback" : "live", targetStreams = expected, requestedSeconds = seconds, elapsedSeconds = timer.Elapsed.TotalSeconds,
                completed = completed && failure is null, meetsRequiredDuration = completed && seconds >= (playback ? 7200 : 86400),
                failure = failure?.Message, p95Ms = sorted.Length == 0 ? (double?)null : sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1],
                memoryTrendBytesPerHour = Trend(samples, s => s.Bytes), handleTrendPerHour = Trend(samples, s => s.Handles),
                note = "趋势使用预热后半段的线性拟合；短测不能认定达到 24 小时／2 小时验收，硬解开启也不能替代 GPU 引擎实测。"
            }, json));
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static long ReadDecoded(VideoTileViewModel tile)
    {
        using var media = tile.NativePlayer?.Media;
        return media?.Statistics.DecodedVideo ?? 0;
    }

    private static object Snapshot(VideoTileViewModel tile)
    {
        using var media = tile.NativePlayer?.Media;
        var stats = media?.Statistics;
        var video = media?.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
        return new { channelId = tile.Channel?.Id, tile.ConsumerId, tile.StreamType, tile.StateLabel, positionMs = tile.PlaybackPositionMs,
            codec = video is { } track ? Encoding.ASCII.GetString(BitConverter.GetBytes(track.Codec)) : null,
            width = video?.Data.Video.Width, height = video?.Data.Video.Height, frameRateNumerator = video?.Data.Video.FrameRateNum,
            frameRateDenominator = video?.Data.Video.FrameRateDen, bitrate = video?.Bitrate,
            decoded = stats?.DecodedVideo, displayed = stats?.DisplayedPictures, lost = stats?.LostPictures, inputBitrate = stats?.InputBitrate };
    }

    private static double? Trend(List<(double Seconds, long Bytes, int Handles)> samples, Func<(double Seconds, long Bytes, int Handles), double> value)
    {
        var stable = samples.Skip(samples.Count / 2).ToArray();
        if (stable.Length < 3) return null;
        var x = stable.Average(s => s.Seconds); var y = stable.Average(value);
        var denominator = stable.Sum(s => Math.Pow(s.Seconds - x, 2));
        return denominator == 0 ? null : stable.Sum(s => (s.Seconds - x) * (value(s) - y)) / denominator * 3600;
    }
}
