using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Device.Domain.Auth;
using RaceDay.Modules.Device.Domain.Sos;
using RaceDay.Shared.Infrastructure.Messaging.Outbox;

namespace RaceDay.Modules.Device.Infrastructure.Persistence;

public sealed class DeviceDbContext(DbContextOptions<DeviceDbContext> options) : DbContext(options)
{
    public DbSet<DeviceAuthRequest> DeviceAuthRequests => Set<DeviceAuthRequest>();
    public DbSet<LinkedDevice> LinkedDevices => Set<LinkedDevice>();
    public DbSet<SosAlert> SosAlerts => Set<SosAlert>();
    public DbSet<SosLocation> SosLocations => Set<SosLocation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("device");

        modelBuilder.Entity<DeviceAuthRequest>(entity =>
        {
            entity.ToTable("device_auth_requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientDeviceId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceCodeHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.UserCode).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.DeviceCodeHash);
            entity.HasIndex(x => x.UserCode);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<LinkedDevice>(entity =>
        {
            entity.ToTable("linked_devices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClientDeviceId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.RefreshTokenHash).HasMaxLength(64);
            entity.HasIndex(x => x.RunnerUserId);
            entity.Ignore(x => x.DomainEvents);
            entity.Ignore(x => x.IsRevoked);
        });

        modelBuilder.Entity<SosAlert>(entity =>
        {
            entity.ToTable("sos_alerts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => x.RunnerUserId);
            entity.HasMany(x => x.Locations)
                .WithOne()
                .HasForeignKey(x => x.SosAlertId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Locations)
                .HasField("_locations")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .AutoInclude(false);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<SosLocation>(entity =>
        {
            entity.ToTable("sos_locations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SosAlertId);
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
