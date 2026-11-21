using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Tests;

public sealed class SlugTests
{
    [Theory]
    [InlineData("AK-47", "ak-47")]
    [InlineData("7.62×39mm", "7-62x39mm")]
    [InlineData(".30-06 Springfield", "30-06-springfield")]
    [InlineData("Sturmgewehr 44", "sturmgewehr-44")]
    [InlineData("Škorpion vz. 61", "skorpion-vz-61")]
    [InlineData("Colt's Manufacturing Company", "colt-s-manufacturing-company")]
    [InlineData("Автомат Калашникова", "avtomat-kalashnikova")]
    [InlineData("Йошкар-Ола", "yoshkar-ola")]
    [InlineData("Heckler & Koch G3", "heckler-koch-g3")]
    [InlineData("  MKb 42(H)  ", "mkb-42-h")]
    public void FromName_produces_lowercase_ascii_hyphenated_slugs(string name, string expected)
    {
        Assert.Equal(expected, Slug.FromName(name));
        Assert.True(Slug.IsValid(expected));
    }

    [Theory]
    [InlineData("ak-47", true)]
    [InlineData("AK-47", false)]
    [InlineData("ak--47", false)]
    [InlineData("-ak", false)]
    [InlineData("", false)]
    [InlineData("ak-47\n", false)]
    public void IsValid_enforces_the_slug_format(string value, bool valid)
    {
        Assert.Equal(valid, Slug.IsValid(value));
    }
}
