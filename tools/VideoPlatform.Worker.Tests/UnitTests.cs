using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Jobs;
using Xunit;

namespace VideoPlatform.Worker.Tests;

public sealed class UnitTests
{
    [Fact]
    public void InventoryRequiresVerifiedArraysAndIdentifiers()
    {
        var id = Guid.NewGuid();
        var valid = JsonNode.Parse($$"""{"bootId":"{{Guid.NewGuid()}}","devices":[],"live":[{"id":"{{id}}","sessionId":"{{id}}","deviceId":2,"state":"playing"}],"playback":[],"exports":[]}""")!;
        var inventory = AdapterInventory.Parse(valid);
        Assert.Equal(new AdapterResource(id, 2, "live", "playing"), Assert.Single(inventory.Resources));
        valid.AsObject().Remove("playback");
        Assert.Throws<InvalidDataException>(() => AdapterInventory.Parse(valid));
        Assert.Throws<InvalidDataException>(() => AdapterInventory.Parse(new JsonArray()));
    }

    [Fact]
    public void UnknownAlarmPreservesRawPayloadAndImage()
    {
        var page = JsonNode.Parse("""{"items":[{"id":"2:1:0","deviceId":2,"channel":7,"eventType":"alarm.sdk_99999","occurredAt":"2026-09-07T00:00:00Z","recovered":false,"payload":{"payloadBase64":"AQID","channels":[7,8]},"imageBase64":"BAUG"}],"nextCursor":"1:100"}""");
        var item = Assert.Single(AdapterAlarmBatch.Parse(2, "0:0", page, DateTimeOffset.UtcNow).Items);
        Assert.Equal("alarm.sdk_99999", item.EventType);
        Assert.Equal(7, item.Channel);
        Assert.Equal("AQID", JsonNode.Parse(item.Payload)?["payload"]?["payloadBase64"]?.ToString());
        Assert.Equal(new byte[] { 4, 5, 6 }, item.Image);
        Assert.Null(item.Warning);
    }

    [Fact]
    public void DamagedEventIsPreservedAndDoesNotDiscardFollowingRecord()
    {
        var page = JsonNode.Parse("""{"items":[42,{"id":"a","deviceId":99}],"nextCursor":"1:100"}""");
        var batch = AdapterAlarmBatch.Parse(2, "0:0", page, DateTimeOffset.UtcNow);
        Assert.Equal(2, batch.Items.Count);
        Assert.All(batch.Items, i => Assert.Equal("system.adapter_record_invalid", i.EventType));
        Assert.Equal("42", JsonNode.Parse(batch.Items[0].Payload)?["raw"]?.ToJsonString());
        Assert.Equal(batch.Items[0].SourceId, AdapterAlarmBatch.Parse(2, "0:0", page, DateTimeOffset.UtcNow).Items[0].SourceId);
    }

    [Theory]
    [InlineData("1:99")]
    [InlineData("1:100")]
    [InlineData("bad")]
    public void CursorCannotRegressOrRepeatWithRecords(string next)
    {
        var page = new JsonObject { ["items"] = new JsonArray(42), ["nextCursor"] = next };
        Assert.Throws<InvalidDataException>(() => AdapterAlarmBatch.Parse(2, "1:100", page, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(100, 100, 1000, null)]
    [InlineData(100, 99, 1000, "10%")]
    [InlineData(1073741824L, 500, 1000, "配额")]
    public void StorageLimitsAreInclusive(long used, long free, long total, string? error)
    {
        var result = new ExportStorage(used, free, total).Rejection(1);
        if (error is null) Assert.Null(result); else Assert.Contains(error, result);
    }

    [Fact]
    public void OutputCannotEscapeJobOrUseSiblingPrefix()
    {
        using var directory = new TestFiles();
        var files = directory.Files;
        var id = Guid.NewGuid();
        var own = files.Prepare(id);
        var good = Path.Combine(own, "录像.mp4");
        File.WriteAllBytes(good, [1, 2, 3]);
        Assert.Equal(3, files.ValidateOutput(id, good).Length);
        Assert.Throws<InvalidDataException>(() => files.ValidateOutput(id, Path.Combine(files.Root, "other.mp4")));
        Assert.Throws<InvalidDataException>(() => files.ValidateOutput(id, own + "-other/file.mp4"));
        Assert.Throws<InvalidDataException>(() => files.ValidateOutput(id, "relative.mp4"));
        Assert.Throws<InvalidDataException>(() => files.ValidateOutput(id, Path.Combine(own, "empty.mp4")));
    }

    [Fact]
    public void CleanupOnlyDeletesRequestedJob()
    {
        using var directory = new TestFiles();
        var id = Guid.NewGuid();
        var own = directory.Files.Prepare(id);
        var other = directory.Files.Prepare(Guid.NewGuid());
        var state = Path.Combine(directory.Files.Root, ".tasks");
        Directory.CreateDirectory(state);
        File.WriteAllBytes(Path.Combine(own, "part.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(other, "part.mp4"), [2]);
        File.WriteAllText(Path.Combine(state, "state.json"), "{}");
        directory.Files.DeleteJobDirectory(id);
        Assert.False(Directory.Exists(own));
        Assert.True(File.Exists(Path.Combine(other, "part.mp4")));
        Assert.True(File.Exists(Path.Combine(state, "state.json")));
        directory.Files.DeleteJobDirectory(id);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"state\":\"completed\",\"progress\":101}")]
    [InlineData("{\"state\":\"unknown\",\"progress\":0}")]
    public void InvalidAdapterExportStatusFailsClosed(string value)
        => Assert.Throws<InvalidDataException>(() => AdapterExportStatus.Parse(JsonNode.Parse(value)));
}

public sealed class TestFiles : IDisposable
{
    public string Root { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".runtime", Guid.NewGuid().ToString("N")));
    public ExportFiles Files { get; }
    public TestFiles()
    {
        Files = new(new PlatformOptions(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PLATFORM_DATABASE_URL"] = "Host=localhost", ["HIK_ADAPTER_INTERNAL_KEY"] = "test", ["PLATFORM_DATA_PATH"] = Root
        }).Build()));
        Directory.CreateDirectory(Files.Root);
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
