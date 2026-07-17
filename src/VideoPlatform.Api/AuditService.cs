using System.Security.Claims;
using System.Text.Json;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class AuditService(PlatformDbContext dbContext)
{
    public Task WriteAsync(
        ClaimsPrincipal principal,
        string action,
        string resourceType,
        Guid resourceId,
        object details,
        CancellationToken cancellationToken)
    {
        Guid? actorId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : null;
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId.ToString(),
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAt = DateTimeOffset.UtcNow
        });
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
