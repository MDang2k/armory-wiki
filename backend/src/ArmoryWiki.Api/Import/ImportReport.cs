using System.Text;
using System.Text.Json;

namespace ArmoryWiki.Api.Import;

/// <summary>What one import run did: counts per kind and outcome, plus everything an admin should look at.</summary>
public sealed class ImportReport(string command, bool dryRun)
{
    public sealed record Issue(string Kind, string? Qid, string Message);

    public string Command { get; } = command;
    public bool DryRun { get; } = dryRun;
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    /// <summary>How many items the discovery query found.</summary>
    public int Discovered { get; set; }
    /// <summary>kind → outcome (created, updated, unchanged, skipped) → count.</summary>
    public SortedDictionary<string, SortedDictionary<string, int>> Counts { get; } = [];
    public List<Issue> Issues { get; } = [];
    /// <summary>The first few firearms created, so a dry run shows what it would bring in.</summary>
    public List<string> Sample { get; } = [];

    public void Count(string kind, string outcome)
    {
        var outcomes = Counts.TryGetValue(kind, out var o) ? o : Counts[kind] = [];
        outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    }

    public int CountOf(string kind, string outcome) =>
        Counts.TryGetValue(kind, out var o) ? o.GetValueOrDefault(outcome) : 0;

    public void Add(string kind, string? qid, string message) => Issues.Add(new(kind, qid, message));

    public string Summary()
    {
        var text = new StringBuilder($"{Command}{(DryRun ? " (dry run, nothing saved)" : "")}\n");
        text.AppendLine($"  {"discovered",-16} {Discovered}");
        foreach (var (kind, outcomes) in Counts)
        {
            text.AppendLine($"  {kind,-16} {string.Join(", ", outcomes.Select(o => $"{o.Value} {o.Key}"))}");
        }

        text.AppendLine($"  {"issues",-16} {Issues.Count}");
        if (Sample.Count > 0)
        {
            text.AppendLine($"  {"sample",-16} {string.Join("; ", Sample)}");
        }

        return text.ToString();
    }

    /// <summary>Writes the full report as JSON into <paramref name="directory"/> and returns the file path.</summary>
    public async Task<string> WriteAsync(string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Command}-{StartedAt:yyyyMMdd'T'HHmmss'Z'}.json");
        await using var file = File.Create(path);
        await JsonSerializer.SerializeAsync(file, this, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, ct);
        return path;
    }
}
