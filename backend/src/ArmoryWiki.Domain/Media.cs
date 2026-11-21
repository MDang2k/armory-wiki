namespace ArmoryWiki.Domain;

/// <summary>
/// A picture of exactly one firearm, person, organization or cartridge (GLOSSARY.md: *image*). Files are stored
/// once per <see cref="ContentHash"/>; several images may share one. The first by <see cref="SortOrder"/> is the
/// subject's thumbnail.
/// </summary>
public sealed class Image : Entity
{
    public int Id { get; set; }
    public int? FirearmId { get; set; }
    public Firearm? Firearm { get; set; }
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? CartridgeId { get; set; }
    public Cartridge? Cartridge { get; set; }
    public int SortOrder { get; set; }
    /// <summary>SHA-256 of the uploaded file, lowercase hex: the storage key.</summary>
    public required string ContentHash { get; set; }
    /// <summary>"jpg", "png" or "webp": the format the cleaned original is stored in.</summary>
    public required string OriginalExtension { get; set; }
    public string? Caption { get; set; }
    public required string Author { get; set; }
    /// <summary>One of <see cref="ImageLicenses.All"/>.</summary>
    public required string License { get; set; }
    /// <summary>Where the image came from, e.g. its Wikimedia Commons file page.</summary>
    public required string SourceUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CommonsFileName { get; set; }
}

public sealed record ImageLicense(string Id, string Name, string Url);

/// <summary>The only licenses accepted for images (GLOSSARY.md: *license*).</summary>
public static class ImageLicenses
{
    public static readonly IReadOnlyList<ImageLicense> All =
    [
        new("CC0-1.0", "CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/"),
        new("PD", "Public domain", "https://creativecommons.org/publicdomain/mark/1.0/"),
        new("CC-BY-2.0", "CC BY 2.0", "https://creativecommons.org/licenses/by/2.0/"),
        new("CC-BY-2.5", "CC BY 2.5", "https://creativecommons.org/licenses/by/2.5/"),
        new("CC-BY-3.0", "CC BY 3.0", "https://creativecommons.org/licenses/by/3.0/"),
        new("CC-BY-4.0", "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/"),
        new("CC-BY-SA-2.0", "CC BY-SA 2.0", "https://creativecommons.org/licenses/by-sa/2.0/"),
        new("CC-BY-SA-2.5", "CC BY-SA 2.5", "https://creativecommons.org/licenses/by-sa/2.5/"),
        new("CC-BY-SA-3.0", "CC BY-SA 3.0", "https://creativecommons.org/licenses/by-sa/3.0/"),
        new("CC-BY-SA-4.0", "CC BY-SA 4.0", "https://creativecommons.org/licenses/by-sa/4.0/"),
    ];

    public static ImageLicense? Find(string? id) => All.FirstOrDefault(l => l.Id == id);
}

public enum VideoStatus { Suggested, Approved, Rejected }

/// <summary>An embedded YouTube video about a firearm (GLOSSARY.md: *video*). Only approved videos are public.</summary>
public sealed class Video : Entity
{
    public int Id { get; set; }
    public int FirearmId { get; set; }
    public Firearm Firearm { get; set; } = null!;
    /// <summary>The 11-character YouTube id.</summary>
    public required string YouTubeVideoId { get; set; }
    public required string Title { get; set; }
    public string? Channel { get; set; }
    public DateTime? PublishedAt { get; set; }
    public VideoStatus Status { get; set; }
}
