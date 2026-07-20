using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class AuditedExportDownloadResult(
    Stream content,
    string contentType,
    string fileName,
    ExportDownloadAuditEntity audit,
    PlatformDbContext dbContext,
    ILogger<AuditedExportDownloadResult> logger) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var length = content.Length;
        var range = ResolveRange(httpContext.Request.Headers.Range.ToString(), length);
        if (range is null)
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers.ContentRange = $"bytes */{length}";
            await FinalizeAuditAsync(0, ExportDownloadResult.Failed);
            await content.DisposeAsync();
            return;
        }

        var (start, end) = range.Value;
        var bytesToSend = end - start + 1;
        response.StatusCode = start == 0 && end == length - 1
            ? StatusCodes.Status200OK
            : StatusCodes.Status206PartialContent;
        response.ContentType = contentType;
        response.ContentLength = bytesToSend;
        response.Headers.AcceptRanges = "bytes";
        response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        if (response.StatusCode == StatusCodes.Status206PartialContent)
        {
            response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
        }

        long bytesServed = 0;
        try
        {
            content.Position = start;
            var buffer = new byte[128 * 1024];
            var remaining = bytesToSend;
            while (remaining > 0)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), httpContext.RequestAborted);
                if (read == 0)
                {
                    throw new EndOfStreamException("导出归档文件在读取时提前结束。");
                }
                await response.Body.WriteAsync(buffer.AsMemory(0, read), httpContext.RequestAborted);
                bytesServed += read;
                remaining -= read;
            }
            await FinalizeAuditAsync(bytesServed, ExportDownloadResult.Completed);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            await FinalizeAuditAsync(bytesServed, ExportDownloadResult.Cancelled);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "录像导出下载失败：审计 {AuditId}。", audit.Id);
            await FinalizeAuditAsync(bytesServed, ExportDownloadResult.Failed);
        }
        finally
        {
            await content.DisposeAsync();
        }
    }

    private async Task FinalizeAuditAsync(long bytesServed, ExportDownloadResult result)
    {
        audit.BytesServed = bytesServed;
        audit.Result = result;
        audit.CompletedAt = DateTimeOffset.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "录像导出下载审计回写失败：审计 {AuditId}。", audit.Id);
        }
    }

    internal static (long Start, long End)? ResolveRange(string rangeHeader, long length)
    {
        if (length <= 0)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(rangeHeader))
        {
            return (0, length - 1);
        }
        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || rangeHeader.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }
        var parts = rangeHeader[6..].Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(parts[0]))
        {
            if (!long.TryParse(parts[1], out var suffixLength) || suffixLength <= 0)
            {
                return null;
            }
            return (Math.Max(0, length - suffixLength), length - 1);
        }
        if (!long.TryParse(parts[0], out var start) || start < 0 || start >= length)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            return (start, length - 1);
        }
        if (!long.TryParse(parts[1], out var requestedEnd) || requestedEnd < start)
        {
            return null;
        }
        return (start, Math.Min(requestedEnd, length - 1));
    }
}
