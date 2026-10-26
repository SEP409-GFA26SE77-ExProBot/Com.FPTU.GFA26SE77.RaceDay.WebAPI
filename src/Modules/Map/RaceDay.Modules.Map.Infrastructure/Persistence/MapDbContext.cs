using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Map.Domain.Courses;
using RaceDay.Shared.Infrastructure.Messaging.Outbox;

namespace RaceDay.Modules.Map.Infrastructure.Persistence;

public sealed class MapDbContext(DbContextOptions<MapDbContext> options) : DbContext(options)
{
    public DbSet<RaceCourse> RaceCourses => Set<RaceCourse>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("map");

        modelBuilder.Entity<RaceCourse>(entity =>
        {
            entity.ToTable("race_courses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DistanceKm).HasPrecision(10, 3);
            entity.Property(x => x.CourseJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.EventId, x.DistanceId }).IsUnique();
            entity.HasIndex(x => x.EventId);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Payload).IsRequired();
            entity.HasIndex(x => x.ProcessedOnUtc);
            entity.HasIndex(x => x.CreatedOnUtc);
        });
    }
}
