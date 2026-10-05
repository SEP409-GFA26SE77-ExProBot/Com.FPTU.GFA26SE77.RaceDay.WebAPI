using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;

namespace RaceDay.Modules.Event.Infrastructure.Persistence;

public sealed class EventDbContext(DbContextOptions<EventDbContext> options) : DbContext(options)
{
    public DbSet<RaceEvent> RaceEvents => Set<RaceEvent>();
    public DbSet<EventDistance> Distances => Set<EventDistance>();
    public DbSet<EventCheckpoint> Checkpoints => Set<EventCheckpoint>();
    public DbSet<EventDocument> Documents => Set<EventDocument>();
    public DbSet<EventRegistration> Registrations => Set<EventRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("event");

        modelBuilder.Entity<RaceEvent>(entity =>
        {
            entity.ToTable("race_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Tagline).HasMaxLength(500);
            entity.Property(x => x.BannerImage).HasMaxLength(1000);
            entity.Property(x => x.Location).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.TicketPricingJson).HasColumnType("jsonb");
            entity.Property(x => x.MerchandiseJson).HasColumnType("jsonb");
            entity.Property(x => x.RulesJson).HasColumnType("jsonb");
            entity.Property(x => x.RewardsJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.OrganizerUserId);
            entity.HasIndex(x => x.EventDate);

            entity.OwnsOne(x => x.Coordinates, c =>
            {
                c.Property(p => p.Lat).HasColumnName("coord_lat");
                c.Property(p => p.Lng).HasColumnName("coord_lng");
            });

            entity.OwnsOne(x => x.Organizer, o =>
            {
                o.Property(p => p.Name).HasColumnName("org_name").HasMaxLength(200);
                o.Property(p => p.LegalEntity).HasColumnName("org_legal_entity").HasMaxLength(200);
                o.Property(p => p.Email).HasColumnName("org_email").HasMaxLength(100);
                o.Property(p => p.Phone).HasColumnName("org_phone").HasMaxLength(30);
                o.Property(p => p.Website).HasColumnName("org_website").HasMaxLength(300);
                o.Property(p => p.Verified).HasColumnName("org_verified");
                o.Property(p => p.AccountHolder).HasColumnName("org_account_holder").HasMaxLength(200);
                o.Property(p => p.AccountNumber).HasColumnName("org_account_number").HasMaxLength(50);
                o.Property(p => p.BankName).HasColumnName("org_bank_name").HasMaxLength(100);
                o.Property(p => p.BankBranch).HasColumnName("org_bank_branch").HasMaxLength(100);
                o.Property(p => p.SwiftCode).HasColumnName("org_swift").HasMaxLength(20);
                o.Property(p => p.TransferNote).HasColumnName("org_transfer_note").HasMaxLength(300);
            });

            entity.OwnsOne(x => x.Weather, w =>
            {
                w.Property(p => p.Temp).HasColumnName("weather_temp").HasMaxLength(40);
                w.Property(p => p.Condition).HasColumnName("weather_condition").HasMaxLength(100);
                w.Property(p => p.Humidity).HasColumnName("weather_humidity").HasMaxLength(40);
            });

            entity.OwnsOne(x => x.Permit, p =>
            {
                p.Property(x => x.FileUrl).HasColumnName("permit_file_url").HasMaxLength(500);
                p.Property(x => x.FileName).HasColumnName("permit_file_name").HasMaxLength(255);
                p.Property(x => x.PermitNumber).HasColumnName("permit_number").HasMaxLength(100);
                p.Property(x => x.IssuingAuthority).HasColumnName("permit_authority").HasMaxLength(200);
                p.Property(x => x.IssueDate).HasColumnName("permit_issue_date");
                p.Property(x => x.ValidUntil).HasColumnName("permit_valid_until");
            });

            entity.HasMany(x => x.Distances)
                .WithOne()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Documents)
                .WithOne()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(x => x.Distances).HasField("_distances").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.Documents).HasField("_documents").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(x => x.ActiveRunners);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EventDistance>(entity =>
        {
            entity.ToTable("event_distances");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(10);
            entity.Property(x => x.Color).HasMaxLength(20);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.RouteGeometryJson).HasColumnType("jsonb");
            entity.OwnsOne(x => x.StartLocation, s =>
            {
                s.Property(p => p.Name).HasColumnName("start_name").HasMaxLength(200);
                s.Property(p => p.Lat).HasColumnName("start_lat");
                s.Property(p => p.Lng).HasColumnName("start_lng");
            });
            entity.OwnsOne(x => x.FinishLocation, f =>
            {
                f.Property(p => p.Name).HasColumnName("finish_name").HasMaxLength(200);
                f.Property(p => p.Lat).HasColumnName("finish_lat");
                f.Property(p => p.Lng).HasColumnName("finish_lng");
            });
            entity.HasMany(x => x.Checkpoints)
                .WithOne()
                .HasForeignKey(x => x.DistanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Checkpoints).HasField("_checkpoints").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(x => x.DomainEvents);
            entity.Ignore(x => x.HasAvailableSlot);
        });

        modelBuilder.Entity<EventCheckpoint>(entity =>
        {
            entity.ToTable("event_checkpoints");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalId).HasMaxLength(64);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Cutoff).HasMaxLength(20);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EventDocument>(entity =>
        {
            entity.ToTable("event_documents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DocumentKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.FileName).HasMaxLength(255);
            entity.Property(x => x.FileUrl).HasMaxLength(500);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EventRegistration>(entity =>
        {
            entity.ToTable("event_registrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Gender).HasMaxLength(20);
            entity.Property(x => x.IdNumber).HasMaxLength(30);
            entity.Property(x => x.BloodType).HasMaxLength(10);
            entity.Property(x => x.EmergencyName).HasMaxLength(100);
            entity.Property(x => x.EmergencyPhone).HasMaxLength(20);
            entity.Property(x => x.ShirtType).HasMaxLength(80);
            entity.Property(x => x.ShirtSize).HasMaxLength(20);
            entity.Property(x => x.ShirtColor).HasMaxLength(80);
            entity.Property(x => x.MedicalCodesJson).HasColumnType("jsonb");
            entity.Property(x => x.HealthCertFileName).HasMaxLength(255);
            entity.Property(x => x.HealthCertUrl).HasMaxLength(2000);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.BibNumber).HasMaxLength(30);
            // Cho phép đăng ký lại cùng cự ly sau Rejected/Cancelled (Status lưu string enum name).
            entity.HasIndex(x => new { x.EventId, x.DistanceId, x.UserId })
                .IsUnique()
                .HasFilter("\"Status\" NOT IN ('Rejected', 'Cancelled')");
            entity.HasIndex(x => x.EventId);
            entity.Ignore(x => x.DomainEvents);
        });
    }
}
