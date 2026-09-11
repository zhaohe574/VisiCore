using System.IO;
using System.Collections.Specialized;
using System.Globalization;
using System.Net.Http;
using System.Web;
using VideoPlatform.Client;
using VideoPlatform.Desktop.Models;
using Generated = VideoPlatform.Client.Generated;

namespace VideoPlatform.Desktop.Services;

public interface IPlatformApi
{
    Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default);
    Task<T> PostAsync<T>(string path, object? body = null, CancellationToken cancellationToken = default);
    Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default);
    Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken = default);
    Task DownloadAsync(string path, string destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class PlatformApi(SessionService session) : IPlatformApi
{
    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var route = new Route(path);
        if (route.Parts is ["public", "releases", "latest"])
        {
            var release = await session.SendPublicAsync((client, ct) => client.GetLatestReleaseOrDefaultAsync(
                route.Text("currentVersion"), route.PackageType(), ct), cancellationToken);
            return release is null ? default : Result<T>(ClientModelMapping.From(release));
        }
        var value = await session.SendAuthorizedAsync<object>(async (client, ct) => route.Parts switch
        {
            ["auth", "me"] => ClientModelMapping.From(await client.GetCurrentUserAsync(ct)),
            ["channels"] => ClientModelMapping.From(await client.ListChannelsAsync(route.Long("deviceId"), route.Long("unitId"),
                route.Boolean("online"), route.Integer("page"), route.Integer("pageSize"), route.Text("search"), ct)),
            ["favorites"] => (await client.GetFavoritesAsync(ct)).Select(ClientModelMapping.From).ToArray(),
            ["layouts"] => (await client.GetLayoutsAsync(ct)).Select(ClientModelMapping.From).ToArray(),
            ["organization"] => ClientModelMapping.From(await client.GetOrganizationAsync(ct)),
            ["live-sessions", var id] => ClientModelMapping.From(await client.GetliveSessionAsync(Guid.Parse(id), ct)),
            ["playback-sessions", var id] => ClientModelMapping.From(await client.GetplaybackSessionAsync(Guid.Parse(id), ct)),
            ["alarms"] => ClientModelMapping.From(await client.ListAlarmsAsync(route.Text("state"), route.Long("deviceId"),
                route.Long("channelId"), route.Text("eventType"), route.Date("from"), route.Date("to"),
                route.Integer("page"), route.Integer("pageSize"), route.Text("search"), ct)),
            ["alarms", var id] => ClientModelMapping.From(await client.GetAlarmAsync(Id(id), ct)),
            ["exports"] => ClientModelMapping.From(await client.ListExportsAsync(route.Integer("page"), route.Integer("pageSize"), ct)),
            _ => throw Unsupported(HttpMethod.Get, path)
        }, cancellationToken);
        return Result<T>(value);
    }

    public async Task<T> PostAsync<T>(string path, object? body = null, CancellationToken cancellationToken = default)
    {
        var route = new Route(path);
        var value = await session.SendAuthorizedAsync<object>(async (client, ct) => (route.Parts, body) switch
        {
            (["live-sessions"], LiveRequest request) => ClientModelMapping.From(await client.StartLiveAsync(new Generated.LiveRequest
                { ChannelId = request.ChannelId, StreamType = request.StreamType, Profile = request.Profile }, cancellationToken: ct)),
            (["playback-sessions"], PlaybackRequest request) => ClientModelMapping.From(await client.StartPlaybackAsync(new Generated.PlaybackRequest
                { ChannelId = request.ChannelId, Start = request.Start, End = request.End, Profile = request.Profile }, cancellationToken: ct)),
            (["recordings", "search"], RecordingRequest request) => (await client.SearchRecordingsAsync(new Generated.RecordingRequest
                { ChannelId = request.ChannelId, Start = request.Start, End = request.End }, cancellationToken: ct)).Select(ClientModelMapping.From).ToArray(),
            (["layouts"], LayoutRequest request) => ClientModelMapping.From(await client.CreateLayoutAsync(ClientModelMapping.To(request), cancellationToken: ct)),
            (["exports"], ExportRequest request) => CreatedExport(await client.CreateExportAsync(new Generated.RecordingRequest
                { ChannelId = request.ChannelId, Start = request.Start, End = request.End }, cancellationToken: ct), request),
            _ => throw Unsupported(HttpMethod.Post, path)
        }, cancellationToken);
        return Result<T>(value);
    }

    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        var route = new Route(path);
        await session.SendAuthorizedAsync(async (client, ct) =>
        {
            switch (method.Method, route.Parts, body)
            {
                case ("PUT", ["auth", "profile"], ProfileRequest request):
                    await client.UpdateProfileAsync(new Generated.ProfileRequest { DisplayName = request.DisplayName, Phone = request.Phone }, cancellationToken: ct); break;
                case ("PUT", ["auth", "password"], PasswordRequest request):
                    await client.ChangePasswordAsync(new Generated.PasswordRequest { CurrentPassword = request.CurrentPassword, NewPassword = request.NewPassword }, cancellationToken: ct); break;
                case ("PUT", ["favorites"], FavoritesRequest request):
                    await client.UpdateFavoritesAsync(new Generated.FavoritesRequest { ChannelIds = request.ChannelIds }, cancellationToken: ct); break;
                case ("PUT", ["layouts", var id], LayoutRequest request):
                    await client.UpdateLayoutAsync(Id(id), ClientModelMapping.To(request), cancellationToken: ct); break;
                case ("DELETE", ["layouts", var id], _):
                    await client.DeleteLayoutAsync(Id(id), cancellationToken: ct); break;
                case ("POST", ["live-sessions", var id, "renew"], _):
                    await client.RenewliveSessionAsync(Guid.Parse(id), cancellationToken: ct); break;
                case ("POST", ["playback-sessions", var id, "renew"], _):
                    await client.RenewplaybackSessionAsync(Guid.Parse(id), cancellationToken: ct); break;
                case ("POST", ["playback-sessions", var id, "control"], PlaybackControl request):
                    await client.ControlPlaybackAsync(Guid.Parse(id), new Generated.PlaybackControlRequest
                        { Action = request.Action, Position = request.Position, Speed = request.Speed }, cancellationToken: ct); break;
                case ("DELETE", ["live-sessions"], _):
                    await session.ClearMediaSessionsAsync("live", route.Text("all") == "true", ct); break;
                case ("DELETE", ["playback-sessions"], _):
                    await session.ClearMediaSessionsAsync("playback", route.Text("all") == "true", ct); break;
                case ("DELETE", ["live-sessions", var id], _):
                    await client.StopliveSessionAsync(Guid.Parse(id), cancellationToken: ct); break;
                case ("DELETE", ["playback-sessions", var id], _):
                    await client.StopplaybackSessionAsync(Guid.Parse(id), cancellationToken: ct); break;
                case ("POST", ["channels", var id, "ptz"], PtzRequest request):
                    await client.StartPtzAsync(Id(id), new Generated.PtzRequest { Command = request.Command, Speed = request.Speed }, cancellationToken: ct); break;
                case ("POST", ["channels", var id, "ptz", "stop"], _):
                    await client.StopPtzAsync(Id(id), cancellationToken: ct); break;
                case ("POST", ["channels", var id, "ptz", "presets", var preset], _):
                    await client.CallPtzPresetAsync(Id(id), int.Parse(preset, CultureInfo.InvariantCulture), cancellationToken: ct); break;
                case ("POST", ["alarms", var id, "actions"], AlarmAction request):
                    await client.ActOnAlarmAsync(Id(id), new Generated.AlarmActionRequest { Action = request.Action, Note = request.Note }, cancellationToken: ct); break;
                case ("POST", ["exports", var id, "cancel"], _):
                    await client.CancelExportAsync(Guid.Parse(id), cancellationToken: ct); break;
                case ("POST", ["exports", var id, "retry"], _):
                    await client.RetryExportAsync(Guid.Parse(id), cancellationToken: ct); break;
                default: throw Unsupported(method, path);
            }
            return true;
        }, cancellationToken);
    }

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        var generation = session.Generation;
        using var response = await OpenFileAsync(path, cancellationToken);
        using var buffer = new MemoryStream();
        await response.Stream.CopyToAsync(buffer, cancellationToken);
        if (generation != session.Generation) throw new OperationCanceledException("登录状态已改变，已忽略迟到响应。");
        return buffer.ToArray();
    }

    public async Task DownloadAsync(string path, string destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var generation = session.Generation;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await OpenFileAsync(path, cancellationToken);
            var source = response.Stream;
            var contentLength = response.Headers.FirstOrDefault(header => header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
            long? expectedLength = long.TryParse(contentLength, NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? length : null;
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (generation != session.Generation) throw new OperationCanceledException("账号已退出，下载已取消。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    if (expectedLength is > 0) progress?.Report(total * 100d / expectedLength.Value);
                }
                if (expectedLength is { } expected && expected != total) throw new IOException("下载未完成，文件长度不匹配。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != session.Generation) throw new OperationCanceledException("账号已退出，下载已取消。");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Task<Generated.FileResponse> OpenFileAsync(string path, CancellationToken cancellationToken)
    {
        var route = new Route(path);
        if (route.Parts is ["public", "releases", var release, "download"])
            return session.SendPublicAsync((client, ct) => client.DownloadReleaseAsync(Id(release), ct), cancellationToken);
        return session.SendAuthorizedAsync((client, ct) => route.Parts switch
        {
            ["alarms", var id, "image"] => client.GetAlarmImageAsync(Id(id), ct),
            ["exports", var id, "download"] => client.DownloadExportAsync(Guid.Parse(id), ct),
            _ => throw Unsupported(HttpMethod.Get, path)
        }, cancellationToken);
    }

    // 创建接口只返回任务标识和状态，其余已知字段来自提交内容，列表刷新再取得完整详情。
    private static ExportJob CreatedExport(Generated.ExportCreatedResponse value, ExportRequest request) => new(value.Id.ToString(),
        request.ChannelId, "", request.Start, request.End, value.State, value.Progress, null, null, null, default, null);

    private static T Result<T>(object value) => value is T result ? result : throw new InvalidDataException($"接口返回类型与桌面调用不匹配：{typeof(T).Name}。");
    private static long Id(string value) => long.Parse(value, CultureInfo.InvariantCulture);
    private static NotSupportedException Unsupported(HttpMethod method, string path) => new($"生成客户端尚未映射桌面调用：{method} {path}。");

    private sealed class Route
    {
        public string[] Parts { get; }
        private readonly NameValueCollection _query;

        public Route(string path)
        {
            var uri = new Uri(new Uri("https://desktop.invalid/"), path.TrimStart('/'));
            Parts = uri.AbsolutePath.Trim('/').Split('/');
            _query = HttpUtility.ParseQueryString(uri.Query);
        }

        public string? Text(string key) => _query[key];
        public int? Integer(string key) => Text(key) is { Length: > 0 } value ? int.Parse(value, CultureInfo.InvariantCulture) : null;
        public long? Long(string key) => Text(key) is { Length: > 0 } value ? Id(value) : null;
        public bool? Boolean(string key) => Text(key) is { Length: > 0 } value ? bool.Parse(value) : null;
        public DateTimeOffset? Date(string key) => Text(key) is { Length: > 0 } value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) : null;
        public Generated.PackageType? PackageType() => Text("packageType") switch
        {
            null or "" => null,
            "msi" => Generated.PackageType.Msi,
            "zip" => Generated.PackageType.Zip,
            _ => throw new ArgumentException("更新包类型必须为 msi 或 zip。")
        };
    }
}
