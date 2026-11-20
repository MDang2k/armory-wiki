using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArmoryWiki.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.WikidataId).IsUnique();
        b.HasQueryFilter(x => x.Status == PublishStatus.Published);
    }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.WikidataId).IsUnique();
        b.HasQueryFilter(x => x.Status == PublishStatus.Published);
    }
}

internal sealed class CartridgeConfiguration : IEntityTypeConfiguration<Cartridge>
{
    public void Configure(EntityTypeBuilder<Cartridge> b)
    {
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.WikidataId).IsUnique();
        b.HasQueryFilter(x => x.Status == PublishStatus.Published);
    }
}

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> b)
    {
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.WikidataId).IsUnique();
    }
}

internal sealed class OperatingMechanismConfiguration : IEntityTypeConfiguration<OperatingMechanism>
{
    public void Configure(EntityTypeBuilder<OperatingMechanism> b)
    {
        b.HasIndex(x => x.Slug).IsUnique();
        // text_pattern_ops lets `path LIKE '/gas-operated/%'` use the index.
        b.HasIndex(x => x.Path).HasOperators("text_pattern_ops");
        b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId);
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b) => b.HasIndex(x => x.Slug).IsUnique();
}

internal sealed class WikidataClassTagMapConfiguration : IEntityTypeConfiguration<WikidataClassTagMap>
{
    public void Configure(EntityTypeBuilder<WikidataClassTagMap> b) => b.HasKey(x => new { x.Qid, x.TagId });
}

internal sealed class SlugRedirectConfiguration : IEntityTypeConfiguration<SlugRedirect>
{
    public void Configure(EntityTypeBuilder<SlugRedirect> b) => b.HasKey(x => new { x.EntityType, x.OldSlug });
}
