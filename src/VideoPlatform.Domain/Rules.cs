namespace VideoPlatform.Domain;

public sealed class PlatformException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class Rules
{
    public static void Require(bool valid, string message, string code = "validation.failed", int status = 400)
    {
        if (!valid) throw new PlatformException(status, code, message);
    }

    public static string Text(string? value, string label, int max = 128)
    {
        var text = value?.Trim() ?? "";
        Require(text.Length > 0 && text.Length <= max, $"{label}不能为空，且不能超过 {max} 个字符");
        return text;
    }

    public static void Password(string? value) => Require(value is { Length: >= 12 and <= 256 }, "密码长度必须为 12～256 个字符");

    public static void TimeRange(DateTimeOffset start, DateTimeOffset end, int days = 1)
        => Require(end > start && end - start <= TimeSpan.FromDays(days), $"结束时间必须晚于开始时间，范围不能超过 {days} 天");

    public static string AlarmTransition(string state, string action) => action switch
    {
        "claim" when state == "new" => "processing",
        "note" when state is "new" or "processing" or "closed" => state,
        "close" when state == "processing" => "closed",
        "reopen" when state == "closed" => "new",
        _ => throw new PlatformException(409, "alarm.transition", "报警状态已变化，请刷新后重试")
    };

    public static readonly IReadOnlyDictionary<string, (string Name, string[] Codes)> DefaultRoles =
        new Dictionary<string, (string, string[])>
        {
            ["admin"] = ("系统管理员", []),
            ["operator"] = ("值班员", ["channel.read", "device.read", "area.read", "live.view", "playback.view", "alarm.read", "alarm.ack", "export.create"])
        };

    public static readonly IReadOnlyDictionary<string, string> Permissions = new Dictionary<string, string>
    {
        ["user.read"] = "查看账号", ["user.manage"] = "管理账号", ["role.read"] = "查看角色", ["role.manage"] = "管理角色",
        ["device.read"] = "查看设备", ["device.manage"] = "管理设备", ["plugin.read"] = "查看驱动插件", ["plugin.manage"] = "管理驱动插件",
        ["channel.read"] = "查看通道", ["channel.assign"] = "分配通道",
        ["area.read"] = "查看业务结构", ["area.manage"] = "管理业务结构", ["live.view"] = "实时预览", ["playback.view"] = "录像回放",
        ["ptz.control"] = "云台控制", ["alarm.read"] = "查看报警", ["alarm.ack"] = "处理报警", ["statistics.read"] = "查看统计",
        ["audit.read"] = "查看审计", ["session.manage"] = "管理在线会话", ["settings.manage"] = "管理系统配置",
        ["layout.share"] = "发布共享轮巡", ["export.create"] = "导出录像", ["export.manage"] = "管理导出任务", ["desktop.release.manage"] = "管理客户端版本"
    };
}
