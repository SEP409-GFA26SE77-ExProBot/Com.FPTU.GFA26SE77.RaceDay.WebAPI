using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Shared.Infrastructure.Messaging.Outbox;

namespace RaceDay.Modules.Payment.Infrastructure.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payment");

        modelBuilder.Entity<PaymentOrder>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(15, 2);
            entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            entity.Property(x => x.PaymentMethod).HasMaxLength(50);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.PaymentGateway).HasMaxLength(100);
            entity.Property(x => x.TransactionReference).HasMaxLength(255);
            entity.Property(x => x.CheckoutUrl).HasMaxLength(1000);
            entity.HasIndex(x => x.RegistrationId);
            entity.HasIndex(x => x.OrderCode).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasMany(x => x.Transactions)
                .WithOne()
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Transactions).HasField("_transactions").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("payment_transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TransactionCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GatewayTransactionId).HasMaxLength(255);
            entity.Property(x => x.TransactionType).HasMaxLength(50);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.Amount).HasPrecision(15, 2);
            entity.Property(x => x.RawResponseJson).HasColumnType("jsonb");
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

