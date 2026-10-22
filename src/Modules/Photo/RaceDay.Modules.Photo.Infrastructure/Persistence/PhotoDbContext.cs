using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Photo.Domain;

namespace RaceDay.Modules.Photo.Infrastructure.Persistence;

public sealed class PhotoDbContext(DbContextOptions<PhotoDbContext> options) : DbContext(options)
{
    public DbSet<PhotoAsset> Photos => Set<PhotoAsset>();
    public DbSet<PhotoTag> PhotoTags => Set<PhotoTag>();
    public DbSet<PhotoDispute> PhotoDisputes => Set<PhotoDispute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("photo");

        modelBuilder.Entity<PhotoAsset>(entity =>
        {
            entity.ToTable("photos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ContentSha256).HasMaxLength(64);
            entity.Property(x => x.OriginalFileName).HasMaxLength(260);
            entity.Property(x => x.ContentType).HasMaxLength(128);
            entity.Property(x => x.Zone).HasMaxLength(64);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.DiscardReason).HasMaxLength(500);
            entity.Property(x => x.OcrJobId).HasMaxLength(64);
            entity.HasIndex(x => new { x.EventId, x.Status });
            entity.HasIndex(x => new { x.EventId, x.ContentSha256 });
            entity.HasMany(x => x.Tags)
                .WithOne()
                .HasForeignKey(x => x.PhotoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Tags)
                .HasField("_tags")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(x => x.DomainEvents);
            entity.Ignore(x => x.IsSearchable);
        });

        modelBuilder.Entity<PhotoTag>(entity =>
        {
            entity.ToTable("photo_tags");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BibNormalized).HasMaxLength(30).IsRequired();
            entity.Property(x => x.BboxJson).HasMaxLength(128);
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.BibNormalized, x.PhotoId });
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PhotoDispute>(entity =>
        {
            entity.ToTable("photo_disputes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ReviewNote).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.EventId, x.Status });
            entity.HasIndex(x => x.ReporterUserId);
            entity.Ignore(x => x.DomainEvents);
        });
    }
}
