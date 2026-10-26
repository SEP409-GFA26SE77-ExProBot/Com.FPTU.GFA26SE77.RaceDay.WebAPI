using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Bảng <c>event_registration_checkpoint_passages</c> đã được tạo bởi
    /// <c>20261015120000_AddRegistrationFinishTiming</c>. Migration này từng
    /// CreateTable trùng → Postgres 42P07 khi <c>MigrateAsync</c> sau pull.
    /// Giữ migration (history/snapshot) nhưng Up/Down no-op.
    /// </remarks>
    public partial class FixPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // no-op — tránh "relation already exists"
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // no-op — rollback bảng thuộc AddRegistrationFinishTiming
        }
    }
}
