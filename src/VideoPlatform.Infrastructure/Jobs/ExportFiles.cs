namespace VideoPlatform.Infrastructure.Jobs;

public sealed record ExportStorage(long UsedBytes, long FreeBytes, long TotalBytes)
{
    public string? Rejection(int quotaGb)
        => TotalBytes <= 0 ? "无法确定导出磁盘容量，暂不执行导出。"
            : FreeBytes < TotalBytes / 10 ? "导出磁盘剩余不足 10%，已暂停导出。"
            : UsedBytes >= checked(quotaGb * 1024L * 1024 * 1024) ? "导出目录已达到存储配额，已暂停导出。" : null;
}

public sealed class ExportFiles(PlatformOptions options)
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ExportsPath));
    public string JobDirectory(Guid jobId)
    {
        if (jobId == Guid.Empty) throw new InvalidDataException("导出任务标识不能为空。");
        var path = Path.Combine(Root, jobId.ToString("D"));
        RejectLinks(path);
        return path;
    }

    public string Prepare(Guid jobId)
    {
        var directory = JobDirectory(jobId);
        Directory.CreateDirectory(directory);
        RejectLinks(directory);
        return directory;
    }

    public FileInfo ValidateOutput(Guid jobId, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested))
            throw new InvalidDataException("适配器导出结果必须是完整文件路径。");
        var full = Path.GetFullPath(requested);
        var directory = JobDirectory(jobId);
        if (!full.StartsWith(directory + Path.DirectorySeparatorChar, Comparison))
            throw new InvalidDataException("适配器导出文件超出当前任务目录。");
        var relative = Path.GetRelativePath(directory, full);
        if (relative.Contains(':') || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is ".." or "."))
            throw new InvalidDataException("导出文件路径包含非法路径段。");
        RejectLinks(full);
        var file = new FileInfo(full);
        if (!file.Exists || file.Length <= 0 || !new[] { ".mp4", ".zip" }.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("导出结果不存在、为空或封装类型无效。");
        return file;
    }

    public ExportStorage Measure()
    {
        RejectLinks(Root);
        long used = 0;
        if (Directory.Exists(Root))
            foreach (var file in Walk(Root).Where(p => !p.Directory)) used = checked(used + new FileInfo(file.Path).Length);
        var drive = DriveInfo.GetDrives().Where(d => d.IsReady &&
                (Root.Equals(Path.TrimEndingDirectorySeparator(d.Name), Comparison) || Root.StartsWith(Path.TrimEndingDirectorySeparator(d.Name) + Path.DirectorySeparatorChar, Comparison)))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault() ?? new DriveInfo(Path.GetPathRoot(Root)!);
        return new(used, drive.AvailableFreeSpace, drive.TotalSize);
    }

    public void DeleteJobDirectory(Guid jobId)
    {
        var directory = JobDirectory(jobId);
        if (!Directory.Exists(directory)) return;
        // 先验证完整树，再逐项删除；绝不递归跟随联接，也不触及其他任务或适配器的 .tasks。
        var entries = Walk(directory).ToList();
        foreach (var entry in entries.Where(e => !e.Directory))
        {
            RejectLinks(entry.Path);
            File.Delete(entry.Path);
        }
        foreach (var entry in entries.Where(e => e.Directory).OrderByDescending(e => e.Path.Length))
        {
            RejectLinks(entry.Path);
            Directory.Delete(entry.Path, false);
        }
        RejectLinks(directory);
        Directory.Delete(directory, false);
    }

    private static IEnumerable<(string Path, bool Directory)> Walk(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            RejectLinks(directory);
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("导出目录存在符号链接或目录联接，已停止文件操作。");
                var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                yield return (entry.FullName, isDirectory);
                if (isDirectory) pending.Push(entry.FullName);
            }
        }
    }

    private static void RejectLinks(string path)
    {
        for (var current = new FileInfo(path); current is not null; current = current.Directory is { } parent ? new FileInfo(parent.FullName) : null)
        {
            if (current.LinkTarget is not null || (current.Exists || Directory.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("导出路径不能经过符号链接或目录联接。");
        }
    }
}
