internal static class PlaybackTimeline
{
    public static IReadOnlyList<PlaybackSegment> Normalize(IEnumerable<RecordingSummary> recordings, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        if (rangeEnd <= rangeStart)
            return Array.Empty<PlaybackSegment>();

        var result = new List<PlaybackSegment>();
        foreach (var recording in recordings
            .Where(item => item.End > item.Start && item.End > rangeStart && item.Start < rangeEnd)
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
    }
}
