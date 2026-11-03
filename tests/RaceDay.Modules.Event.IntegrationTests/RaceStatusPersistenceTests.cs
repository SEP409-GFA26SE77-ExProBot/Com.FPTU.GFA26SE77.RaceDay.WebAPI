using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Event.Infrastructure.Repositories;

namespace RaceDay.Modules.Event.IntegrationTests;

public class RaceStatusPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private static readonly DateTime Now = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
    private EventDbContext Db() => new(new DbContextOptionsBuilder<EventDbContext>().UseSqlite(_connection).Options);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    private async Task<Guid> SeedAsync(bool running = true)
    {
        await using var db = Db();
        var reg = EventRegistration.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Runner", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        reg.Confirm("1001");
        if (running) reg.Start(Now.AddHours(-1));
        db.Registrations.Add(reg);
        await db.SaveChangesAsync();
        return reg.Id;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentFinishAndDnf_OnlyOneCommits_WithAtomicAudit(bool decisionFirst)
    {
        var id = await SeedAsync();
        await using var finishDb = Db();
        await using var decisionDb = Db();
        var finishReg = (await new EventRegistrationRepository(finishDb).GetByIdAsync(id))!;
        var decisionReg = (await new EventRegistrationRepository(decisionDb).GetByIdAsync(id))!;
        finishReg.Finish(Now, 3600);
        new EventRegistrationRepository(finishDb).Update(finishReg);
        var change = decisionReg.ChangeRaceStatus(RaceStatus.DNF, "Stopped", Guid.NewGuid(), Now, null);
        new EventRegistrationRepository(decisionDb).Update(decisionReg);
        await new RaceStatusChangeRepository(decisionDb).AddAsync(change);

        var winner = decisionFirst ? decisionDb : finishDb;
        var loser = decisionFirst ? finishDb : decisionDb;
        await winner.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync());

        await using var verify = Db();
        var persisted = await verify.Registrations.SingleAsync();
        Assert.Equal(decisionFirst ? RaceStatus.DNF : RaceStatus.Finished, persisted.RaceStatus);
        Assert.Equal(decisionFirst ? 1 : 0, await verify.RaceStatusChanges.CountAsync());
    }

    [Fact]
    public async Task ConcurrentDnsAndStart_CannotOverwriteDecision()
    {
        var id = await SeedAsync(false);
        await using var startDb = Db();
        await using var dnsDb = Db();
        var starting = (await startDb.Registrations.FindAsync(id))!;
        var absent = (await dnsDb.Registrations.FindAsync(id))!;
        starting.Start(Now);
        dnsDb.RaceStatusChanges.Add(absent.ChangeRaceStatus(RaceStatus.DNS, "Absent", Guid.NewGuid(), Now, Now));
        await dnsDb.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => startDb.SaveChangesAsync());
    }

    [Fact]
    public async Task DqAfterFinish_PersistsAuditAndPreservesTiming_ExcludedFromCertificateLookup()
    {
        var id = await SeedAsync();
        await using (var db = Db())
        {
            var reg = (await db.Registrations.FindAsync(id))!;
            reg.Finish(Now, 3600);
            await db.SaveChangesAsync();
            var change = reg.ChangeRaceStatus(RaceStatus.DQ, "Rule violation", Guid.NewGuid(), Now, null);
            await new RaceStatusChangeRepository(db).AddAsync(change);
            await db.SaveChangesAsync();
        }

        await using var verify = Db();
        var persisted = (await verify.Registrations.FindAsync(id))!;
        var audit = await new RaceStatusChangeRepository(verify).GetLatestAsync(id);
        Assert.NotNull(audit);
        Assert.Equal(RaceStatus.Finished, audit.PreviousStatus);
        Assert.Equal(RaceStatus.DQ, audit.Status);
        Assert.Equal("Rule violation", audit.Reason);
        Assert.Equal(RaceStatus.DQ, persisted.RaceStatus);
        Assert.Equal(RegistrationStatus.Finished, persisted.Status);
        Assert.Equal(Now, persisted.FinishedAtUtc);
        Assert.Equal(3600, persisted.ResultSeconds);
        Assert.Null(await new EventRegistrationRepository(verify).GetFinishedByEventAndUserAsync(persisted.EventId, persisted.UserId));
    }
}
