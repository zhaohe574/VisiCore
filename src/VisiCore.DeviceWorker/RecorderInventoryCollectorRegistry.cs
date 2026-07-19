using VisiCore.Core;

namespace VisiCore.DeviceWorker;

public sealed class RecorderInventoryCollectorRegistry(IEnumerable<IRecorderInventoryCollector> collectors)
{
    private readonly IReadOnlyList<IRecorderInventoryCollector> _collectors = collectors.ToList();

    public IRecorderInventoryCollector? Resolve(WorkerRecorderAssignment assignment)
    {
        var matches = _collectors.Where(item => item.CanCollect(assignment)).ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"录像机 {assignment.RecorderId} 被多个采集器认领：{string.Join(", ", matches.Select(item => item.Name))}。")
        };
    }
}
