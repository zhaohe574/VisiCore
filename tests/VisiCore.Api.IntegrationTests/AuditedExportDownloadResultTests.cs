using VisiCore.Api;
using Xunit;

namespace VisiCore.Api.IntegrationTests;

public sealed class AuditedExportDownloadResultTests
{
    [Theory(DisplayName = "导出下载 Range 仅接受单个有效字节范围")]
    [InlineData("", 100L, 0L, 99L)]
    [InlineData("bytes=10-29", 100L, 10L, 29L)]
    [InlineData("bytes=90-", 100L, 90L, 99L)]
    [InlineData("bytes=-12", 100L, 88L, 99L)]
    [InlineData("bytes=90-200", 100L, 90L, 99L)]
    public void ResolveRangeAcceptsSupportedRange(string header, long length, long expectedStart, long expectedEnd)
    {
        var range = AuditedExportDownloadResult.ResolveRange(header, length);

        Assert.NotNull(range);
        Assert.Equal((expectedStart, expectedEnd), range.Value);
    }

    [Theory(DisplayName = "导出下载拒绝空文件、多范围和越界范围")]
    [InlineData("bytes=0-1", 0L)]
    [InlineData("bytes=100-101", 100L)]
    [InlineData("bytes=20-10", 100L)]
    [InlineData("bytes=0-1,3-4", 100L)]
    [InlineData("items=0-1", 100L)]
    public void ResolveRangeRejectsUnsupportedRange(string header, long length)
    {
        Assert.Null(AuditedExportDownloadResult.ResolveRange(header, length));
    }
}
