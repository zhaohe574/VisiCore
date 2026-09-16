internal static class PlaybackTimeline
{
    /// <summary>
    /// 规范化回放时间线。<paramref name="streamType"/> 指定优先使用的录像码流（1 主码流、2 子码流）：
    /// 录像机在同时段通常保存主／子两套文件，取错码流会让小窗口回放也吃主码流带宽与解码开销。
    /// 指定档位没有录像时回退到全部录像，避免出现“有录像却检索不到”的假缺口。
    /// </summary>
    public static IReadOnlyList<PlaybackSegment> Normalize(IEnumerable<RecordingSummary> recordings, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, int? streamType = null)
    {
        if (rangeEnd <= rangeStart)
            return Array.Empty<PlaybackSegment>();

        var list = recordings.Where(item => item.End > item.Start).ToList();
        if (streamType is 1 or 2)
        {
            var preferred = list.Where(item => item.StreamType == streamType.Value).ToList();
            if (preferred.Count > 0) list = preferred;
        }

        var result = new List<PlaybackSegment>();
        foreach (var recording in list
            .Where(item => item.End > rangeStart && item.Start < rangeEnd)
            .Select(item => new PlaybackSegment(
                item.Start < rangeStart ? rangeStart : item.Start,
                item.End > rangeEnd ? rangeEnd : item.End,
                item.FileName,
                item.FileSize,
                item.FileType,
                item.StreamType,
                item.FileIndex))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End))
        {
            if (result.Count > 0
                && recording.Start <= result[^1].End
                && recording.StreamType == result[^1].StreamType
                && string.Equals(recording.FileName, result[^1].FileName, StringComparison.Ordinal)
                && recording.FileIndex == result[^1].FileIndex)
            {
                var previous = result[^1];
                result[^1] = previous with { End = recording.End > previous.End ? recording.End : previous.End };
            }
            else
            {
                result.Add(recording);
            }
        }
        return result;
    }

    public static void SelfTest()
    {
        var start = new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.FromHours(8));
        var recordings = new[]
        {
            new RecordingSummary("late", start.AddMinutes(40), start.AddMinutes(80), 0, 0, 0, 2),
            new RecordingSummary("early", start.AddMinutes(-10), start.AddMinutes(15), 0, 0, 0, 1),
            new RecordingSummary("early", start.AddMinutes(10), start.AddMinutes(20), 0, 0, 0, 1)
        };
        var segments = Normalize(recordings, start, start.AddHours(1));
        if (segments.Count != 2
            || segments[0].Start != start
            || segments[0].End != start.AddMinutes(20)
            || segments[1].Start != start.AddMinutes(40)
            || segments[1].End != start.AddHours(1))
            throw new InvalidOperationException("回放时间线规范化自检失败");

        // 同时存在主／子码流录像时，必须只取请求的档位。
        var mixed = new[]
        {
            new RecordingSummary("main", start, start.AddHours(1), 0, 0, 1, 1),
            new RecordingSummary("sub", start, start.AddHours(1), 0, 0, 2, 2)
        };
        var sub = Normalize(mixed, start, start.AddHours(1), 2);
        if (sub.Count != 1 || sub[0].StreamType != 2 || sub[0].FileName != "sub")
            throw new InvalidOperationException("回放时间线子码流优选自检失败");
        var main = Normalize(mixed, start, start.AddHours(1), 1);
        if (main.Count != 1 || main[0].StreamType != 1 || main[0].FileName != "main")
            throw new InvalidOperationException("回放时间线主码流优选自检失败");
        // 指定档位没有录像时回退到可用录像，避免出现假缺口。
        var fallback = Normalize([mixed[0]], start, start.AddHours(1), 2);
        if (fallback.Count != 1 || fallback[0].StreamType != 1)
            throw new InvalidOperationException("回放时间线码流回退自检失败");
    }
}
