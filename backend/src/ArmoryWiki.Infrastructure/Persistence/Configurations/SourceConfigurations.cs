using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArmoryWiki.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> b) =>
        b.Property<uint>(ArmoryWikiDbContext.Version).IsRowVersion();
}

internal sealed class CitationConfiguration : IEntityTypeConfiguration<Citation>
{
    public void Configure(EntityTypeBuilder<Citation> b)
    {
        // A cited source cannot be deleted; a deleted subject takes its citations with it.
        b.HasOne(c => c.Source).WithMany().HasForeignKey(c => c.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.Firearm).WithMany().HasForeignKey(c => c.FirearmId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(c => c.Variant).WithMany().HasForeignKey(c => c.VariantId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(c => c.Person).WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(c => c.Organization).WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(c => c.Cartridge).WithMany().HasForeignKey(c => c.CartridgeId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_citations_exactly_one_subject",
            "num_nonnulls(firearm_id, variant_id, person_id, organization_id, cartridge_id) = 1"));
    }
}
