using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media.Imaging;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using VideoPlatform.Desktop.Views;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class CandidateFactAttribute : FactAttribute
{
    public CandidateFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VP_CANDIDATE_PASSWORD"))) Skip = "需要候选平台只在测试进程内提供的登录凭据。";
    }
}

[Collection(nameof(WpfTestCollection))]
public sealed class CandidateTests(WpfTestHost host)
{
    [CandidateFact]
    [Trait("Category", "Candidate")]
    public Task TrustedHttpsLoginNativeVideoAndSessionRelease() => host.RunAsync(VerifyAsync, TimeSpan.FromMinutes(3));

    private static async Task VerifyAsync()
    {
        Window? window = null;
        WorkspaceViewModel? workspace = null;
        WorkspaceViewModel? sibling = null;
        Window? siblingWindow = null;
        SessionService? session = null;
        VlcPlayerFactory? players = null;
        PtzService? ptz = null;
        Exception? failure = null;
        using var http = new HttpClient(TransportPolicy.CreateHandler(Environment.GetEnvironmentVariable("VP_CANDIDATE_SOURCE_ADDRESS"))) { Timeout = TimeSpan.FromSeconds(30) };
        var report = new List<string>();
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../candidate-evidence")); Directory.CreateDirectory(output);
        try
        {
            session = new SessionService(http, new MemoryCredentials());
            session.SetServer(Environment.GetEnvironmentVariable("VP_CANDIDATE_URL") ?? "https://10.37.200.74:8443");
            await session.LoginAsync(Environment.GetEnvironmentVariable("VP_CANDIDATE_USER") ?? "admin", Environment.GetEnvironmentVariable("VP_CANDIDATE_PASSWORD")!);
            Assert.True(session.IsAuthenticated);
            var token = await session.GetTokenAsync(); await session.RefreshAsync(); Assert.Equal(token, await session.GetTokenAsync());
            report.Add("通过：候选 HTTPS 正常证书校验登录，稳定令牌续期。");
            var api = new PlatformApi(session); players = new VlcPlayerFactory();
            ptz = new PtzService(api);
            workspace = new WorkspaceViewModel(api, players, ptz, new UiDispatcher(), new FakeDialogs());
            await workspace.SetAccessAsync(session.CurrentUser); await workspace.RefreshAsync();
            report.Add($"资源：{workspace.Channels.Count} 路通道，{workspace.Channels.Values.Count(c => c.Online)} 路在线。");
            var selected = workspace.Channels.Values.Where(c => c.Online).Take(2).ToArray(); Assert.Equal(2, selected.Length);
            workspace.StreamType = "1";
            window = new Window { Title = "VisiCore（视枢）候选实机验收", Width = 1366, Height = 768, Content = new WorkspaceView { DataContext = workspace } };
            window.Show();
            for (var i = 0; i < selected.Length; ++i)
            {
                var tile = workspace.Tiles[i]; await workspace.OpenChannelAsync(selected[i], tile);
                Assert.NotNull(tile.SessionId);
                var path = Path.Combine(output, $"native-channel-{selected[i].Id}.png");
                File.Delete(path);
                for (var attempt = 0; attempt < 60; ++attempt)
                {
                    await Task.Delay(500);
                    await tile.TickAsync(DateTimeOffset.UtcNow);
                    if (tile.NativePlayer?.IsPlaying == true && tile.Capture(path))
                    {
                        await Task.Delay(500);
                        if (File.Exists(path) && new FileInfo(path).Length > 1024) break;
                    }
                }
                uint width = 0, height = 0;
                tile.NativePlayer?.Size(0, ref width, ref height);
                report.Add($"通道 {selected[i].Id}：设备码流 {selected[i].Codec}，播放器状态 {tile.NativePlayer?.State}，画面尺寸 {width}×{height}。");
                Assert.True(File.Exists(path), $"全局通道 {selected[i].Id} 未取得原生解码截图：{tile.StateLabel}");
                using var file = File.OpenRead(path); var bitmap = BitmapFrame.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var pixels = new byte[bitmap.PixelHeight * ((bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8)]; bitmap.CopyPixels(pixels, (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8, 0);
                Assert.True(pixels.Distinct().Count() > 20, "真实视频截图不能是单色空白画面。");
                report.Add($"通过：全局通道 {selected[i].Id} 原生视频截图 {bitmap.PixelWidth}×{bitmap.PixelHeight}，主码流使用 HTTPS MPEG-TS。");
            }
            sibling = workspace.CreateSibling();
            await sibling.SetAccessAsync(session.CurrentUser); await sibling.RefreshAsync();
            siblingWindow = new Window { Title = "VisiCore（视枢）候选实机验收副工作区", Width = 960, Height = 720, Content = new WorkspaceView { DataContext = sibling } };
            siblingWindow.Show();
            await sibling.OpenChannelCoreAsync(selected[0], sibling.Tiles[0]);
            for (var attempt = 0; attempt < 60 && sibling.Tiles[0].NativePlayer?.IsPlaying != true; attempt++)
                await Task.Delay(500);
            Assert.True(sibling.Tiles[0].NativePlayer?.IsPlaying == true);
            Assert.NotEqual(workspace.Tiles[0].SessionId, sibling.Tiles[0].SessionId);
            var siblingSession = sibling.Tiles[0].SessionId!;
            var positionBefore = sibling.Tiles[0].PlaybackPositionMs ?? -1;
            var lease = workspace.Tiles[0].SessionId!;
            await workspace.Tiles[0].StopAsync();
            Assert.Null(workspace.Tiles[0].NativePlayer);
            var ended = await Assert.ThrowsAsync<PlatformException>(() => api.SendAsync(HttpMethod.Post, $"live-sessions/{lease}/renew"));
            Assert.Equal(HttpStatusCode.NotFound, ended.StatusCode);
            Assert.NotNull(workspace.Tiles[1].NativePlayer);
            await Task.Delay(3000);
            Assert.True(sibling.Tiles[0].PlaybackPositionMs > positionBefore);
            await api.SendAsync(HttpMethod.Post, $"live-sessions/{siblingSession}/renew");
            report.Add("通过：同通道在两个独立工作区播放，停止第一格后副窗口持续解码并可独立续期。");
            await workspace.SetModeAsync(true);
            Assert.All(workspace.Tiles, tile => Assert.Null(tile.NativePlayer));
            Assert.Equal(siblingSession, sibling.Tiles[0].SessionId);
            Assert.True(sibling.Tiles[0].NativePlayer?.IsPlaying == true);
            report.Add("通过：主工作区切换回放仅释放本工作区，副工作区实时画面保持运行。");
            await sibling.DisposeAsync(); sibling = null; siblingWindow.Close(); siblingWindow = null;
            await workspace.StopAllCoreAsync(); Assert.All(workspace.Tiles, tile => Assert.Null(tile.NativePlayer));
            await session.LogoutAsync(); Assert.False(session.IsAuthenticated);
            report.Add("通过：全部停止释放原生播放器，主动退出撤销登录会话。");
        }
        catch (Exception ex) { report.Add($"失败：{ex.Message}"); failure = ex; }
        finally
        {
            try
            {
                if (workspace is not null) await workspace.ClearAsync();
                if (sibling is not null) await sibling.DisposeAsync();
                if (ptz is not null) await ptz.DisposeAsync();
                if (session is not null) await session.LogoutAsync();
            }
            catch (Exception ex) { report.Add($"清理失败：{ex.Message}"); failure ??= ex; }
            finally
            {
                siblingWindow?.Close(); window?.Close(); players?.Dispose();
                await File.WriteAllLinesAsync(Path.Combine(output, "候选实机验收.txt"), report);
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
