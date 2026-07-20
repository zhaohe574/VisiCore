using VisiCore.Core;

namespace VisiCore.DeviceWorker;

public interface IRecorderInventoryCollector
{
    string Name { get; }

    bool CanCollect(WorkerRecorderAssignment assignment);

    Task<WorkerInventoryReport> CollectInventoryAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken);

    Task<WorkerHealthReport> CollectHealthAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken);

    Task<WorkerClockReport> CollectClockAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken);
}
