if (args.Contains("--wait"))
{
    await Task.Delay(TimeSpan.FromSeconds(1));
    return;
}
await File.AppendAllTextAsync(Path.Combine(AppContext.BaseDirectory, "restarted.txt"), $"更新器恢复启动：{DateTimeOffset.Now:O}{Environment.NewLine}");
