
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Domain.Disputes;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Modules.Event.Domain.Supplies;

namespace RaceDay.Modules.Event.Infrastructure.Persistence;

public sealed class EventDbContext(DbContextOptions<EventDbContext> options)
    : DbContext(options)
{
    public DbSet<RaceEvent> RaceEvents => Set<RaceEvent>();
    public DbSet<EventDistance> Distances => Set<EventDistance>();
    public DbSet<EventCheckpoint> Checkpoints => Set<EventCheckpoint>();
    public DbSet<EventDocument> Documents => Set<EventDocument>();
    public DbSet<EventRegistration> Registrations => Set<EventRegistration>();
    public DbSet<RegistrationCheckpointPassage> RegistrationCheckpointPassages
        => Set<RegistrationCheckpointPassage>();
    public DbSet<EventPackagePurchase> PackagePurchases => Set<EventPackagePurchase>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PackageDurationOption> PackageDurationOptions
        => Set<PackageDurationOption>();
    public DbSet<PackageCapacityOption> PackageCapacityOptions
        => Set<PackageCapacityOption>();
    public DbSet<EventStaffMember> StaffMembers => Set<EventStaffMember>();
    public DbSet<EventComplaint> Complaints => Set<EventComplaint>();
    public DbSet<EventSupplyItem> SupplyItems => Set<EventSupplyItem>();
    public DbSet<EventDispute> Disputes => Set<EventDispute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("event");

        modelBuilder.Entity<EventDispute>(entity =>
        {
            entity.ToTable("event_disputes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(EventDispute.MaxTitleLength).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(EventDispute.MaxDescriptionLength).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.RunnerUserId, x.CreatedAtUtc });
            entity.HasOne<RaceEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            entity.Ignore(x => x.DomainEvents);
        });

        // RaceEvent
        modelBuilder.Entity<RaceEvent>(entity =>
        {
            entity.ToTable("race_events");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Tagline).HasMaxLength(500);
            entity.Property(x => x.BannerImage).HasMaxLength(1000);

            entity.Property(x => x.Location)
                .HasMaxLength(300)
                .IsRequired();

            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);

            entity.Property(x => x.PackagePurchaseId);
            entity.Property(x => x.PackageExpiresAtUtc);
            entity.Property(x => x.RegistrationOpensAtUtc);
            entity.Property(x => x.RegistrationClosesAtUtc);
            entity.Property(x => x.EndDate);

            entity.Property(x => x.BibPrefix).HasMaxLength(12);
            entity.Property(x => x.BibRangeStart);
            entity.Property(x => x.BibRangeEnd);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.TicketPricingJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.MerchandiseJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.RulesJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.RewardsJson)
                .HasColumnType("jsonb");

            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.OrganizerUserId);
            entity.HasIndex(x => x.EventDate);

            entity.OwnsOne(x => x.Coordinates, coordinates =>
            {
                coordinates.Property(x => x.Lat)
                    .HasColumnName("coord_lat")
                    .IsRequired();

                coordinates.Property(x => x.Lng)
                    .HasColumnName("coord_lng")
                    .IsRequired();
            });

            entity.OwnsOne(x => x.Organizer, organizer =>
            {
                organizer.Property(x => x.Name)
                    .HasColumnName("org_name")
                    .HasMaxLength(200)
                    .IsRequired();

                organizer.Property(x => x.LegalEntity)
                    .HasColumnName("org_legal_entity")
                    .HasMaxLength(200);

                organizer.Property(x => x.Email)
                    .HasColumnName("org_email")
                    .HasMaxLength(100);

                organizer.Property(x => x.Phone)
                    .HasColumnName("org_phone")
                    .HasMaxLength(30);

                organizer.Property(x => x.Website)
                    .HasColumnName("org_website")
                    .HasMaxLength(300);

                organizer.Property(x => x.Verified)
                    .HasColumnName("org_verified");

                organizer.Property(x => x.AccountHolder)
                    .HasColumnName("org_account_holder")
                    .HasMaxLength(200);

                organizer.Property(x => x.AccountNumber)
                    .HasColumnName("org_account_number")
                    .HasMaxLength(50);

                organizer.Property(x => x.BankName)
                    .HasColumnName("org_bank_name")
                    .HasMaxLength(100);

                organizer.Property(x => x.BankBranch)
                    .HasColumnName("org_bank_branch")
                    .HasMaxLength(100);

                organizer.Property(x => x.SwiftCode)
                    .HasColumnName("org_swift")
                    .HasMaxLength(20);

                organizer.Property(x => x.TransferNote)
                    .HasColumnName("org_transfer_note")
                    .HasMaxLength(300);
            });

            entity.OwnsOne(x => x.Weather, weather =>
            {
                weather.Property(x => x.Temp)
                    .HasColumnName("weather_temp")
                    .HasMaxLength(40)
                    .IsRequired();

                weather.Property(x => x.Condition)
                    .HasColumnName("weather_condition")
                    .HasMaxLength(100);

                weather.Property(x => x.Humidity)
                    .HasColumnName("weather_humidity")
                    .HasMaxLength(40);
            });

            entity.OwnsOne(x => x.Permit, permit =>
            {
                permit.Property(x => x.FileUrl)
                    .HasColumnName("permit_file_url")
                    .HasMaxLength(500)
                    .IsRequired();

                permit.Property(x => x.FileName)
                    .HasColumnName("permit_file_name")
                    .HasMaxLength(255);

                permit.Property(x => x.PermitNumber)
                    .HasColumnName("permit_number")
                    .HasMaxLength(100);

                permit.Property(x => x.IssuingAuthority)
                    .HasColumnName("permit_authority")
                    .HasMaxLength(200);

                permit.Property(x => x.IssueDate)
                    .HasColumnName("permit_issue_date");

                permit.Property(x => x.ValidUntil)
                    .HasColumnName("permit_valid_until");
            });

            entity.HasMany(x => x.Distances)
                .WithOne()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Documents)
                .WithOne()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(x => x.Distances)
                .HasField("_distances")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Navigation(x => x.Documents)
                .HasField("_documents")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(x => x.ActiveRunners);
            entity.Ignore(x => x.DomainEvents);
        });

        // EventDistance
        modelBuilder.Entity<EventDistance>(entity =>
        {
            entity.ToTable("event_distances");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Currency).HasMaxLength(10);
            entity.Property(x => x.Color).HasMaxLength(20);
            entity.Property(x => x.BibPrefix).HasMaxLength(12);
            entity.Property(x => x.BibRangeStart);
            entity.Property(x => x.BibRangeEnd);

            entity.Property(x => x.Price)
                .HasPrecision(18, 2);

            entity.Property(x => x.StartAt);
            entity.Property(x => x.EndAt);

            entity.Property(x => x.RouteGeometryJson)
                .HasColumnType("jsonb");

            entity.HasMany(x => x.Checkpoints)
                .WithOne()
                .HasForeignKey(x => x.DistanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(x => x.Checkpoints)
                .HasField("_checkpoints")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.Ignore(x => x.DomainEvents);
            entity.Ignore(x => x.HasAvailableSlot);
            entity.Ignore(x => x.HasBibRange);
            entity.Ignore(x => x.BibRangeSize);
        });

        // EventCheckpoint
        modelBuilder.Entity<EventCheckpoint>(entity =>
        {
            entity.ToTable("event_checkpoints");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ExternalId).HasMaxLength(64);
            entity.Property(x => x.Name).HasMaxLength(200);

            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.Cutoff).HasMaxLength(20);

            entity.Ignore(x => x.DomainEvents);
        });

        // EventDocument
        modelBuilder.Entity<EventDocument>(entity =>
        {
            entity.ToTable("event_documents");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.DocumentKey)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.FileName).HasMaxLength(255);
            entity.Property(x => x.FileUrl).HasMaxLength(500);

            entity.Ignore(x => x.DomainEvents);
        });

        // EventRegistration
        modelBuilder.Entity<EventRegistration>(entity =>
        {
            entity.ToTable("event_registrations");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.FullName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Phone)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.Gender).HasMaxLength(20);
            entity.Property(x => x.IdNumber).HasMaxLength(30);
            entity.Property(x => x.BloodType).HasMaxLength(10);
            entity.Property(x => x.EmergencyName).HasMaxLength(100);
            entity.Property(x => x.EmergencyPhone).HasMaxLength(20);
            entity.Property(x => x.ShirtType).HasMaxLength(80);
            entity.Property(x => x.ShirtSize).HasMaxLength(20);
            entity.Property(x => x.ShirtColor).HasMaxLength(80);

            entity.Property(x => x.MedicalCodesJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.HealthCertFileName).HasMaxLength(255);
            entity.Property(x => x.HealthCertUrl).HasMaxLength(2000);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.BibNumber).HasMaxLength(30);

            entity.Property(x => x.QuotedAmount)
                .HasPrecision(18, 2);

            entity.Property(x => x.QuotedPhaseKey).HasMaxLength(64);
            entity.Property(x => x.QuotedPhaseName).HasMaxLength(120);

            entity.Property(x => x.CheckedInAtUtc);
            entity.Property(x => x.CheckedInByUserId);
            entity.Property(x => x.StartedAtUtc);
            entity.Property(x => x.FinishedAtUtc);
            entity.Property(x => x.ResultSeconds);

            entity.HasIndex(x => new { x.EventId, x.DistanceId, x.UserId })
                .IsUnique()
                .HasFilter("\"Status\" NOT IN ('Rejected', 'Cancelled')");

            entity.HasIndex(x => x.EventId);

            entity.Ignore(x => x.DomainEvents);
        });

        // RegistrationCheckpointPassage
        modelBuilder.Entity<RegistrationCheckpointPassage>(entity =>
        {
            entity.ToTable("event_registration_checkpoint_passages");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.PassedAtUtc);
            entity.Property(x => x.CreatedAtUtc);

            entity.HasIndex(x => new
            {
                x.RegistrationId,
                x.CheckpointId
            })
                .IsUnique();

            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => x.DistanceId);

            entity.Ignore(x => x.DomainEvents);
        });

        // EventPackagePurchase
        modelBuilder.Entity<EventPackagePurchase>(entity =>
        {
            entity.ToTable("package_purchases");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.OrganizerUserId);

            entity.Property(x => x.OrgName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.OrgIntro).HasMaxLength(2000);
            entity.Property(x => x.OrgAddress).HasMaxLength(500);
            entity.Property(x => x.InvoiceInfo).HasMaxLength(1000);
            entity.Property(x => x.DurationMonths);
            entity.Property(x => x.ParticipantCap);
            entity.Property(x => x.PromoCode).HasMaxLength(50);

            entity.Property(x => x.BasePrice)
                .HasPrecision(15, 2);

            entity.Property(x => x.CapacityFee)
                .HasPrecision(15, 2);

            entity.Property(x => x.Discount)
                .HasPrecision(15, 2);

            entity.Property(x => x.TotalAmount)
                .HasPrecision(15, 2);

            entity.Property(x => x.Currency)
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.PaymentId);
            entity.Property(x => x.CreatedEventId);
            entity.Property(x => x.PackageExpiresAtUtc);

            entity.HasIndex(x => x.OrganizerUserId);

            entity.Ignore(x => x.DomainEvents);
        });

        // PromoCode
        modelBuilder.Entity<PromoCode>(entity =>
        {
            entity.ToTable("promo_codes");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(x => x.Code)
                .IsUnique();

            entity.Property(x => x.PercentOff)
                .HasPrecision(5, 4);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.ExpiresAtUtc);
            entity.Property(x => x.MaxRedemptions);
            entity.Property(x => x.UsedCount);
            entity.Property(x => x.CreatedAtUtc);
            entity.Property(x => x.UpdatedAtUtc);

            entity.Ignore(x => x.DomainEvents);
        });

        // PackageDurationOption
        modelBuilder.Entity<PackageDurationOption>(entity =>
        {
            entity.ToTable("package_duration_options");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(x => x.Months);

            entity.HasIndex(x => x.Months)
                .IsUnique();

            entity.Property(x => x.Price)
                .HasPrecision(15, 2);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.SortOrder);
            entity.Property(x => x.CreatedAtUtc);
            entity.Property(x => x.UpdatedAtUtc);

            entity.Ignore(x => x.DomainEvents);
        });

        // PackageCapacityOption
        modelBuilder.Entity<PackageCapacityOption>(entity =>
        {
            entity.ToTable("package_capacity_options");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(x => x.MaxParticipants);

            entity.HasIndex(x => x.MaxParticipants)
                .IsUnique();

            entity.Property(x => x.AddOnFee)
                .HasPrecision(15, 2);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.SortOrder);
            entity.Property(x => x.CreatedAtUtc);
            entity.Property(x => x.UpdatedAtUtc);

            entity.Ignore(x => x.DomainEvents);
        });

        // EventStaffMember
        modelBuilder.Entity<EventStaffMember>(entity =>
        {
            entity.ToTable("event_staff_members");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Role)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.FullName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.StationId).HasMaxLength(120);
            entity.Property(x => x.StationLabel).HasMaxLength(300);

            entity.HasIndex(x => x.EventId);

            entity.HasIndex(x => new
            {
                x.EventId,
                x.Role,
                x.Email
            })
                .IsUnique()
                .HasFilter("\"Status\" <> 'Revoked'");

            entity.Ignore(x => x.DomainEvents);
        });

        // EventComplaint
        modelBuilder.Entity<EventComplaint>(entity =>
        {
            entity.ToTable("event_complaints");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Content)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(x => x.EvidenceLinksJson)
                .IsRequired()
                .HasColumnType("jsonb");

            entity.Property(x => x.ResponseContent)
                .HasMaxLength(2000);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => x.UserId);

            entity.Ignore(x => x.DomainEvents);
        });

        // EventSupplyItem
        modelBuilder.Entity<EventSupplyItem>(entity =>
        {
            entity.ToTable("event_supplies");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(EventSupplyItem.MaxNameLength)
                .IsRequired();

            entity.Property(x => x.Quantity)
                .IsRequired();

            entity.Property(x => x.Unit)
                .HasMaxLength(EventSupplyItem.MaxUnitLength)
                .IsRequired();

            entity.Property(x => x.Notes)
                .HasMaxLength(EventSupplyItem.MaxNotesLength);

            entity.Property(x => x.SortOrder);
            entity.Property(x => x.CreatedAtUtc);
            entity.Property(x => x.UpdatedAtUtc);

            entity.HasIndex(x => x.EventId);

            entity.Ignore(x => x.DomainEvents);
        });
    }
}