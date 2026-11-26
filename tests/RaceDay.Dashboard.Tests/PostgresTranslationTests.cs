using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RaceDay.Modules.Event.Infrastructure.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Iam.Infrastructure.Persistence;
using RaceDay.Modules.Iam.Infrastructure.Repositories;
using RaceDay.Modules.Payment.Infrastructure.Finance;
using RaceDay.Modules.Payment.Infrastructure.Persistence;
using RaceDay.Shared.Application.Dashboard;

namespace RaceDay.Dashboard.Tests;

/// <summary>Checks production-provider LINQ translation; does not execute SQL on a PostgreSQL server.</summary>
public class PostgresTranslationTests
{
    [Fact]
    public async Task DailyAggregates_TranslateWithNpgsql_IncludingVietnamDateBuckets()
    {
        var capture = new CaptureCommands();
        var noConnection = new SuppressConnection();
        const string connection = "Host=localhost;Database=translation_only;Username=unused;Password=unused";
        var range = new DashboardDateRange(new(2026, 11, 15), new(2026, 11, 16));
        await using var iam = new IamDbContext(new DbContextOptionsBuilder<IamDbContext>()
            .UseNpgsql(connection).AddInterceptors(noConnection, capture).Options);
        await new AdminDashboardReader(iam).ReadAsync(range, default);
        await using var payment = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connection).AddInterceptors(noConnection, capture).Options);
        await new PaymentRevenueQuery(payment).ReadAsync([Guid.NewGuid()], range.FromUtc, range.ToUtcExclusive, default);
        await using var events = new EventDbContext(new DbContextOptionsBuilder<EventDbContext>()
            .UseNpgsql(connection).AddInterceptors(noConnection, capture).Options);
        await new OrganizerDashboardReader(events).TrendAsync(Guid.NewGuid(), Guid.NewGuid(), range, default);
        var dailySql = capture.Commands.Where(sql => sql.Contains("date_trunc", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(4, dailySql.Length); // IAM, collected, refunded, registrations.
        Assert.All(dailySql, sql => Assert.Contains("GROUP BY", sql));
        Assert.All(dailySql, sql => Assert.Contains("7", sql));
    }

    private sealed class SuppressConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CaptureCommands : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            var table = new DataTable();
            if (command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal))
            {
                table.Columns.Add("exists", typeof(bool));
                table.Rows.Add(true);
            }
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
