using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Payment.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Payment.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(PaymentDbContext))]
[Migration("20261111033100_UniquePaymentTransactionCode")]
public partial class UniquePaymentTransactionCode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_payment_transactions_TransactionCode"
                ON payment.payment_transactions ("TransactionCode");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS payment."IX_payment_transactions_TransactionCode";
            """);
    }
}
