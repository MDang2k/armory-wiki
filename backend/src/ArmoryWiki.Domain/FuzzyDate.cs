namespace ArmoryWiki.Domain;

/// <summary>
/// A historical date that may only be known to the month, year or decade,
/// optionally approximate ("c. 1940s") and optionally a range ("1942–1944").
/// </summary>
public sealed class FuzzyDate
{
    /// <summary>Earliest or best-known year.</summary>
    public int Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    /// <summary>End year for ranges.</summary>
    public int? YearTo { get; set; }
    public DatePrecision Precision { get; set; }
    /// <summary>"circa": independent of <see cref="Precision"/>.</summary>
    public bool IsApproximate { get; set; }

    public static FuzzyDate InYear(int year, bool approximate = false) =>
        new() { Year = year, Precision = DatePrecision.Year, IsApproximate = approximate };

    public static FuzzyDate OnDay(int year, int month, int day) =>
        new() { Year = year, Month = month, Day = day, Precision = DatePrecision.Day };

    /// <summary>"The 1950s" is InDecade(1950), stored as the range 1950–1959 so year filters find it.</summary>
    public static FuzzyDate InDecade(int firstYear) =>
        firstYear % 10 == 0
            ? new() { Year = firstYear, YearTo = firstYear + 9, Precision = DatePrecision.Decade }
            : throw new ArgumentOutOfRangeException(nameof(firstYear), firstYear, "A decade starts at a year ending in 0.");
}
