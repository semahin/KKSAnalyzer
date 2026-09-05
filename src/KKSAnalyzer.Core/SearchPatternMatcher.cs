namespace KKSAnalyzer.Core;

public static class SearchPatternMatcher
{
    public static bool Contains(string? value, string pattern)
    {
        if (value is null) return false;
        if (!pattern.Contains('*'))
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        var parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;

        var start = 0;
        foreach (var part in parts)
        {
            var index = value.IndexOf(part, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            start = index + part.Length;
        }

        return true;
    }
}
