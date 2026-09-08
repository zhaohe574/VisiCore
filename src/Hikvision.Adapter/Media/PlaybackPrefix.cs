using System.Threading.Channels;

// 探测样本有独立上限，保留原始块用于重放，不能复制整个最大回调块多次。
internal sealed class PlaybackPrefix
{
    public const int TargetBytes = 512 * 1024;
    public const int SampleLimit = 2 * 1024 * 1024;
    private readonly List<byte[]> _blocks = new();
    private int _length;
    public int RetainedBytes => _length;

    public static async Task<PlaybackPrefix> ReadAsync(BoundedMediaBuffer buffer, CancellationToken token)
    {
        var prefix = new PlaybackPrefix();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (prefix._length < TargetBytes)
        {
            try
            {
                var block = await buffer.ReadAsync(timeout.Token);
                prefix._blocks.Add(block); prefix._length += block.Length;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && prefix._length > 0) { break; }
            catch (ChannelClosedException) when (!buffer.Failed && prefix._length > 0) { break; }
        }
        return prefix;
    }
    public byte[] Sample()
    {
        var sample = new byte[Math.Min(_length, SampleLimit)];
        var offset = 0;
        foreach (var block in _blocks)
        {
            var count = Math.Min(block.Length, sample.Length - offset);
            block.AsSpan(0, count).CopyTo(sample.AsSpan(offset));
            offset += count;
            if (offset == sample.Length) break;
        }
        return sample;
    }
    public async Task ReplayAsync(Stream destination, CancellationToken token)
    {
        try { foreach (var block in _blocks) await destination.WriteAsync(block, token); }
        finally { _blocks.Clear(); _length = 0; }
    }
}
