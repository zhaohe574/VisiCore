using System.Text.Json;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 一次性注册码只在首次登记时读取。成功登记后立即删除，避免它长期留在配置或环境变量中。
/// </summary>
public sealed class EdgeAgentBootstrapStore(EdgeAgentOptions options)
{
    public string? ReadEnrollmentCode()
    {
        if (!string.IsNullOrWhiteSpace(options.EnrollmentCode))
        {
            return options.EnrollmentCode.Trim();
        }
        if (string.IsNullOrWhiteSpace(options.BootstrapFilePath) || !File.Exists(options.BootstrapFilePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(options.BootstrapFilePath));
            return document.RootElement.TryGetProperty("enrollmentCode", out var code) &&
                   code.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(code.GetString())
                ? code.GetString()!.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void ClearAfterEnrollment()
    {
        if (string.IsNullOrWhiteSpace(options.BootstrapFilePath))
        {
            return;
        }
        try
        {
            File.Delete(options.BootstrapFilePath);
        }
        catch (IOException)
        {
            // 只保留固定失败路径；下一次启动仍会因已登记身份忽略该文件。
        }
        catch (UnauthorizedAccessException)
        {
            // 容器只读挂载时不影响已完成登记的运行态。
        }
    }
}
