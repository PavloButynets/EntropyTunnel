namespace EntropyTunnel.Client.Pipeline;

public static class HeaderMasker
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token",
        "X-Access-Token",
        "Proxy-Authorization",
    };

    /// <summary>
    /// Returns the same dictionary if no sensitive headers are present, or a shallow copy
    /// with sensitive values replaced by "[redacted]".  O(n) where n = header count.
    /// </summary>
    public static Dictionary<string, string>? Mask(Dictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0) return headers;

        Dictionary<string, string>? copy = null;
        foreach (var key in headers.Keys)
        {
            if (!Sensitive.Contains(key)) continue;
            copy ??= new Dictionary<string, string>(headers);
            copy[key] = "[redacted]";
        }
        return copy ?? headers;
    }
}
