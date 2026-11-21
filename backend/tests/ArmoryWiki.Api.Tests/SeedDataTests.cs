using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Seed;
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
        Assert.Equal(16, await db.Tags.CountAsync());
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

    [Fact]
    public async Task Manually_operated_firearms_have_no_firing_modes()
    {
        await using var db = fixture.NewDbContext();

        var modes = await db.Variants
            .Where(v => v.IsBaseVariant && v.Firearm.OperatingMechanism!.Path.StartsWith("/manual/"))
            .Select(v => v.FiringModes)
            .ToListAsync();

        Assert.NotEmpty(modes); // the Kar98k
        Assert.All(modes, m => Assert.Equal(FiringModes.None, m));
    }

    [Fact]
    public async Task Reference_seeding_never_recreates_what_an_admin_removed_but_adds_entries_it_never_seeded()
    {
        await using var db = fixture.NewDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var mechanismCount = await db.OperatingMechanisms.CountAsync();

        // An admin deleted a seeded tag: it stays deleted.
        db.Tags.Remove(await db.Tags.SingleAsync(t => t.Slug == "post-cold-war"));
        // A tag the seeder has never handled (as if just added to the code): simulated by forgetting its log entry.
        db.Tags.Remove(await db.Tags.SingleAsync(t => t.Slug == "personal-defense"));
        db.ReferenceSeedLog.Remove(await db.ReferenceSeedLog.SingleAsync(e => e.Key == "tag:personal-defense"));
        await db.SaveChangesAsync();

        await ReferenceDataSeeder.SeedAsync(db);
        await ReferenceDataSeeder.SeedAsync(db); // a second run changes nothing

        Assert.False(await db.Tags.AnyAsync(t => t.Slug == "post-cold-war"));
        Assert.True(await db.Tags.AnyAsync(t => t.Slug == "personal-defense"));
        Assert.Equal(15, await db.Tags.CountAsync());
        Assert.Equal(mechanismCount, await db.OperatingMechanisms.CountAsync());
    }
}
