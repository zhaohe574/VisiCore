namespace VideoPlatform.Api;

public sealed class EdgeOperationReadinessOptions
{
    public int MaximumStatusAgeSeconds { get; init; } = 90;

    public void Validate()
    {
        if (MaximumStatusAgeSeconds is < 30 or > 600)
        {
            throw new InvalidOperationException("边缘运行态最大有效期必须在 30 至 600 秒之间。");
        }
    }
}
