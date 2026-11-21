using System.Linq.Expressions;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Common;

public sealed record SourceDto(SourceType Type, string Title, string? Author, string? Publisher, int? Year, string? Url, string? Isbn);

/// <summary>
/// One citation, shown as a footnote. <see cref="FieldName"/> is the cited field (null: the whole subject);
/// <see cref="Variant"/> is set when a variant, not the firearm itself, is cited.
/// </summary>
public sealed record CitationDto(SourceDto Source, string? FieldName, string? Locator, string? Variant);

public static class CitationProjection
{
    /// <summary>Citations in the order they were added, so footnote numbers stay stable.</summary>
    public static readonly Expression<Func<Citation, CitationDto>> ToDto = c => new CitationDto(
        new SourceDto(c.Source.Type, c.Source.Title, c.Source.Author, c.Source.Publisher, c.Source.Year, c.Source.Url, c.Source.Isbn),
        c.FieldName,
        c.Locator,
        c.Variant == null ? null : c.Variant.Slug);
}
