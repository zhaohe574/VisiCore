using System.Net;
using System.Net.Sockets;

namespace VideoPlatform.Core;

public static class RecorderEndpointHostPolicy
{
    public static bool IsValidHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.IndexOfAny(['@', '/', '\\', '?', '#', '[', ']']) >= 0)
        {
            return false;
        }

        if (IPAddress.TryParse(value, out var address))
        {
            return address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId == 0;
        }

        if (value.Contains(':') || value.EndsWith('.') ||
            value.Any(character => character > 0x7f) || Uri.CheckHostName(value) != UriHostNameType.Dns)
        {
            return false;
        }

        foreach (var label in value.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' ||
                label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }
        }
        return true;
    }
}
