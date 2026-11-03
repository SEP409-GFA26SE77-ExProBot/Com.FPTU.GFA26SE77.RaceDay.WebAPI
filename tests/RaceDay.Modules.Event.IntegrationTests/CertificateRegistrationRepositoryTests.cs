using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Event.Infrastructure.Repositories;

namespace RaceDay.Modules.Event.IntegrationTests;

public sealed class CertificateRegistrationRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EventDbContext _db = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private static readonly DateTime FinishedAt = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

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
    public async Task Lookup_RequiresBothEventAndTokenUser()
    {
        var eventId = Guid.NewGuid();
        AddRegistration(eventId, Guid.NewGuid(), RegistrationStatus.Finished);
        AddRegistration(Guid.NewGuid(), _userId, RegistrationStatus.Finished);
        await _db.SaveChangesAsync();
        var repository = new EventRegistrationRepository(_db);

        Assert.Null(await repository.GetFinishedByEventAndUserAsync(eventId, _userId));

        var ownRegistration = AddRegistration(eventId, _userId, RegistrationStatus.Finished);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await repository.GetFinishedByEventAndUserAsync(eventId, _userId);
        Assert.NotNull(result);
        Assert.Equal(ownRegistration.Id, result.Id);
    }

    [Theory]
    [InlineData(RegistrationStatus.PendingPayment)]
    [InlineData(RegistrationStatus.PendingApproval)]
    [InlineData(RegistrationStatus.Confirmed)]
    [InlineData(RegistrationStatus.Rejected)]
    [InlineData(RegistrationStatus.Cancelled)]
    public async Task Lookup_UnfinishedRegistration_ReturnsNull(RegistrationStatus status)
    {
        var eventId = Guid.NewGuid();
        AddRegistration(eventId, _userId, status);
        await _db.SaveChangesAsync();

        Assert.Null(await new EventRegistrationRepository(_db).GetFinishedByEventAndUserAsync(eventId, _userId));
    }

    [Fact]
    public async Task Lookup_FinishedStatusWithoutFinishTimestamp_ReturnsNull()
    {
        var eventId = Guid.NewGuid();
        var registration = AddRegistration(eventId, _userId, RegistrationStatus.Confirmed);
        _db.Entry(registration).Property(x => x.Status).CurrentValue = RegistrationStatus.Finished;
        await _db.SaveChangesAsync();

        Assert.Null(await new EventRegistrationRepository(_db).GetFinishedByEventAndUserAsync(eventId, _userId));
    }

    [Fact]
    public async Task Lookup_MultipleRegistrations_ReturnsLatestFinishedRace()
    {
        var eventId = Guid.NewGuid();
        AddRegistration(eventId, _userId, RegistrationStatus.Finished, FinishedAt.AddDays(-1));
        var latest = AddRegistration(eventId, _userId, RegistrationStatus.Finished, FinishedAt);
        AddRegistration(eventId, _userId, RegistrationStatus.Confirmed);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await new EventRegistrationRepository(_db).GetFinishedByEventAndUserAsync(eventId, _userId);

        Assert.NotNull(result);
        Assert.Equal(latest.Id, result.Id);
    }

    private EventRegistration AddRegistration(Guid eventId, Guid userId, RegistrationStatus status, DateTime? finishedAt = null)
    {
        var registration = EventRegistration.Create(eventId, Guid.NewGuid(), userId,
            "Runner", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        if (status == RegistrationStatus.Finished)
        {
            registration.Confirm("1001");
            registration.Finish(finishedAt ?? FinishedAt, 3600);
        }
        else
        {
            _db.Entry(registration).Property(x => x.Status).CurrentValue = status;
        }
        _db.Registrations.Add(registration);
        return registration;
    }
}
