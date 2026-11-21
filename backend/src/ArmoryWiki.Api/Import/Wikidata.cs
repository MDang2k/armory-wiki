using System.Globalization;
using System.Text.Json;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Import;

/// <summary>The Wikidata properties and items the importer reads.</summary>
public static class Wd
{
    public const string InstanceOf = "P31", SubclassOf = "P279", Inception = "P571", DesignedBy = "P287",
        Manufacturer = "P176", CountryOfOrigin = "P495", Ammunition = "P739", BasedOn = "P144", Length = "P2043",
        Mass = "P2067", MuzzleVelocity = "P4137", RateOfFire = "P3792", EffectiveRange = "P4176",
        BirthDate = "P569", DeathDate = "P570", Citizenship = "P27", Country = "P17", Dissolved = "P576",
        AppliesToPart = "P518", SourcingCircumstances = "P1480";

    public const string Human = "Q5", GunBarrel = "Q487192", Circa = "Q5727902";
    /// <summary>The proleptic Julian calendar model of Wikidata time values.</summary>
    public const string JulianCalendar = "Q1985786";

    public static string ItemUrl(string qid) => $"https://www.wikidata.org/wiki/{qid}";
}

/// <summary>One item as wbgetentities returns it, reduced to what the importer reads. Pure: no I/O.</summary>
public sealed class WikidataEntity
{
    private readonly Dictionary<string, List<JsonElement>> _claims;

    private WikidataEntity(string qid, Dictionary<string, List<JsonElement>> claims) => (Qid, _claims) = (qid, claims);

    public string Qid { get; }
    /// <summary>English label, falling back to the language-neutral "mul" label that many items now use instead.</summary>
    public string? Label { get; private init; }
    public IReadOnlyList<string> Aliases { get; private init; } = [];
    public string? EnwikiTitle { get; private init; }

    /// <summary>
    /// Parses a wbgetentities response. Results are keyed by the QID that was asked for: a merged item answers with
    /// the item it was merged into (<c>Qid</c> differs from the key), a deleted one with null.
    /// </summary>
    public static Dictionary<string, WikidataEntity?> ParseResponse(JsonElement root)
    {
        var result = new Dictionary<string, WikidataEntity?>();
        foreach (var entry in root.GetProperty("entities").EnumerateObject())
        {
            result[entry.Name] = entry.Value.TryGetProperty("missing", out _) ? null : Parse(entry.Value);
        }

        return result;
    }

    public static WikidataEntity Parse(JsonElement e)
    {
        e = e.Clone(); // outlives the response document it came from
        var claims = new Dictionary<string, List<JsonElement>>();
        if (e.TryGetProperty("claims", out var all))
        {
            foreach (var property in all.EnumerateObject())
            {
                // "Truthy" statements, as SPARQL's wdt: sees them: the preferred ones if any, else the normal ones.
                var statements = property.Value.EnumerateArray().ToList();
                var preferred = statements.Where(s => s.GetProperty("rank").GetString() == "preferred").ToList();
                claims[property.Name] = preferred.Count > 0
                    ? preferred
                    : [.. statements.Where(s => s.GetProperty("rank").GetString() == "normal")];
            }
        }

        var label = Text(e, "labels", "en") ?? Text(e, "labels", "mul");
        return new WikidataEntity(e.GetProperty("id").GetString()!, claims)
        {
            Label = label,
            Aliases = [.. AliasValues(e, "en").Concat(AliasValues(e, "mul"))
                .Where(a => !string.Equals(a, label, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            EnwikiTitle = e.TryGetProperty("sitelinks", out var links) && links.TryGetProperty("enwiki", out var en)
                ? en.GetProperty("title").GetString()
                : null,
        };
    }

    /// <summary>Item values (QIDs) of a property, in Wikidata's order.</summary>
    public IReadOnlyList<string> Items(string property) =>
        [.. Values(property).Where(v => v.Value.ValueKind == JsonValueKind.Object && v.Value.TryGetProperty("id", out _))
            .Select(v => v.Value.GetProperty("id").GetString()!)];

    /// <summary>How many truthy values the property has.</summary>
    public int Count(string property) => Values(property).Count();

    /// <summary>The property's single item value, or null when it has none or several.</summary>
    public string? SingleItem(string property) => Items(property) is [var only] ? only : null;

    public bool Has(string property) => _claims.TryGetValue(property, out var s) && s.Count > 0;

    /// <summary>
    /// The property's date as a <see cref="FuzzyDate"/>, or null when there is none, several, or one we cannot
    /// represent (BCE, coarser than a decade). "circa" in the sourcing-circumstances qualifier makes it approximate.
    /// </summary>
    public FuzzyDate? Date(string property)
    {
        if (Values(property).ToList() is not [var (value, qualifiers)] || value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("time", out var timeElement))
        {
            return null;
        }

        // "+1947-00-00T00:00:00Z": month and day are 00 when unknown.
        var time = timeElement.GetString()!;
        if (!time.StartsWith('+'))
        {
            return null;
        }

        var parts = time[1..].Split('-', 3);
        var year = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var day = int.Parse(parts[2][..2], CultureInfo.InvariantCulture);
        var precision = value.GetProperty("precision").GetInt32();
        if (precision == 11 && month > 0 && day > 0 && value.TryGetProperty("calendarmodel", out var calendar)
            && calendar.GetString()!.EndsWith('/' + Wd.JulianCalendar, StringComparison.Ordinal))
        {
            // Old Style dates (Imperial Russia) are stored in Julian numbers: 12 May 1849 O.S. is 24 May 1849.
            var gregorian = new DateTime(year, month, day, new JulianCalendar());
            (year, month, day) = (gregorian.Year, gregorian.Month, gregorian.Day);
        }

        FuzzyDate? date = precision switch
        {
            11 when month > 0 && day > 0 => FuzzyDate.OnDay(year, month, day),
            10 when month > 0 => new FuzzyDate { Year = year, Month = month, Precision = DatePrecision.Month },
            9 => FuzzyDate.InYear(year),
            8 => FuzzyDate.InDecade(year - year % 10),
            _ => null,
        };
        if (date is not null && qualifiers.TryGetValue(Wd.SourcingCircumstances, out var circumstances))
        {
            date.IsApproximate = circumstances.Any(c => c.TryGetProperty("id", out var id) && id.GetString() == Wd.Circa);
        }

        return date;
    }

    /// <summary>Quantity values of a property, optionally only those whose "applies to part" qualifier is (or is absent).</summary>
    public IReadOnlyList<Quantity> Quantities(string property, Func<string?, bool>? part = null) =>
        [.. Values(property)
            .Where(v => v.Value.ValueKind == JsonValueKind.Object && v.Value.TryGetProperty("amount", out _))
            .Where(v => part is null || part(v.Qualifiers.TryGetValue(Wd.AppliesToPart, out var p) && p.Count > 0 && p[0].TryGetProperty("id", out var id) ? id.GetString() : null))
            .Select(v => new Quantity(
                decimal.Parse(v.Value.GetProperty("amount").GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture),
                v.Value.GetProperty("unit").GetString()!.Split('/')[^1]))];

    private IEnumerable<(JsonElement Value, Dictionary<string, List<JsonElement>> Qualifiers)> Values(string property)
    {
        if (!_claims.TryGetValue(property, out var statements))
        {
            yield break;
        }

        foreach (var statement in statements)
        {
            var snak = statement.GetProperty("mainsnak");
            if (snak.GetProperty("snaktype").GetString() != "value")
            {
                continue; // "unknown value" / "no value"
            }

            var qualifiers = new Dictionary<string, List<JsonElement>>();
            if (statement.TryGetProperty("qualifiers", out var q))
            {
                foreach (var p in q.EnumerateObject())
                {
                    qualifiers[p.Name] = [.. p.Value.EnumerateArray()
                        .Where(s => s.GetProperty("snaktype").GetString() == "value")
                        .Select(s => s.GetProperty("datavalue").GetProperty("value"))];
                }
            }

            yield return (snak.GetProperty("datavalue").GetProperty("value"), qualifiers);
        }
    }

    private static string? Text(JsonElement e, string group, string language) =>
        e.TryGetProperty(group, out var g) && g.TryGetProperty(language, out var l) ? l.GetProperty("value").GetString() : null;

    private static IEnumerable<string> AliasValues(JsonElement e, string language) =>
        e.TryGetProperty("aliases", out var a) && a.TryGetProperty(language, out var list)
            ? list.EnumerateArray().Select(x => x.GetProperty("value").GetString()!)
            : [];
}

public sealed record Quantity(decimal Amount, string UnitQid);

/// <summary>A firearm's facts as the import will write them, plus what was skipped and why.</summary>
public sealed record FirearmFacts(
    string Name,
    IReadOnlyList<string> AlternativeNames,
    FuzzyDate? DesignDate,
    string? WikipediaTitle,
    string? CountryQid,
    IReadOnlyList<string> ClassQids,
    IReadOnlyList<string> DesignerQids,
    IReadOnlyList<string> ManufacturerQids,
    IReadOnlyList<string> AmmunitionQids,
    IReadOnlyList<string> BasedOnQids,
    int? OverallLengthMm,
    int? BarrelLengthMm,
    int? MuzzleVelocityMps,
    int? RateOfFireMinRpm,
    int? RateOfFireMaxRpm,
    int? EffectiveRangeM,
    IReadOnlyDictionary<string, string> ExtraSpecs,
    IReadOnlyList<string> Skipped);

/// <summary>Pure mapping from Wikidata items to the importer's facts (PLAN.md Phase 4a).</summary>
public static class WikidataMapper
{
    // Factor to the stored metric unit, per unit QID.
    private static readonly Dictionary<string, decimal> ToMm = new()
    {
        ["Q174789"] = 1m, ["Q174728"] = 10m, ["Q11573"] = 1000m, ["Q218593"] = 25.4m, ["Q3710"] = 304.8m,
    };
    private static readonly Dictionary<string, decimal> ToMps = new()
    {
        ["Q182429"] = 1m, ["Q748716"] = 0.3048m, ["Q180154"] = 1m / 3.6m,
    };
    private static readonly Dictionary<string, decimal> ToM = new()
    {
        ["Q11573"] = 1m, ["Q828224"] = 1000m, ["Q482798"] = 0.9144m,
    };
    private static readonly Dictionary<string, decimal> ToRpm = new() { ["Q38623562"] = 1m };
    private static readonly Dictionary<string, string> MassUnits = new()
    {
        ["Q11570"] = "kg", ["Q41803"] = "g", ["Q100995"] = "lb", ["Q48013"] = "oz",
    };

    /// <summary>Key in <see cref="Variant.ExtraSpecs"/> for a mass whose basis is not stated (GLOSSARY.md: *empty weight*).</summary>
    public const string UnspecifiedWeight = "weight (unspecified)";

    public static FirearmFacts ToFirearm(WikidataEntity e)
    {
        var skipped = new List<string>();
        var countries = e.Items(Wd.CountryOfOrigin);
        if (countries.Count > 1)
        {
            skipped.Add($"country of origin: {countries.Count} values ({string.Join(", ", countries)})");
        }

        if (e.Count(Wd.Inception) > 1)
        {
            skipped.Add($"design date: {e.Count(Wd.Inception)} values");
        }

        var rates = Convert(e.Quantities(Wd.RateOfFire), ToRpm, "rate of fire", skipped);
        return new FirearmFacts(
            e.Label!,
            e.Aliases,
            e.Date(Wd.Inception),
            e.EnwikiTitle,
            countries is [var country] ? country : null,
            [.. e.Items(Wd.InstanceOf).Concat(e.Items(Wd.SubclassOf)).Distinct()],
            e.Items(Wd.DesignedBy),
            e.Items(Wd.Manufacturer),
            e.Items(Wd.Ammunition),
            e.Items(Wd.BasedOn),
            Single(Convert(e.Quantities(Wd.Length, part => part is null), ToMm, "length", skipped), "length", skipped),
            Single(Convert(e.Quantities(Wd.Length, part => part == Wd.GunBarrel), ToMm, "barrel length", skipped), "barrel length", skipped),
            Single(Convert(e.Quantities(Wd.MuzzleVelocity), ToMps, "muzzle velocity", skipped), "muzzle velocity", skipped),
            rates.Count > 0 ? rates.Min() : null,
            rates.Count > 0 ? rates.Max() : null,
            Single(Convert(e.Quantities(Wd.EffectiveRange), ToM, "effective range", skipped), "effective range", skipped),
            Weight(e, skipped),
            skipped);
    }

    // ponytail: Wikidata states no basis (empty/loaded) on firearm masses today, so every mass is an unspecified
    // weight and WeightEmptyG is never imported. Map a "criterion used: unloaded" qualifier here if one appears.
    private static Dictionary<string, string> Weight(WikidataEntity e, List<string> skipped)
    {
        var masses = e.Quantities(Wd.Mass).Where(q =>
        {
            var known = MassUnits.ContainsKey(q.UnitQid);
            if (!known)
            {
                skipped.Add($"mass: unknown unit {q.UnitQid}");
            }

            return known;
        }).ToList();
        if (masses.Count > 1)
        {
            skipped.Add($"mass: {masses.Count} values");
        }

        return masses is [var m]
            ? new() { [UnspecifiedWeight] = $"{m.Amount.ToString("0.###", CultureInfo.InvariantCulture)} {MassUnits[m.UnitQid]}" }
            : [];
    }

    private static List<int> Convert(IReadOnlyList<Quantity> values, Dictionary<string, decimal> factors, string what, List<string> skipped)
    {
        var result = new List<int>();
        foreach (var q in values)
        {
            if (!factors.TryGetValue(q.UnitQid, out var factor))
            {
                skipped.Add($"{what}: unknown unit {q.UnitQid}");
            }
            else if (q.Amount <= 0)
            {
                skipped.Add($"{what}: non-positive value {q.Amount}");
            }
            else
            {
                result.Add((int)Math.Round(q.Amount * factor, MidpointRounding.AwayFromZero));
            }
        }

        return result;
    }

    private static int? Single(List<int> values, string what, List<string> skipped)
    {
        if (values.Count > 1)
        {
            skipped.Add($"{what}: {values.Count} values");
        }

        return values is [var only] ? only : null;
    }
}
