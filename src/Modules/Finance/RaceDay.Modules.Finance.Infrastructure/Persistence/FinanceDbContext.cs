using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Payouts;
using RaceDay.Modules.Finance.Domain.Refunds;
using RaceDay.Modules.Finance.Domain.Settings;

namespace RaceDay.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<EventLedger> EventLedgers => Set<EventLedger>();
    public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();
    public DbSet<RefundRequest> RefundRequests => Set<RefundRequest>();
    public DbSet<OrganizerBankAccount> OrganizerBankAccounts => Set<OrganizerBankAccount>();
    public DbSet<PlatformFinanceSettings> PlatformSettings => Set<PlatformFinanceSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("finance");

        modelBuilder.Entity<EventLedger>(e =>
        {
            e.ToTable("event_ledgers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => x.OrganizerUserId);
            e.Property(x => x.EventName).HasMaxLength(300).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.GrossRevenue).HasPrecision(18, 2);
            e.Property(x => x.RefundedTotal).HasPrecision(18, 2);
            e.Property(x => x.WithdrawnTotal).HasPrecision(18, 2);
            e.Property(x => x.PayoutPending).HasPrecision(18, 2);
            e.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PayoutRequest>(e =>
        {
            e.ToTable("payout_requests");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrganizerUserId);
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.EventName).HasMaxLength(300);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.BankName).HasMaxLength(120);
            e.Property(x => x.AccountNumber).HasMaxLength(50);
            e.Property(x => x.AccountHolderName).HasMaxLength(120);
            e.Property(x => x.Branch).HasMaxLength(120);
            e.Property(x => x.TransferNote).HasMaxLength(300);
            e.Property(x => x.ReceiptUrl).HasMaxLength(1000);
            e.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<RefundRequest>(e =>
        {
            e.ToTable("refund_requests");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PaymentId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.EventId);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Property(x => x.RefundAmount).HasPrecision(18, 2);
            e.Property(x => x.RefundPercent).HasPrecision(5, 2);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.RejectReason).HasMaxLength(1000);
            e.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OrganizerBankAccount>(e =>
        {
            e.ToTable("organizer_bank_accounts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrganizerUserId).IsUnique();
            e.Property(x => x.BankName).HasMaxLength(120);
            e.Property(x => x.AccountNumber).HasMaxLength(50);
            e.Property(x => x.AccountHolderName).HasMaxLength(120);
            e.Property(x => x.Branch).HasMaxLength(120);
            e.Property(x => x.TransferNote).HasMaxLength(300);
            e.Property(x => x.Bin).HasMaxLength(20);
            e.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<PlatformFinanceSettings>(e =>
        {
            e.ToTable("platform_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.PlatformFeePercent).HasPrecision(5, 2);
            e.Property(x => x.PreEventWithdrawPercent).HasPrecision(5, 2);
            e.Property(x => x.PostEventWithdrawPercent).HasPrecision(5, 2);
            e.Property(x => x.MinPayoutAmount).HasPrecision(18, 2);
            e.Property(x => x.FullRefundPercent).HasPrecision(5, 2);
            e.Property(x => x.PartialRefundPercent).HasPrecision(5, 2);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Ignore(x => x.DomainEvents);
        });
    }
}
