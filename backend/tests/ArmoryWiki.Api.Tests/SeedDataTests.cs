using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

[Collection(nameof(DatabaseCollection))]
public sealed class SeedDataTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Seeder_populates_a_fresh_database()
    {
        await using var db = fixture.NewDbContext();

        Assert.Equal(13, await db.Countries.CountAsync());
        Assert.Equal(19, await db.OperatingMechanisms.CountAsync());
        Assert.Equal(15, await db.Tags.CountAsync());
        Assert.Equal(12, await db.Cartridges.CountAsync());
        Assert.Equal(6, await db.People.CountAsync());
        Assert.Equal(7, await db.Organizations.CountAsync());
        Assert.Equal(15, await db.Firearms.CountAsync());

        // Every seeded firearm has exactly one base variant with a primary cartridge.
        var firearmsWithBaseAndPrimary = await db.Firearms
            .CountAsync(f => f.Variants.Count(v => v.IsBaseVariant) == 1
                && f.Variants.Any(v => v.IsBaseVariant && v.Chamberings.Any(c => c.IsPrimary)));
        Assert.Equal(15, firearmsWithBaseAndPrimary);
    }
}
