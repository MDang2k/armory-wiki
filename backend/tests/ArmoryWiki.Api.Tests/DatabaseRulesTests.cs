using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArmoryWiki.Api.Tests;

/// <summary>Rules the database itself must enforce. Each write test runs in a rolled-back transaction.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class DatabaseRulesTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Filtering_by_gas_operated_includes_every_descendant_mechanism()
    {
        await using var db = fixture.NewDbContext();
        var gas = await db.OperatingMechanisms.SingleAsync(m => m.Slug == "gas-operated");

        var firearms = await db.Firearms
            .Where(f => f.OperatingMechanism!.Path.StartsWith(gas.Path))
            .Select(f => new { f.Slug, Mechanism = f.OperatingMechanism!.Slug })
            .ToListAsync();

        Assert.Equal(
            ["direct-impingement", "long-stroke-piston", "short-stroke-piston"],
            firearms.Select(f => f.Mechanism).Distinct().Order());
        Assert.Contains(firearms, f => f.Slug == "ak-47");
        Assert.Contains(firearms, f => f.Slug == "m14");
        Assert.Contains(firearms, f => f.Slug == "m16");
        Assert.DoesNotContain(firearms, f => f.Slug is "m1911" or "karabiner-98k");
    }

    [Fact]
    public async Task A_firearm_cannot_have_a_second_base_variant()
    {
        await using var db = fixture.NewDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var ak47 = await db.Firearms.SingleAsync(f => f.Slug == "ak-47");

        db.Variants.Add(new Variant { FirearmId = ak47.Id, Slug = "second-base", Name = "Second base", IsBaseVariant = true });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ux_variants_one_base_per_firearm", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
    }

    [Fact]
    public async Task A_variant_cannot_have_a_second_primary_cartridge()
    {
        await using var db = fixture.NewDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var baseVariant = await db.Variants.SingleAsync(v => v.Firearm.Slug == "ak-47" && v.IsBaseVariant);
        var otherCartridge = await db.Cartridges.SingleAsync(c => c.Slug == "5-45x39mm");

        db.Add(new VariantCartridge { VariantId = baseVariant.Id, CartridgeId = otherCartridge.Id, IsPrimary = true });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ux_variant_cartridges_one_primary_per_variant", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
    }

    [Fact]
    public async Task Draft_firearms_are_hidden_unless_filters_are_ignored()
    {
        await using var db = fixture.NewDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var draft = new Firearm { Slug = "draft-rifle", Name = "Draft rifle", Status = PublishStatus.Draft };
        draft.Variants.Add(new Variant { Slug = "draft-rifle", Name = "Draft rifle", IsBaseVariant = true });
        db.Firearms.Add(draft);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.False(await db.Firearms.AnyAsync(f => f.Slug == "draft-rifle"));
        Assert.False(await db.Variants.AnyAsync(v => v.Slug == "draft-rifle"));
        Assert.True(await db.Firearms.IgnoreQueryFilters().AnyAsync(f => f.Slug == "draft-rifle"));
    }

    [Fact]
    public async Task Saving_sets_utc_created_and_updated_timestamps()
    {
        await using var db = fixture.NewDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);
        var tag = new Tag { Slug = "timestamp-check", Name = "Timestamp check", Group = TagGroup.Role };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        var created = tag.CreatedAt;

        tag.Name = "Renamed";
        await db.SaveChangesAsync();

        Assert.True(created >= before);
        Assert.Equal(DateTimeKind.Utc, created.Kind);
        Assert.True(tag.UpdatedAt > created);
    }
}
