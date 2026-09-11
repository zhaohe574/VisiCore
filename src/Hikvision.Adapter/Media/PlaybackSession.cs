using System.Diagnostics;
using System.Threading.Channels;

internal sealed class PlaybackSession : IAsyncDisposable
{
    private readonly IDevice _device;
    private readonly ZlmClient _zlm;
    private readonly TranscodeBudget _budget;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _stateGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly IReadOnlyList<PlaybackSegment> _segments;
    private readonly string _stream;
    private CancellationTokenSource? _pipelineStop;
    private Task? _pipeline;
    private IPlaybackSource? _source;
    private BoundedMediaBuffer? _buffer;
    private Task? _monitor;
    private MediaCodecs? _probedCodecs;
    private string _state = "starting", _codec = "unknown";
    private string? _error;
    private bool _transcoded;
    private DateTimeOffset _currentTime;
    private double _speed = 1;
    private int _segmentIndex = -1;
    private int _disposed;
    private Process? _publisher;
    private bool _userPaused, _sourcePaused;
    private volatile bool _transferComplete;
    private volatile bool _segmentCompleted;
    private long _lastSourcePoll;
    public PlaybackStartRequest Request { get; }

    public PlaybackSession(IDevice device, PlaybackStartRequest request, IReadOnlyList<PlaybackSegment> segments, ZlmClient zlm, TranscodeBudget budget)
    {
        _device = device; Request = request; _segments = segments; _zlm = zlm; _budget = budget;
        _currentTime = request.Start;
        _stream = $"vp2_d{device.Id}_c{request.Channel}_playback_{request.SessionId:N}_{request.Profile}";
    }
    public async Task StartAsync()
    {
        _monitor = Task.Run(MonitorAsync);
        await _gate.WaitAsync();
        try { await StartAtAsync(Request.Start); }
        finally { _gate.Release(); }
    }
    public PlaybackSummary Summary()
    {
        lock (_stateGate)
        {
            var progress = (int)Math.Clamp((_currentTime - Request.Start).TotalMilliseconds / (Request.End - Request.Start).TotalMilliseconds * 100, 0, 100);
            return new(Request.SessionId, _stream, _state, Request.Start, Request.End, _currentTime, progress, _speed, _segments, _codec, _transcoded, _error);
        }
    }
    private async Task StartAtAsync(DateTimeOffset target)
    {
        await StopPipelineAsync();
        _sourcePaused = false; _transferComplete = false; _segmentCompleted = false; _userPaused = false;
        _segmentIndex = -1;
        for (var i = 0; i < _segments.Count; i++)
            if (_segments[i].Start <= target && _segments[i].End > target) { _segmentIndex = i; break; }
        if (_segmentIndex < 0)
        {
            for (var i = 0; i < _segments.Count; i++)
            {
                if (_segments[i].End > target)
                {
                    target = _segments[i].Start > target ? _segments[i].Start : target;
                    _segmentIndex = i;
                    break;
                }
            }
        }
        lock (_stateGate)
        {
            _currentTime = target;
            _state = target >= Request.End ? "completed" : _segmentIndex < 0 ? "gap" : "starting";
            _error = null;
        }
        if (_segmentIndex < 0) return;
        var segment = _segments[_segmentIndex];
        _buffer = new BoundedMediaBuffer();
        var buffer = _buffer;
        _source = OpenSegmentSource(segment, target, buffer);
        _pipelineStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_device.Simulated) _pipeline = PublishAsync(buffer, readyTcs, _pipelineStop.Token);
        else readyTcs.TrySetResult(true);
        _source.Start();
        if (_speed != 1) _source.SetSpeed(_speed);
        if (_device.Simulated)
            lock (_stateGate) { _state = "playing"; _codec = Request.Profile == "native" ? "H265" : "H264"; _transcoded = Request.Profile == "browser"; }
        await Task.WhenAny(readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(8)));
    }

    private IPlaybackSource OpenSegmentSource(PlaybackSegment segment, DateTimeOffset start, BoundedMediaBuffer buffer)
    {
        return _device.OpenPlayback(Request.Channel, start, segment.End, segment.FileIndex, (type, pointer, size) =>
        {
            if (type is 1 or 2 or 3 or 4 or 5) buffer.TryCopy(pointer, size);
            if (type == 12)
            {
                _segmentCompleted = true;
            }
        });
    }

    private async Task PublishAsync(BoundedMediaBuffer buffer, TaskCompletionSource<bool> readyTcs, CancellationToken token)
    {
        var origin = _currentTime;
        var speed = _speed;
        Process? process = null;
        IDisposable? slot = null;
        Task? drains = null;
        Task<string>? diagnostic = null;
        Exception? failure = null;
        try
        {
            // 只保留有限的探测前缀；识别录像自身的编码后，同一批字节原样送入发布进程。
            var prefix = await PlaybackPrefix.ReadAsync(buffer, token, _probedCodecs is null ? PlaybackPrefix.TargetBytes : 64 * 1024);
            var codecs = _probedCodecs ?? await MediaTools.ProbeAsync("pipe:0", token, prefix.Sample());
            _probedCodecs = codecs;
            var transcode = Request.Profile == "browser" && codecs.RequiresBrowserTranscode;
            if (transcode) slot = _budget.Acquire();
            var target = $"{MediaTools.RtmpBase.TrimEnd('/')}/playback/{_stream}";
            var args = PlaybackPublisher.Arguments("pipe:0", target, codecs, transcode, Request.Profile, speed, "flv");
            process = MediaTools.Start(MediaTools.Ffmpeg, args, true);
            var publishing = process;
            publishing.Exited += (_, _) =>
            {
                if (!token.IsCancellationRequested && !_transferComplete)
                {
                    lock (_stateGate) { _state = "failed"; _error = "回放发布进程提前退出。"; }
                    readyTcs.TrySetResult(false);
                }
            };
            publishing.EnableRaisingEvents = true;
            diagnostic = MediaTools.CaptureAsync(process.StandardError, 16 * 1024, CancellationToken.None);
            drains = Task.WhenAll(diagnostic, PlaybackPublisher.ReadProgressAsync(process.StandardOutput, elapsed =>
            {
                var position = origin + elapsed * speed;
                lock (_stateGate)
                    if (!_userPaused) _currentTime = position < Request.Start ? Request.Start : position > Request.End ? Request.End : position;
            }, token));
            lock (_stateGate)
            {
                _publisher = process; _codec = transcode ? "H264" : codecs.DisplayVideo; _transcoded = transcode;
                // 不使用 SIGSTOP 来实现暂停，只依靠 SDK 端暂停数据推送。
            }
            await prefix.ReplayAsync(process.StandardInput.BaseStream, token);
            var readiness = _zlm.WaitReadyAsync("playback", _stream, token, "rtmp");
            _ = Task.Run(async () =>
            {
                try
                {
                    await readiness;
                    readyTcs.TrySetResult(true);
                    lock (_stateGate) { if (_state == "starting") _state = "playing"; }
                }
                catch (Exception ex)
                {
                    readyTcs.TrySetException(ex);
                }
            }, token);
            while (true)
            {
                if (readiness.IsFaulted) await readiness;
                if (readiness.IsCompletedSuccessfully) lock (_stateGate) { if (_state == "starting") _state = "playing"; }
                if (process.HasExited) throw new IOException("回放发布进程异常退出。");
                byte[] bytes;
                try { bytes = await buffer.ReadAsync(token); }
                catch (ChannelClosedException) when (!buffer.Failed) { break; }
                await process.StandardInput.BaseStream.WriteAsync(bytes, token);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new IOException("回放媒体发布失败。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            failure = ex;
            readyTcs.TrySetResult(false);
            lock (_stateGate) { _state = "failed"; _error = ex is AdapterException ? ex.Message : "回放缓冲或媒体发布失败，请重新建立会话。"; }
        }
        finally
        {
            lock (_stateGate) _publisher = null;
            if (process is not null)
            {
                MediaTools.Kill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                if (drains is not null) try { await drains; } catch (OperationCanceledException) { }
                process.Dispose();
            }
            if (failure is not null)
                MediaTools.ReportFailure($"回放 {Request.SessionId}，设备 {_device.Id}，通道 {Request.Channel}，{failure.GetType().Name}：{failure.Message}", diagnostic is null ? "" : await diagnostic);
            slot?.Dispose();
        }
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                if (!_device.Simulated && !_transferComplete && _source is { } activeSource)
                {
                    var shouldPause = _userPaused || _buffer?.HighWatermark == true;
                    if (!_sourcePaused && shouldPause) { activeSource.Pause(true); _sourcePaused = true; }
                    else if (_sourcePaused && !_userPaused && _buffer?.LowWatermark == true) { activeSource.Pause(false); _sourcePaused = false; }
                }

                await _gate.WaitAsync(_stop.Token);
                try
                {
                    if (_buffer?.Failed == true)
                        lock (_stateGate) { _state = "failed"; _error = "回放缓冲溢出，会话已停止，请重新建立会话。"; }
                    if (_state == "failed") { await StopPipelineAsync(); continue; }
                    if (_source is null) continue;
                    if (Environment.TickCount64 - _lastSourcePoll >= 250)
                    {
                        _lastSourcePoll = Environment.TickCount64;
                        if (_device.Simulated && _source.CurrentTime is { } position && position >= Request.Start && position <= Request.End)
                            lock (_stateGate) _currentTime = position;
                        if (_source.Completed) _segmentCompleted = true;
                    }
                    if (!_userPaused && _segmentCompleted && _buffer is not null && !_buffer.Failed)
                    {
                        _segmentCompleted = false;
                        var nextIndex = _segmentIndex + 1;
                        if (nextIndex < _segments.Count && _segments[nextIndex].Start <= _segments[_segmentIndex].End.AddSeconds(5))
                        {
                            _source?.Dispose();
                            _segmentIndex = nextIndex;
                            var nextSeg = _segments[nextIndex];
                            _source = OpenSegmentSource(nextSeg, nextSeg.Start, _buffer);
                            _source.Start();
                            if (_speed != 1) _source.SetSpeed(_speed);
                            _sourcePaused = false;
                        }
                        else if (nextIndex < _segments.Count)
                        {
                            if (_buffer.Bytes == 0)
                            {
                                var nextSeg = _segments[nextIndex];
                                await StartAtAsync(nextSeg.Start);
                            }
                        }
                        else
                        {
                            _transferComplete = true;
                            _buffer.Complete();
                        }
                    }
                    if (_transferComplete && (_pipeline is null || _pipeline.IsCompleted))
                    {
                        lock (_stateGate)
                        {
                            if (_state != "failed") _state = "completed";
                        }
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (Exception)
                {
                    lock (_stateGate) { _state = "failed"; _error = "回放设备状态读取或片段切换失败。"; }
                    await StopPipelineAsync();
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task<PlaybackSummary> ControlAsync(PlaybackControlRequest request)
    {
        if (request.Action == "seek")
        {
            if (request.Position is not { } p)
                throw new ArgumentException("定位时间不能为空。");
            if (p < Request.Start.AddSeconds(-2) || p > Request.End.AddSeconds(2))
                throw new ArgumentException("定位时间不在回放范围内。");
            var clamped = p < Request.Start ? Request.Start : p > Request.End ? Request.End : p;
            request = request with { Position = clamped };
        }
        await _gate.WaitAsync();
        try
        {
            if (_state is "stopped" or "failed") throw new AdapterException(409, "PLAYBACK_STATE", "该回放已停止或失败，无法继续控制。");
            switch (request.Action)
            {
                case "pause":
                    SetPaused(true);
                    break;
                case "resume":
                    if (_source is null)
                    {
                        var next = _segments.FirstOrDefault(s => s.End > _currentTime);
                        if (next is not null) await StartAtAsync(next.Start > _currentTime ? next.Start : _currentTime);
                    }
                    else SetPaused(false);
                    break;
                case "seek":
                    await StartAtAsync(request.Position!.Value);
                    break;
                case "speed":
                    if (request.Speed is not (0.25 or 0.5 or 1 or 2 or 4 or 8)) throw new ArgumentException("回放倍速支持 0.25、0.5、1、2、4、8。");
                    if (_source is null) throw new AdapterException(409, "PLAYBACK_GAP", "当前没有可播放片段，不能设置倍速。");
                    try
                    {
                        _source.SetSpeed(request.Speed.Value);
                        var paused = _userPaused;
                        lock (_stateGate) _speed = request.Speed.Value;
                        if (!_device.Simulated)
                        {
                            await StartAtAsync(Summary().CurrentTime);
                            if (paused) SetPaused(true);
                        }
                    }
                    catch { lock (_stateGate) _speed = 1; throw; }
                    break;
                default: throw new ArgumentException("回放控制动作无效。");
            }
            return Summary();
        }
        catch (Exception) when (request.Action == "seek")
        {
            lock (_stateGate) { _state = "failed"; _error = "回放定位失败。"; }
            await StopPipelineAsync(); throw;
        }
        finally { _gate.Release(); }
    }
    private void SetPaused(bool pause)
    {
        if (_source is not null)
        {
            var sourcePause = pause || !_device.Simulated && _buffer?.LowWatermark == false;
            _source.Pause(sourcePause); _sourcePaused = sourcePause;
        }
        lock (_stateGate)
        {
            // 不对 FFmpeg 发 SIGSTOP，SIGSTOP 会导致 RTMP 心跳停止，ZLM 会断开播放连接。
            // SDK 端已暂停推送数据，FFmpeg 输入管道空闲，画面自然静止；
            // 恢复时 SDK 重新推送，FFmpeg 自动继续读取输出。
            _userPaused = pause; _state = pause ? "paused" : "playing";
        }
    }
    private async Task StopPipelineAsync()
    {
        try { _source?.Dispose(); }
        finally
        {
            _source = null;
            if (_pipelineStop is not null) await _pipelineStop.CancelAsync();
            if (_pipeline is not null) await _pipeline;
            _pipelineStop?.Dispose(); _pipelineStop = null; _pipeline = null; _buffer = null;
            if (!_device.Simulated)
            {
                try { await _zlm.CloseAsync("playback", _stream); } catch { }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _stop.CancelAsync();
        if (_monitor is not null) await _monitor;
        await _gate.WaitAsync();
        try
        {
            await StopPipelineAsync();
            lock (_stateGate) _state = "stopped";
        }
        catch { Interlocked.Exchange(ref _disposed, 0); throw; }
        finally { _gate.Release(); if (Volatile.Read(ref _disposed) != 0) _stop.Dispose(); }
    }
}
