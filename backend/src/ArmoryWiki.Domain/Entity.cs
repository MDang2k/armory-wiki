namespace ArmoryWiki.Domain;

/// <summary>Base for every persisted entity. Timestamps are UTC and set by the DbContext.</summary>
public abstract class Entity
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A public entity reached by its slug.</summary>
public interface ISlugged
{
    int Id { get; }
    string Slug { get; set; }
}

/// <summary>Content with a draft/published lifecycle and fields locked against imports (GLOSSARY.md).</summary>
public interface IPublishable
{
    PublishStatus Status { get; set; }
    List<string> LockedFields { get; }
}
