using System.Text.RegularExpressions;

namespace VisiCore.Persistence;

/// <summary>
/// 统一平台登录名格式。邮箱规范化为小写，普通账号保留大小写。
/// </summary>
public static class PlatformUsernamePolicy
{
    private static readonly Regex RegularAccountPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new(
        "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
        RegexOptions.CultureInvariant);

    public static bool TryNormalize(string? value, out string username, out string validationError)
    {
        username = string.Empty;
        validationError = "账号必须是 1 至 64 位的普通账号或邮箱地址。";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length is < 1 or > 64 || candidate.Any(char.IsControl))
        {
            return false;
        }

        if (EmailPattern.IsMatch(candidate))
        {
            username = candidate.ToLowerInvariant();
            return true;
        }

        if (!RegularAccountPattern.IsMatch(candidate))
        {
            return false;
        }

        username = candidate;
        return true;
    }
}
