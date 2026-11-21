using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArmoryWiki.Domain;

/// <summary>URL-safe identifiers: lowercase ASCII letters and digits separated by single hyphens.</summary>
public static partial class Slug
{
    public const string Pattern = @"^[a-z0-9]+(-[a-z0-9]+)*\z"; // \z, not $: $ also matches before a trailing newline

    /// <summary>Firearm slugs that would collide with API routes under /api/firearms.</summary>
    public static readonly IReadOnlySet<string> ReservedFirearmSlugs = new HashSet<string> { "facets", "compare" };

    public static bool IsValid(string? value) => value is not null && ValidSlug().IsMatch(value);

    /// <summary>"7.62×39mm" → "7-62x39mm", "Škorpion vz. 61" → "skorpion-vz-61", "Автомат Калашникова" → "avtomat-kalashnikova".</summary>
    public static string FromName(string name)
    {
        var ascii = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            // Look letters up before decomposing them: FormD would turn й into и + breve.
            if (Transliterations.TryGetValue(c, out var latin))
            {
                ascii.Append(latin);
                continue;
            }

            foreach (var part in c.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)
                {
                    ascii.Append(part); // accents split off by FormD are dropped: š → s + ˇ
                }
            }
        }

        return NonAlphanumeric().Replace(ascii.ToString(), "-").Trim('-');
    }

    /// <summary>Letters FormD cannot reduce to ASCII: the multiplication sign, a few Latin letters, and Cyrillic.</summary>
    private static readonly Dictionary<char, string> Transliterations = new()
    {
        ['×'] = "x", ['ß'] = "ss", ['ø'] = "o", ['æ'] = "ae", ['œ'] = "oe", ['ł'] = "l", ['đ'] = "d", ['þ'] = "th",
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya", ['і'] = "i", ['ї'] = "i", ['є'] = "ye", ['ґ'] = "g",
    };

    [GeneratedRegex(Pattern)]
    private static partial Regex ValidSlug();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
