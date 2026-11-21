using System.Text.Json;
using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace ArmoryWiki.Infrastructure.Persistence.Configurations;

internal sealed class FirearmConfiguration : IEntityTypeConfiguration<Firearm>
{
    public void Configure(EntityTypeBuilder<Firearm> b)
    {
        b.HasIndex(f => f.Slug).IsUnique();
        b.HasIndex(f => f.WikidataId).IsUnique();
        b.HasIndex(f => f.Status);
        // Postgres's xmin system column: the firearm's version for optimistic concurrency. Every admin write to
        // the firearm or its children touches this row, so one version guards the whole firearm.
        b.Property<uint>(ArmoryWikiDbContext.Version).IsRowVersion();
        b.Property(f => f.Summary).HasMaxLength(500);
        b.OwnsOne(f => f.DesignDate, d => d.HasIndex(x => x.Year));
        b.HasQueryFilter(f => f.Status == PublishStatus.Published);

        b.HasMany(f => f.Tags).WithMany().UsingEntity<FirearmTag>(
            r => r.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId),
            l => l.HasOne<Firearm>().WithMany().HasForeignKey(x => x.FirearmId),
            j => j.HasKey(x => new { x.FirearmId, x.TagId }));

        // Generated columns need IMMUTABLE expressions; array_to_string is only STABLE, so the
        // initial migration creates armorywiki_immutable_array_to_string() first.
        b.Property<NpgsqlTsVector>(ArmoryWikiDbContext.SearchVector)
            .HasComputedColumnSql(
                "setweight(to_tsvector('simple', name), 'A') || "
                + "setweight(to_tsvector('simple', armorywiki_immutable_array_to_string(alternative_names)), 'B') || "
                + "setweight(to_tsvector('simple', coalesce(summary, '')), 'C')",
                stored: true);
        b.HasIndex(ArmoryWikiDbContext.SearchVector).HasMethod("gin");

        b.Property<string>(ArmoryWikiDbContext.SearchNames)
            .HasComputedColumnSql(
                "lower(name || ' ' || armorywiki_immutable_array_to_string(alternative_names))",
                stored: true);
        b.HasIndex(ArmoryWikiDbContext.SearchNames).HasMethod("gin").HasOperators("gin_trgm_ops");
    }
}

internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> b)
    {
        b.HasQueryFilter(v => v.Firearm.Status == PublishStatus.Published);
        b.HasIndex(v => new { v.FirearmId, v.Slug }).IsUnique();
        b.HasIndex(v => v.FirearmId)
            .IsUnique()
            .HasFilter("is_base_variant")
            .HasDatabaseName("ux_variants_one_base_per_firearm");

        b.Property(v => v.ExtraSpecs)
            .HasColumnType("jsonb")
            .HasConversion(
                d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions?)null)
                    ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (x, y) => x!.Count == y!.Count && !x.Except(y).Any(),
                    // XOR keeps the hash independent of key order (jsonb does not preserve it).
                    d => d.Aggregate(0, (hash, kv) => hash ^ HashCode.Combine(kv.Key, kv.Value)),
                    d => new Dictionary<string, string>(d)));
    }
}

internal sealed class VariantCartridgeConfiguration : IEntityTypeConfiguration<VariantCartridge>
{
    public void Configure(EntityTypeBuilder<VariantCartridge> b)
    {
        b.HasKey(x => new { x.VariantId, x.CartridgeId });
        b.HasQueryFilter(x => x.Variant.Firearm.Status == PublishStatus.Published
            && x.Cartridge.Status == PublishStatus.Published);
        b.HasIndex(x => x.VariantId)
            .IsUnique()
            .HasFilter("is_primary")
            .HasDatabaseName("ux_variant_cartridges_one_primary_per_variant");
    }
}

internal sealed class FirearmDesignerConfiguration : IEntityTypeConfiguration<FirearmDesigner>
{
    public void Configure(EntityTypeBuilder<FirearmDesigner> b)
    {
        b.HasQueryFilter(x => x.Firearm.Status == PublishStatus.Published);
        b.HasIndex(x => new { x.FirearmId, x.PersonId, x.OrganizationId, x.Role })
            .IsUnique()
            .AreNullsDistinct(false);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_firearm_designers_person_xor_organization",
            "num_nonnulls(person_id, organization_id) = 1"));
    }
}

internal sealed class FirearmManufacturerConfiguration : IEntityTypeConfiguration<FirearmManufacturer>
{
    public void Configure(EntityTypeBuilder<FirearmManufacturer> b) =>
        b.HasQueryFilter(x => x.Firearm.Status == PublishStatus.Published
            && x.Organization.Status == PublishStatus.Published);
}

internal sealed class AdoptionConfiguration : IEntityTypeConfiguration<Adoption>
{
    public void Configure(EntityTypeBuilder<Adoption> b) =>
        b.HasQueryFilter(x => x.Firearm.Status == PublishStatus.Published);
}

internal sealed class FirearmRelationConfiguration : IEntityTypeConfiguration<FirearmRelation>
{
    public void Configure(EntityTypeBuilder<FirearmRelation> b)
    {
        b.HasKey(x => new { x.FromFirearmId, x.ToFirearmId, x.Type });
        b.HasQueryFilter(x => x.FromFirearm.Status == PublishStatus.Published
            && x.ToFirearm.Status == PublishStatus.Published);
        b.HasOne(x => x.FromFirearm).WithMany(f => f.RelationsFrom).HasForeignKey(x => x.FromFirearmId);
        b.HasOne(x => x.ToFirearm).WithMany(f => f.RelationsTo).HasForeignKey(x => x.ToFirearmId);
        b.ToTable(t => t.HasCheckConstraint("ck_firearm_relations_not_self", "from_firearm_id <> to_firearm_id"));
    }
}
