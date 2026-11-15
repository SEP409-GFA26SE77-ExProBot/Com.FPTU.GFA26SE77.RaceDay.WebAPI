using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RaceDay.Modules.Event.Application.Dashboard;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Events;
using RaceDay.Modules.Event.Infrastructure.Finance;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Finance.Application.Dashboard;
using RaceDay.Modules.Iam.Application.Dashboard;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Infrastructure.Persistence;
using RaceDay.Modules.Iam.Infrastructure.Repositories;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Modules.Payment.Infrastructure.Finance;
using RaceDay.Modules.Payment.Infrastructure.Persistence;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Dashboard;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Dashboard.Tests;

public class DashboardTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly DateTime MidnightUtc = new(2026, 11, 14, 17, 0, 0, DateTimeKind.Utc);
    private static DashboardDateRange Range => new(Day, Day.AddDays(1));
    private static void Set(object entity, string property, object value) => entity.GetType().GetProperty(property)!.SetValue(entity, value);
    private static ICurrentUserAccessor Current(Guid id, bool admin = false)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.UserId.Returns(id);
        user.IsInRole("Admin").Returns(admin);
        user.IsAdmin.Returns(admin);
        return user;
    }

    [Fact]
    public void DateRange_UsesVietnamMidnight_AndThirtyDayDefault()
    {
        var range = DashboardDateRange.Create(null, null, MidnightUtc);
        Assert.Equal(Day, range.To);
        Assert.Equal(30, range.Days().Count());
        Assert.Equal(MidnightUtc, new DashboardDateRange(Day, Day).FromUtc);
        Assert.Equal(MidnightUtc.AddDays(1), new DashboardDateRange(Day, Day).ToUtcExclusive);
    }

    [Fact]
    public void DateRange_RejectsReversedAndOverlongRanges()
    {
        Assert.Throws<DomainException>(() => DashboardDateRange.Create(Day.AddDays(1), Day, MidnightUtc));
        Assert.Throws<DomainException>(() => DashboardDateRange.Create(Day.AddDays(-366), Day, MidnightUtc));
        Assert.Equal(366, DashboardDateRange.Create(Day.AddDays(-365), Day, MidnightUtc).Days().Count());
    }

    [Fact]
    public async Task Admin_SeparatesCurrentTotalsFromDateRange_AndFillsEmptyDays()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new IamDbContext(new DbContextOptionsBuilder<IamDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var runner = Role.Create("Runner");
        var organizer = Role.Create("Organizer");
        db.Roles.AddRange(runner, organizer);
        var users = new[] {
            User.Create("a@test.vn", "usera", "hash", "A", runner.Id, status: UserStatus.Active),
            User.Create("b@test.vn", "userb", "hash", "B", runner.Id, status: UserStatus.Pending),
            User.Create("c@test.vn", "userc", "hash", "C", organizer.Id, status: UserStatus.Locked)
        };
        Set(users[0], "CreatedAt", MidnightUtc.AddTicks(-1));
        Set(users[1], "CreatedAt", MidnightUtc);
        Set(users[2], "CreatedAt", Range.ToUtcExclusive);
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var result = await new AdminDashboardReader(db).ReadAsync(Range, default);
        Assert.Equal(3, result.TotalUsers);
        Assert.Equal(1, result.ActiveUsers);
        Assert.Equal(1, result.PendingUsers);
        Assert.Equal(1, result.LockedUsers);
        Assert.Equal(1, result.NewUsersInPeriod);
        Assert.Equal(new[] { 1, 0 }, result.NewUsersByDay.Select(x => x.Count));
        Assert.Equal(2, result.UsersByRole.Single(x => x.Role == "Runner").Count);
    }

    [Fact]
    public async Task AdminHandler_DeniesNonAdminBeforeReading()
    {
        var reader = Substitute.For<IAdminDashboardReader>();
        var handler = new GetAdminDashboardQueryHandler(reader, Current(Guid.NewGuid()));
        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new(null, null), default));
        Assert.Empty(reader.ReceivedCalls());
    }

    private static RaceEvent Race(Guid owner)
    {
        var race = RaceEvent.CreateDraft(owner, "Test race", "HCM", Day.AddDays(3), new GeoPoint(10, 106),
            OrganizerInfo.Create("BTC", "btc@test.vn", "0900000000", "RaceDay", null, false), 100);
        race.ReplaceDistances([EventDistance.Create(race.Id, "5 km", 5, 100, 100000, "#ffffff")]);
        return race;
    }

    private static EventRegistration Registration(RaceEvent race)
        => EventRegistration.Create(race.Id, race.Distances.Single().Id, Guid.NewGuid(), "Runner", "runner@test.vn",
            "0900000000", null, null, null, null, null, null, null, null, null, null, null, "[]", null, null, true);

    [Fact]
    public async Task EventDashboard_SeparatesWorkflowAndRaceState_AndScopesAllQueriesToOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new EventDbContext(new DbContextOptionsBuilder<EventDbContext>().UseSqlite(connection).Options);
        // The production model has a PostgreSQL-only jsonb default; adapt DDL only for this SQLite fixture.
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript().Replace("'[]'::jsonb", "'[]'"));
        var owner = Guid.NewGuid();
        var race = Race(owner);
        var foreign = Race(Guid.NewGuid());
        var pending = Registration(race);
        pending.MarkPaidPendingApproval();
        var running = Registration(race);
        running.Confirm("0001");
        running.CheckIn(owner);
        running.Start(MidnightUtc);
        var finished = Registration(race);
        finished.Confirm("0002");
        finished.Start(MidnightUtc);
        finished.Finish(MidnightUtc.AddHours(1), 3600);
        var cancelled = Registration(race);
        cancelled.CancelUnpaid();
        var foreignReg = Registration(foreign);
        foreignReg.MarkPaidPendingApproval();
        Set(running, "CreatedAtUtc", MidnightUtc);
        Set(finished, "CreatedAtUtc", Range.ToUtcExclusive);
        Set(pending, "CreatedAtUtc", MidnightUtc.AddSeconds(-1));
        Set(cancelled, "CreatedAtUtc", MidnightUtc.AddHours(1));
        Set(foreignReg, "CreatedAtUtc", MidnightUtc);
        Set(race, "Status", RaceEventStatus.Approved);
        db.RaceEvents.AddRange(race, foreign);
        db.Registrations.AddRange(pending, running, finished, cancelled, foreignReg);
        db.Complaints.Add(EventComplaint.Create(race.Id, race.Distances.Single().Id, Guid.NewGuid(), "Help", "Need help", null));
        await db.SaveChangesAsync();
        var reader = new OrganizerDashboardReader(db);
        var result = (await reader.EventAsync(owner, race.Id, default))!;
        Assert.Equal(4, result.TotalRegistrations);
        Assert.Equal(1, result.RegistrationsByStatus.Single(x => x.Status == "pending_approval").Count);
        Assert.Equal(2, result.CheckInSummary.Eligible);
        Assert.Equal(1, result.CheckInSummary.CheckedIn);
        Assert.Equal(1, result.RaceByStatus.Single(x => x.Status == "running").Count);
        Assert.Equal(1, result.RaceByStatus.Single(x => x.Status == "finished").Count);
        Assert.Equal(2, result.RaceByStatus.Sum(x => x.Count));
        Assert.Equal(2, result.Distances.Single().ConfirmedRegistrations);
        Assert.Contains("official-letter", result.Preparation.MissingDocumentKeys);
        Assert.Contains("security", result.Preparation.MissingDocumentKeys);
        Assert.Null(await reader.EventAsync(owner, foreign.Id, default));
        Assert.Null(await reader.TrendAsync(owner, foreign.Id, Range, default));
        var overview = await reader.OverviewAsync(owner, Day, default);
        Assert.Equal(1, overview.TotalEvents);
        Assert.Equal(1, overview.PendingRegistrations);
        Assert.Equal(1, overview.PendingComplaints);
        Assert.Equal(race.Id, Assert.Single(overview.UpcomingEvents).EventId);
        Assert.Equal(2, Assert.Single(overview.EventsWithPendingTasks).PendingTasks);
        var trend = (await reader.TrendAsync(owner, race.Id, Range, default))!;
        Assert.Equal(2, trend.TotalRegistrations); // Includes the cancelled order, excludes the end boundary.
        Assert.Equal(new[] { 2, 0 }, trend.Days.Select(x => x.Count));
        var scope = await new OrganizerRegistrationScopeQuery(db).ListIdsAsync(owner, null, default);
        Assert.Equal(4, scope.Count);
        Assert.Contains(cancelled.Id, scope);
        Assert.DoesNotContain(foreignReg.Id, scope);
    }

    [Fact]
    public async Task EventHandler_DeniesForeignEventBeforeReadingMap()
    {
        var reader = Substitute.For<IOrganizerDashboardReader>();
        var courses = Substitute.For<IEventCoursesReadQuery>();
        var handler = new GetEventDashboardQueryHandler(reader, courses, Current(Guid.NewGuid()));
        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new(Guid.NewGuid()), default));
        Assert.Empty(courses.ReceivedCalls());
    }

    [Fact]
    public async Task Revenue_UsesPaidTimeAndSuccessfulRefundTime_ExcludesPendingForeignAndPackages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var registrationId = Guid.NewGuid();
        var paid = PaymentOrder.Create(registrationId, Guid.NewGuid(), 100000, "VND", 1);
        paid.MarkPaid("charge-1", null);
        Set(paid, "PaidAtUtc", MidnightUtc);
        var refund = paid.MarkRefunded(50000, "refund-1", null);
        Set(refund, "CreatedAtUtc", MidnightUtc.AddDays(1));
        var pending = PaymentOrder.Create(registrationId, Guid.NewGuid(), 999999, "VND", 2);
        var foreign = PaymentOrder.Create(Guid.NewGuid(), Guid.NewGuid(), 999999, "VND", 3);
        foreign.MarkPaid("foreign", null);
        Set(foreign, "PaidAtUtc", MidnightUtc);
        var package = PaymentOrder.CreateForPackage(Guid.NewGuid(), Guid.NewGuid(), 999999, 0, null, "VND", 4);
        package.MarkPaid("package", null);
        Set(package, "PaidAtUtc", MidnightUtc);
        db.PaymentOrders.AddRange(paid, pending, foreign, package);
        await db.SaveChangesAsync();
        var reader = new PaymentRevenueQuery(db);
        var result = await reader.ReadAsync([registrationId], Range.FromUtc, Range.ToUtcExclusive, default);
        Assert.Equal(100000, result.Sum(x => x.Collected));
        Assert.Equal(50000, result.Sum(x => x.Refunded));
        Assert.Equal(Day, result.Single(x => x.Collected > 0).Date);
        Assert.Equal(Day.AddDays(1), result.Single(x => x.Refunded > 0).Date);
        var refundOnly = await reader.ReadAsync([registrationId], MidnightUtc.AddDays(1), Range.ToUtcExclusive, default);
        Assert.Equal(0, refundOnly.Sum(x => x.Collected));
        Assert.Equal(50000, refundOnly.Sum(x => x.Refunded));
        Assert.Empty(await reader.ReadAsync([], Range.FromUtc, Range.ToUtcExclusive, default));
    }

    [Fact]
    public async Task RevenueHandler_DeniesForeignOrMissingEventBeforeReadingPayments()
    {
        var payments = Substitute.For<IPaymentRevenueQuery>();
        var registrations = Substitute.For<IOrganizerRegistrationScopeQuery>();
        var handler = new GetRevenueTrendQueryHandler(Substitute.For<IEventFinanceQuery>(), registrations, payments, Current(Guid.NewGuid()));
        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new(Guid.NewGuid(), Day, Day), default));
        Assert.Empty(registrations.ReceivedCalls());
        Assert.Empty(payments.ReceivedCalls());
    }

    [Fact]
    public async Task RevenueHandler_ZeroFillsAndKeepsCurrenciesSeparate()
    {
        var id = Guid.NewGuid();
        var scope = Substitute.For<IOrganizerRegistrationScopeQuery>();
        scope.ListIdsAsync(id, null, default).Returns(new[] { Guid.NewGuid() });
        var payments = Substitute.For<IPaymentRevenueQuery>();
        payments.ReadAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), default)
            .Returns(new[] { new PaymentRevenueDay(Day, "VND", 100000, 0), new PaymentRevenueDay(Day, "USD", 10, 20) });
        var handler = new GetRevenueTrendQueryHandler(Substitute.For<IEventFinanceQuery>(), scope, payments, Current(id));
        var result = await handler.Handle(new(null, Day, Day.AddDays(1)), default);
        Assert.Equal(2, result.Series.Count);
        Assert.Equal(-10, result.Series.Single(x => x.Currency == "USD").NetCashFlow);
        Assert.All(result.Series, s => Assert.Equal(0, s.Days[1].NetCashFlow));
    }
}
