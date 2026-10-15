using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Iam.Domain.Profiles;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Infrastructure.Messaging.Outbox;

namespace RaceDay.Modules.Iam.Infrastructure.Persistence;

public sealed class IamDbContext(DbContextOptions<IamDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("iam");

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(500).IsRequired();
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("text").IsRequired();
            entity.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc");
            entity.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc");
            entity.Property(x => x.ProcessedOnUtc).HasColumnName("processed_on_utc");
            entity.HasIndex(x => x.ProcessedOnUtc);
            entity.HasIndex(x => x.CreatedOnUtc);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("role_id");
            entity.Property(x => x.RoleName).HasColumnName("role_name").HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(255);
            entity.HasIndex(x => x.RoleName).IsUnique();
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("user_id");
            entity.Property(x => x.Email).HasColumnName("email").HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20);
            entity.Property(x => x.DateOfBirth).HasColumnName("date_of_birth");
            entity.Property(x => x.Gender).HasColumnName("gender").HasConversion<short>();
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            entity.Property(x => x.EmailVerified).HasColumnName("email_verified");
            entity.Property(x => x.MustChangePassword).HasColumnName("must_change_password");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Ignore(x => x.DomainEvents);

            entity.HasOne(x => x.Profile)
                .WithOne()
                .HasForeignKey<Profile>(x => x.Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Role>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("user_id");
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(255);
            entity.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(100);
            entity.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(100);
            entity.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(500);
            entity.Property(x => x.VerificationStatus).HasColumnName("verification_status").HasConversion<short>();
            entity.Property(x => x.VerifiedAt).HasColumnName("verified_at");
            entity.Property(x => x.VerifiedBy).HasColumnName("verified_by").HasMaxLength(100);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmailOtp>(entity =>
        {
            entity.ToTable("email_otps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Email).HasColumnName("email").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<short>();
            entity.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ExpiresOnUtc).HasColumnName("expires_on_utc");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            entity.Property(x => x.ConsumedOnUtc).HasColumnName("consumed_on_utc");
            entity.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc");
            entity.HasIndex(x => new { x.Email, x.Purpose, x.CreatedOnUtc });
            entity.Ignore(x => x.DomainEvents);
        });
    }
}
