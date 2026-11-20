using System.Net;
using System.Text;

namespace MochiDock.Core;

/// <summary>Pure helpers for talking to a shared GOWA server: the Basic auth header and a "is this address private?" check.</summary>
public static class GowaRemotePolicy
{
    /// <summary>"Basic base64(user:password)" or null when there is no user name.</summary>
    public static string? BasicAuthHeader(string? user, string? password)
    {
        if (string.IsNullOrWhiteSpace(user)) return null;
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user.Trim() + ":" + (password ?? "")));
    }

    /// <summary>
    /// True for loopback, LAN (10/8, 172.16/12, 192.168/16), link-local, Tailscale (100.64/10), *.local / *.ts.net / single-label host names.
    /// A server outside these ranges without a password would be open to the internet, so the UI warns about it.
    /// </summary>
    public static bool IsPrivateAddress(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var text = url.Trim();
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return false;

        var host = uri.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.');
    }
}
