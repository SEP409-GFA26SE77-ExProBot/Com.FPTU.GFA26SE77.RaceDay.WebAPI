using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Domain.Disputes;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Event.Infrastructure.Repositories;

namespace RaceDay.Modules.Event.IntegrationTests;

public sealed class RunnerDisputeRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EventDbContext _db = null!;
    private readonly Guid _runnerId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 26);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new EventDbContext(new DbContextOptionsBuilder<EventDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RecentEvents_IncludesBothDateBoundaries_ExcludesFutureOldAndOtherRunners()
    {
        var lowerBoundary = await AddEvent(Today.AddMonths(-1));
        var upperBoundary = await AddEvent(Today);
        await AddEvent(Today.AddMonths(-1).AddDays(-1));
        await AddEvent(Today.AddDays(1));
        await AddEvent(Today.AddDays(-1), runnerId: Guid.NewGuid());
        await _db.SaveChangesAsync();

        var query = new RecentJoinedEventQuery(_db);
        var result = await query.ListAsync(_runnerId, Today, 1, 20);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([upperBoundary.Id, lowerBoundary.Id], result.Items.Select(x => x.Id));
        Assert.Null(await query.GetAsync(Guid.NewGuid(), lowerBoundary.Id, Today));
    }

    [Theory]
    [InlineData(RegistrationStatus.PendingPayment, false)]
    [InlineData(RegistrationStatus.PendingApproval, false)]
    [InlineData(RegistrationStatus.Confirmed, true)]
    [InlineData(RegistrationStatus.Finished, true)]
    [InlineData(RegistrationStatus.Rejected, false)]
    [InlineData(RegistrationStatus.Cancelled, false)]
    public async Task RecentEvents_FiltersRegistrationStatus(RegistrationStatus status, bool eligible)
    {
        var race = await AddEvent(Today, status);
        await _db.SaveChangesAsync();
        var query = new RecentJoinedEventQuery(_db);

        var result = await query.ListAsync(_runnerId, Today, 1, 20);
        var single = await query.GetAsync(_runnerId, race.Id, Today);

        Assert.Equal(eligible ? 1 : 0, result.TotalCount);
        Assert.Equal(eligible, single is not null);
    }

    [Theory]
    [InlineData(RaceEventStatus.Cancelled)]
    [InlineData(RaceEventStatus.Suspended)]
    [InlineData(RaceEventStatus.Draft)]
    [InlineData(RaceEventStatus.Rejected)]
    public async Task RecentEvents_UnavailableEvent_Excluded(RaceEventStatus status)
    {
        var race = await AddEvent(Today);
        _db.Entry(race).Property(x => x.Status).CurrentValue = status;
        await _db.SaveChangesAsync();
        var query = new RecentJoinedEventQuery(_db);

        Assert.Empty((await query.ListAsync(_runnerId, Today, 1, 20)).Items);
        Assert.Null(await query.GetAsync(_runnerId, race.Id, Today));
    }

    [Fact]
    public async Task RecentEvents_MultipleDistances_ReturnsOneEvent_AndPaginates()
    {
        var race = await AddEvent(Today);
        _db.Registrations.Add(Registration(race.Id, RegistrationStatus.Finished, _runnerId));
        var older = await AddEvent(Today.AddDays(-1));
        await _db.SaveChangesAsync();
        var query = new RecentJoinedEventQuery(_db);

        var page1 = await query.ListAsync(_runnerId, Today, 1, 1);
        var page2 = await query.ListAsync(_runnerId, Today, 2, 1);

        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(race.Id, Assert.Single(page1.Items).Id);
        Assert.Equal(older.Id, Assert.Single(page2.Items).Id);
    }

    [Fact]
    public async Task RecentEvents_MonthEnd_UsesCalendarMonthInsteadOfThirtyDays()
    {
        var today = new DateOnly(2026, 3, 31);
        var boundary = await AddEvent(new(2026, 2, 28));
        await AddEvent(new(2026, 2, 27));
        await _db.SaveChangesAsync();

        var result = await new RecentJoinedEventQuery(_db).ListAsync(_runnerId, today, 1, 20);

        Assert.Equal(boundary.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task Disputes_ReloadFromDatabase_IsolateRunner_AndListNewestFirst()
    {
        var race = await AddEvent(Today);
        var now = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
        var repository = new EventDisputeRepository(_db);
        var older = EventDispute.Create(race.Id, _runnerId, "Older", "First complaint", now.AddDays(-1));
        var newest = EventDispute.Create(race.Id, _runnerId, "Newest", "Latest complaint", now);
        await repository.AddAsync(older);
        await repository.AddAsync(newest);
        await repository.AddAsync(EventDispute.Create(race.Id, Guid.NewGuid(), "Other runner", "Private complaint", now));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var first = await repository.ListByRunnerAsync(_runnerId, 1, 1);
        var second = await repository.ListByRunnerAsync(_runnerId, 2, 1);

        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(newest.Id, Assert.Single(first.Items).Id);
        Assert.Equal(race.Name, first.Items[0].EventName);
        Assert.Equal("open", first.Items[0].Status);
        Assert.Equal(older.Id, Assert.Single(second.Items).Id);
        Assert.Empty((await repository.ListByRunnerAsync(Guid.NewGuid(), 1, 10)).Items);
    }

    private async Task<RaceEvent> AddEvent(DateOnly date,
        RegistrationStatus registrationStatus = RegistrationStatus.Confirmed, Guid? runnerId = null)
    {
        var race = RaceEvent.CreateDraft(Guid.NewGuid(), "Test Run", "Hanoi", date,
            new GeoPoint(21, 105), OrganizerInfo.Create("Organizer", "org@test.vn", "0900000000"), 500);
        _db.Entry(race).Property(x => x.Status).CurrentValue = RaceEventStatus.Completed;
        await _db.RaceEvents.AddAsync(race);
        await _db.Registrations.AddAsync(Registration(race.Id, registrationStatus, runnerId ?? _runnerId));
        return race;
    }

    private EventRegistration Registration(Guid eventId, RegistrationStatus status, Guid runnerId)
    {
        var registration = EventRegistration.Create(eventId, Guid.NewGuid(), runnerId,
            "Runner", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        _db.Entry(registration).Property(x => x.Status).CurrentValue = status;
        return registration;
    }
}
