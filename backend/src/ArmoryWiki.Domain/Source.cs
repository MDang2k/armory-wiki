namespace ArmoryWiki.Domain;

public enum SourceType { Book, Article, Website, Patent, Manual, Video, Dataset }

/// <summary>A published work facts are taken from (GLOSSARY.md: *source*).</summary>
public sealed class Source : Entity
{
    public int Id { get; set; }
    public SourceType Type { get; set; }
    public required string Title { get; set; }
    public string? Author { get; set; }
    public string? Publisher { get; set; }
    public int? Year { get; set; }
    public string? Url { get; set; }
    public string? Isbn { get; set; }
}

/// <summary>
/// Links exactly one subject (firearm, variant, person, organization or cartridge), optionally one of its fields,
/// to a source (GLOSSARY.md: *citation*). Deleting the subject deletes its citations.
/// </summary>
public sealed class Citation : Entity
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public Source Source { get; set; } = null!;
    public int? FirearmId { get; set; }
    public Firearm? Firearm { get; set; }
    public int? VariantId { get; set; }
    public Variant? Variant { get; set; }
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? CartridgeId { get; set; }
    public Cartridge? Cartridge { get; set; }
    /// <summary>The cited field, by its API name (e.g. "designDate"); null cites the subject as a whole.</summary>
    public string? FieldName { get; set; }
    /// <summary>Where in the source: "p. 112", "claim 3", "12:40", or a deep link.</summary>
    public string? Locator { get; set; }
}
