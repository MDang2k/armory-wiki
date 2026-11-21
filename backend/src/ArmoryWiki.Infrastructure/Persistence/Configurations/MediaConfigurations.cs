using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArmoryWiki.Infrastructure.Persistence.Configurations;

internal sealed class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> b)
    {
        // Deleting the subject deletes its image rows; the files go once no image uses their hash.
        b.HasOne(i => i.Firearm).WithMany(f => f.Images).HasForeignKey(i => i.FirearmId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(i => i.Person).WithMany(p => p.Images).HasForeignKey(i => i.PersonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(i => i.Organization).WithMany(o => o.Images).HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(i => i.Cartridge).WithMany(c => c.Images).HasForeignKey(i => i.CartridgeId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_images_exactly_one_subject",
            "num_nonnulls(firearm_id, person_id, organization_id, cartridge_id) = 1"));
        b.HasIndex(i => i.ContentHash);
        b.HasIndex(i => i.CommonsFileName).IsUnique();
        b.Property(i => i.ContentHash).HasMaxLength(64);
        b.Property(i => i.OriginalExtension).HasMaxLength(4);
    }
}

internal sealed class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> b)
    {
        b.HasOne(v => v.Firearm).WithMany(f => f.Videos).HasForeignKey(v => v.FirearmId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(v => new { v.FirearmId, v.YouTubeVideoId }).IsUnique();
        b.Property(v => v.YouTubeVideoId).HasMaxLength(11);
        // A draft firearm's videos are hidden with it.
        b.HasQueryFilter(v => v.Firearm.Status == PublishStatus.Published);
    }
}
