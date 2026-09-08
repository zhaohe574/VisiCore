using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace VideoPlatform.Infrastructure.Jobs;

public sealed record AdapterResource(Guid Id, long DeviceId, string Kind, string State);
public sealed record AdapterInventory(Guid BootId, IReadOnlyList<AdapterResource> Resources)
{
    // 对应适配器 DeviceRegistry.SessionsAsync 和 ExportService.Snapshot 的实际结构。
    public static AdapterInventory Parse(JsonNode? value)
    {
        if (value is not JsonObject root || !Guid.TryParse(root.Text("bootId"), out var bootId) || bootId == Guid.Empty || root["devices"] is not JsonArray)
            throw new InvalidDataException("适配器资源清单格式无效，已停止本轮资源核对。");
        var resources = new List<AdapterResource>();
        foreach (var kind in new[] { "live", "playback", "exports" })
        {
            if (root[kind] is not JsonArray entries) throw new InvalidDataException($"适配器资源清单缺少 {kind} 数组。");
            foreach (var entry in entries)
            {
                if (entry is not JsonObject row || !Guid.TryParse(row.Text(kind == "exports" ? "jobId" : "sessionId"), out var id) || id == Guid.Empty || row.Id("deviceId") <= 0)
                    throw new InvalidDataException("适配器资源标识无效，已停止本轮资源核对。");
                resources.Add(new(id, row.Id("deviceId"), kind, row.Text("state")));
            }
        }
        if (resources.Select(r => (r.Kind, r.DeviceId, r.Id)).Distinct().Count() != resources.Count)
            throw new InvalidDataException("适配器资源清单包含重复标识。");
        return new(bootId, resources);
    }
}

public sealed record AdapterExportStatus(string State, int Progress, string? Path, string? Error)
{
    public static AdapterExportStatus Parse(JsonNode? value)
    {
        if (value is not JsonObject row || row.Text("state") is not ("queued" or "running" or "completed" or "failed" or "cancelled") ||
            !int.TryParse(row.Text("progress"), out var progress) || progress is < 0 or > 100)
            throw new InvalidDataException("适配器导出状态格式无效。");
        return new(row.Text("state"), progress, row["path"]?.ToString(), row["error"]?.ToString());
    }
}

public sealed record IngestedAlarm(string SourceId, int? Channel, string EventType, DateTimeOffset OccurredAt,
    bool Recovered, string Payload, byte[]? Image, string? Warning);

public sealed record AdapterAlarmBatch(IReadOnlyList<IngestedAlarm> Items, string NextCursor)
{
    private static (long Segment, long Offset) Cursor(string value)
    {
        var pieces = value.Split(':');
        if (pieces.Length != 2 || !long.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var segment) ||
            !long.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
            throw new InvalidDataException("适配器报警游标格式无效。");
        return (segment, offset);
    }

    public static AdapterAlarmBatch Parse(long deviceId, string previous, JsonNode? value, DateTimeOffset receivedAt)
    {
        if (value is not JsonObject page || page["items"] is not JsonArray items || items.Count > 100 || page["nextCursor"] is null)
            throw new InvalidDataException("适配器报警批次格式无效，未确认日志。");
        var next = page.Text("nextCursor");
        var comparison = Cursor(next).CompareTo(Cursor(previous));
        if (comparison < 0 || comparison == 0 && items.Count > 0)
            throw new InvalidDataException("适配器报警游标倒退或未前进，未确认日志。");
        var parsed = new List<IngestedAlarm>();
        for (var index = 0; index < items.Count; index++)
        {
            var raw = items[index];
            var rawText = raw?.ToJsonString() ?? "null";
            var row = raw as JsonObject;
            var source = row.Text("id");
            string? warning = null;
            if (string.IsNullOrWhiteSpace(source) || source.Length > 512)
            {
                source = "quarantine:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{previous}:{index}:{rawText}")));
                warning = "报警缺少有效源标识，已保留原始记录。";
            }
            var type = row.Text("eventType");
            var channel = row?["channel"] is null ? (int?)null : int.TryParse(row.Text("channel"), out var number) && number is > 0 and <= 65535 ? number : -1;
            var occurredValid = DateTimeOffset.TryParse(row.Text("occurredAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred);
            if (row is null || row.Id("deviceId") != deviceId || channel == -1 || type.Length is 0 or > 128 || !occurredValid ||
                !bool.TryParse(row.Text("recovered"), out _))
                warning = "报警字段损坏或设备标识不匹配，已隔离并保留原始记录。";
            byte[]? image = null;
            if (row?["imageBase64"] is not null)
            {
                try
                {
                    if (row.Text("imageBase64").Length > 16 * 1024 * 1024) throw new FormatException();
                    image = Convert.FromBase64String(row.Text("imageBase64"));
                }
                catch (FormatException) { warning ??= "报警图片编码无效，原始内容已保留。"; }
            }
            if (warning is not null)
            {
                parsed.Add(new(source, null, "system.adapter_record_invalid", occurredValid ? occurred : receivedAt, false,
                    new JsonObject { ["raw"] = raw?.DeepClone(), ["ingestionError"] = warning }.ToJsonString(), image, warning));
            }
            else parsed.Add(new(source, channel, type, occurred, row.Flag("recovered"), rawText, image, null));
        }
        return new(parsed, next);
    }
}
