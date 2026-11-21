namespace ArmoryWiki.Infrastructure.Seed;

/// <summary>One reference-data key the seeder has handled, e.g. "tag:cold-war" or "mechanism:gas-trap".</summary>
public sealed class ReferenceSeedLogEntry
{
    public required string Key { get; set; }
    public DateTime SeededAt { get; set; }
}
