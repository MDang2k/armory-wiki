using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Infrastructure.Persistence;

public sealed class ArmoryWikiDbContext(DbContextOptions<ArmoryWikiDbContext> options) : DbContext(options)
{
    /// <summary>Shadow column: weighted tsvector over name, alternative names and summary.</summary>
    public const string SearchVector = "SearchVector";
    /// <summary>Shadow column: lower-cased name + alternative names, trigram-indexed for fuzzy matching.</summary>
    public const string SearchNames = "SearchNames";

    public DbSet<Firearm> Firearms => Set<Firearm>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<VariantCartridge> VariantCartridges => Set<VariantCartridge>();
    public DbSet<FirearmTag> FirearmTags => Set<FirearmTag>();
    public DbSet<FirearmDesigner> FirearmDesigners => Set<FirearmDesigner>();
    public DbSet<FirearmManufacturer> FirearmManufacturers => Set<FirearmManufacturer>();
    public DbSet<FirearmRelation> FirearmRelations => Set<FirearmRelation>();
    public DbSet<Adoption> Adoptions => Set<Adoption>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Cartridge> Cartridges => Set<Cartridge>();
    public DbSet<OperatingMechanism> OperatingMechanisms => Set<OperatingMechanism>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<WikidataClassTagMap> WikidataClassTagMaps => Set<WikidataClassTagMap>();
    public DbSet<SlugRedirect> SlugRedirects => Set<SlugRedirect>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as strings so the data stays readable and reorder-safe.
        configurationBuilder.Properties<PublishStatus>().HaveConversion<string>();
        configurationBuilder.Properties<DatePrecision>().HaveConversion<string>();
        configurationBuilder.Properties<FeedSystem>().HaveConversion<string>();
        configurationBuilder.Properties<FiringModes>().HaveConversion<string>();
        configurationBuilder.Properties<TagGroup>().HaveConversion<string>();
        configurationBuilder.Properties<OrganizationType>().HaveConversion<string>();
        configurationBuilder.Properties<CartridgeType>().HaveConversion<string>();
        configurationBuilder.Properties<DesignerRole>().HaveConversion<string>();
        configurationBuilder.Properties<RelationType>().HaveConversion<string>();
        configurationBuilder.Properties<SluggedEntity>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.Owned<FuzzyDate>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArmoryWikiDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
