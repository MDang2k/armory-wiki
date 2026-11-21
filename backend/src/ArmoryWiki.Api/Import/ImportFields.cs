using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// The import rules for one entity (GLOSSARY.md: *import*, *locked field*): a new entity takes every value; a draft
/// is refreshed except where an admin locked a field; published content only gets empty fields filled.
/// </summary>
internal sealed class ImportFields(IPublishable target, bool isNew)
{
    public bool CanWrite(string key, bool empty) =>
        isNew || (!target.LockedFields.Contains(key) && (target.Status == PublishStatus.Draft || empty));

    /// <summary>Lists are locked as a whole; on published content they are only filled when empty.</summary>
    public bool CanAddTo(string key, int count) => CanWrite(key, count == 0);

    /// <summary>Writes <paramref name="value"/> when the rules allow and it differs; returns whether it did.</summary>
    public bool Set<T>(string key, T current, T value, Action<T> assign)
    {
        // ponytail: a value removed at the source stays here; drafts are refreshed, never emptied.
        if (value is null || EqualityComparer<T>.Default.Equals(current, value) || !CanWrite(key, current is null))
        {
            return false;
        }

        assign(value);
        return true;
    }

    public bool Set(string key, string? current, string? value, Action<string> assign) =>
        Set<string?>(key, current, value, v => assign(v!)); // Set<T> never assigns null

    public void SetDate(string key, FuzzyDate? current, FuzzyDate? value, Action<FuzzyDate> assign)
    {
        if (value is not null && !Same(current, value) && CanWrite(key, current is null))
        {
            assign(value);
        }
    }

    public void AddNames(string key, List<string> current, IEnumerable<string> names)
    {
        if (!CanAddTo(key, current.Count))
        {
            return;
        }

        current.AddRange(names.Where(n => !current.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList());
    }

    private static bool Same(FuzzyDate? a, FuzzyDate b) =>
        a is not null && a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.YearTo == b.YearTo
        && a.Precision == b.Precision && a.IsApproximate == b.IsApproximate;
}
