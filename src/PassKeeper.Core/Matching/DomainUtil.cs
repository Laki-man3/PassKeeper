using System.Net;

namespace PassKeeper.Core.Matching;

public static class DomainUtil
{
    // Second-level public suffixes that are common in RU/CIS/EU and elsewhere (offline, no PSL download).
    private static readonly HashSet<string> MultiPartSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "gov.uk", "ac.uk", "com.au", "net.au", "org.au", "co.nz", "co.jp", "ne.jp", "or.jp",
        "com.br", "com.cn", "com.tr", "com.ua", "org.ua", "net.ua", "gov.ua", "in.ua", "kiev.ua", "com.ru", "net.ru",
        "org.ru", "pp.ru", "msk.ru", "spb.ru", "msk.su", "com.kz", "org.kz", "gov.kz", "com.by", "gov.by", "com.pl",
        "co.in", "co.za", "com.mx", "com.ar", "co.kr", "com.sg", "com.hk", "com.tw", "gov.ru", "edu.ru", "mil.ru",
    };

    /// <summary>Extracts a lower-case host from a URL, a bare domain or an Android/app identifier.</summary>
    public static string? GetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith("androidapp://", StringComparison.OrdinalIgnoreCase)) return null;
        if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https" or "ftp" or "ftps" or "ws" or "wss")) return null;
        // Punycode form so that "почта.рф" and "xn--80a1acny.xn--p1ai" compare equal.
        var host = uri.IdnHost.Trim('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host.Length == 0 ? null : host;
    }

    /// <summary>Registrable domain ("login.mail.example.co.uk" → "example.co.uk").</summary>
    public static string GetBaseDomain(string host)
    {
        if (IPAddress.TryParse(host, out _) || !host.Contains('.')) return host;
        var labels = host.Split('.');
        if (labels.Length <= 2) return host;
        var lastTwo = labels[^2] + "." + labels[^1];
        if (MultiPartSuffixes.Contains(lastTwo) && labels.Length >= 3)
            return labels[^3] + "." + lastTwo;
        return lastTwo;
    }

    /// <returns>100 for the same host, 80 for a sub-/parent domain, 70 for the same registrable domain, otherwise 0.</returns>
    public static int CompareHosts(string pageHost, string entryHost)
    {
        if (pageHost.Equals(entryHost, StringComparison.OrdinalIgnoreCase)) return 100;
        if (pageHost.EndsWith("." + entryHost, StringComparison.OrdinalIgnoreCase)) return 80;
        if (entryHost.EndsWith("." + pageHost, StringComparison.OrdinalIgnoreCase)) return 75;
        if (IPAddress.TryParse(pageHost, out _) || IPAddress.TryParse(entryHost, out _)) return 0;
        return GetBaseDomain(pageHost).Equals(GetBaseDomain(entryHost), StringComparison.OrdinalIgnoreCase) ? 70 : 0;
    }

    public static string? NormalizeUrlForOpen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u.AbsoluteUri : null;
    }
}
