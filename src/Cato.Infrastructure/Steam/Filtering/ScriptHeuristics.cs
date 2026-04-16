using System.Text.RegularExpressions;

namespace Cato.Infrastructure.Steam.Filtering;

public static class ScriptHeuristics
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static bool HasLatinLetter(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')) return true;
            // Latin-1 Supplement letters (À-ÿ, excluding math/symbols)
            if (ch >= '\u00C0' && ch <= '\u024F') return true;
        }
        return false;
    }

    public static IReadOnlyList<string> ParseSupportedLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        var stripped = HtmlTagRegex.Replace(raw, string.Empty);
        return stripped
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.TrimEnd('*').Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    public static bool HasAnyLatinScriptLanguage(
        IEnumerable<string> languages,
        IEnumerable<string> latinScriptWhitelist)
    {
        var whitelist = new HashSet<string>(latinScriptWhitelist, StringComparer.OrdinalIgnoreCase);
        foreach (var lang in languages)
        {
            if (whitelist.Contains(lang)) return true;
        }
        return false;
    }
}
