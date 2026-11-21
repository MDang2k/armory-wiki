using System.Text.Json;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// Maps a page title in a MediaWiki query answer back to every title that was asked for: the API reports what it
/// normalized ("A_b" → "A b") and, with redirects=1, which redirects it followed (a renamed page).
/// </summary>
public sealed class AskedTitles(JsonElement query)
{
    private readonly Dictionary<string, List<string>> _normalizedFrom = Reverse(query, "normalized");
    private readonly Dictionary<string, List<string>> _redirectedFrom = Reverse(query, "redirects");

    public IEnumerable<string> For(string title)
    {
        foreach (var redirect in (List<string>)[title, .. _redirectedFrom.GetValueOrDefault(title) ?? []])
        {
            foreach (var asked in _normalizedFrom.GetValueOrDefault(redirect) ?? [redirect])
            {
                yield return asked;
            }
        }
    }

    /// <summary>"to" → every "from" of the answer's normalized or redirects list.</summary>
    private static Dictionary<string, List<string>> Reverse(JsonElement query, string list)
    {
        var from = new Dictionary<string, List<string>>();
        if (query.TryGetProperty(list, out var entries))
        {
            foreach (var e in entries.EnumerateArray())
            {
                var to = e.GetProperty("to").GetString()!;
                (from.TryGetValue(to, out var names) ? names : from[to] = []).Add(e.GetProperty("from").GetString()!);
            }
        }

        return from;
    }
}
