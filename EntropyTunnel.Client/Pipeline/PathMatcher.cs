namespace EntropyTunnel.Client.Pipeline;

/// <summary>
/// Lightweight glob-style path matcher used by all pipeline stages.
///
/// Supported patterns:
///   *  or  **          → match any path
///   /api/*             → match any path starting with /api/
///   /api/checkout      → exact match (case-insensitive)
/// </summary>
public static class PathMatcher
{
    public static bool Matches(string path, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;

        // Strip query string — rules target the path only
        var pathOnly = path.Contains('?') ? path[..path.IndexOf('?')] : path;

        // Universal wildcards
        if (pattern is "*" or "**") return true;

        // Prefix wildcard: /api/* matches /api/ and /api/anything
        if (pattern.EndsWith("/*"))
        {
            var prefix = pattern[..^2]; // strip trailing /*
            return pathOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Trailing ** for deeper nesting: /api/** matches /api/v1/users
        if (pattern.EndsWith("/**"))
        {
            var prefix = pattern[..^3];
            return pathOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Exact match
        return string.Equals(pathOnly, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
