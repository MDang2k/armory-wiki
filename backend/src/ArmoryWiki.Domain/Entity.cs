namespace ArmoryWiki.Domain;

/// <summary>Base for every persisted entity. Timestamps are UTC and set by the DbContext.</summary>
public abstract class Entity
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
