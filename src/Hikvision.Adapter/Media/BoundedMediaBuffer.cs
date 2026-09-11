using System.Runtime.InteropServices;
using System.Threading.Channels;

// 不丢弃压缩帧；超过字节或条目限制时整个管线失败，由平台重新建立会话。
internal sealed class BoundedMediaBuffer
{
    private readonly Channel<byte[]> _queue;
    private readonly long _limit;
    private readonly int _capacity;
    private int _count;
    private long _bytes;
    private int _failed;
    public BoundedMediaBuffer(long maxBytes = 256 * 1024 * 1024, int capacity = 32768)
    {
        _limit = maxBytes;
        _capacity = capacity;
        _queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    }
    public long Bytes => Interlocked.Read(ref _bytes);
    public bool HighWatermark => Bytes >= _limit / 4 || Volatile.Read(ref _count) >= Math.Max(1, _capacity / 4);
    public bool LowWatermark => Bytes <= _limit / 16 && Volatile.Read(ref _count) <= _capacity / 16;
    public bool Failed => Volatile.Read(ref _failed) != 0;
    public bool TryCopy(IntPtr pointer, uint size)
    {
        if (Failed) return false;
        if (size == 0) return true;
        if (pointer == IntPtr.Zero || size > _limit || Interlocked.Add(ref _bytes, size) > _limit)
        {
            if (pointer != IntPtr.Zero && size <= _limit) Interlocked.Add(ref _bytes, -(long)size);
            Fail(); return false;
        }
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            Interlocked.Increment(ref _count);
            if (_queue.Writer.TryWrite(bytes)) return true;
            Interlocked.Decrement(ref _count);
        }
        catch { }
        Interlocked.Add(ref _bytes, -(long)size); Fail(); return false;
    }
    public bool TryWrite(byte[] bytes)
    {
        if (Failed) return false;
        if (Interlocked.Add(ref _bytes, bytes.LongLength) > _limit)
        {
            Interlocked.Add(ref _bytes, -bytes.LongLength); Fail(); return false;
        }
        Interlocked.Increment(ref _count);
        if (_queue.Writer.TryWrite(bytes)) return true;
        Interlocked.Decrement(ref _count);
        Interlocked.Add(ref _bytes, -bytes.LongLength); Fail(); return false;
    }
    public async ValueTask<byte[]> ReadAsync(CancellationToken token)
    {
        if (Failed) throw new IOException("回放缓冲溢出，会话已失败。");
        var bytes = await _queue.Reader.ReadAsync(token);
        Interlocked.Decrement(ref _count);
        Interlocked.Add(ref _bytes, -bytes.LongLength);
        if (Failed) throw new IOException("回放缓冲溢出，会话已失败。");
        return bytes;
    }
    public void Complete() => _queue.Writer.TryComplete();
    public void Fail()
    {
        Interlocked.Exchange(ref _failed, 1);
        _queue.Writer.TryComplete(new IOException("回放缓冲溢出，会话已失败。"));
    }
}
