namespace VisiCore.Core;

public static class WeComWebhookAddress
{
    public static bool TryParse(string? value, out Uri? webhookUri)
    {
        webhookUri = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate) ||
            !candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !candidate.Host.Equals("qyapi.weixin.qq.com", StringComparison.OrdinalIgnoreCase) ||
            candidate.Port != 443 ||
            !candidate.AbsolutePath.Equals("/cgi-bin/webhook/send", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            !HasKeyParameter(candidate))
        {
            return false;
        }

        webhookUri = candidate;
        return true;
    }

    private static bool HasKeyParameter(Uri uri)
    {
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = item.IndexOf('=');
            var key = separatorIndex < 0 ? item : item[..separatorIndex];
            var value = separatorIndex < 0 ? string.Empty : item[(separatorIndex + 1)..];
            if (Uri.UnescapeDataString(key).Equals("key", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(Uri.UnescapeDataString(value)))
            {
                return true;
            }
        }

        return false;
    }
}
