using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Event.Application.Simulation;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Infrastructure.Simulation;

public sealed class ConfigurationSimulationDemoGate(IConfiguration configuration) : ISimulationDemoGate
{
    public bool Enabled => SimulationFeatureFlag.IsEnabled(configuration["Simulation:Enabled"]);

    public string? StartTimeZone => configuration["Simulation:StartTimeZone"];
}

public sealed class SimulationCandidateQuery(EventDbContext db) : ISimulationCandidateQuery
{
    public async Task<IReadOnlyList<SimulationRegistrationState>> ListEligibleAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Registrations.AsNoTracking()
            .Where(registration => registration.EventId == eventId
                                   && registration.DistanceId == distanceId
                                   && registration.Status == RegistrationStatus.Confirmed
                                   && registration.RaceStatus == RaceStatus.NotStarted
                                   && registration.StartedAtUtc == null
                                   && registration.FinishedAtUtc == null
                                   && registration.ResultSeconds == null
                                   && registration.BibNumber != null
                                   && registration.BibNumber != "")
            .OrderBy(registration => registration.Id)
            .Select(registration => new
            {
                registration.Id,
                registration.EventId,
                registration.DistanceId,
                registration.BibNumber,
                registration.FullName,
                registration.Status,
                registration.RaceStatus,
                registration.StartedAtUtc,
                registration.FinishedAtUtc,
                registration.ResultSeconds
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => Map(
            row.Id, row.EventId, row.DistanceId, row.BibNumber, row.FullName,
            row.Status, row.RaceStatus, row.StartedAtUtc, row.FinishedAtUtc, row.ResultSeconds)).ToList();
    }

    public async Task<SimulationRegistrationState?> GetAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Registrations.AsNoTracking()
            .Where(registration => registration.Id == registrationId)
            .Select(registration => new
            {
                registration.Id,
                registration.EventId,
                registration.DistanceId,
                registration.BibNumber,
                registration.FullName,
                registration.Status,
                registration.RaceStatus,
                registration.StartedAtUtc,
                registration.FinishedAtUtc,
                registration.ResultSeconds
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : Map(row.Id, row.EventId, row.DistanceId, row.BibNumber, row.FullName,
                row.Status, row.RaceStatus, row.StartedAtUtc, row.FinishedAtUtc, row.ResultSeconds);
    }

    private static SimulationRegistrationState Map(
        Guid id,
        Guid eventId,
        Guid distanceId,
        string? bib,
        string fullName,
        RegistrationStatus status,
        RaceStatus raceStatus,
        DateTime? startedAtUtc,
        DateTime? finishedAtUtc,
        long? resultSeconds)
        => new(
            id,
            eventId,
            distanceId,
            bib ?? "",
            fullName,
            status.ToString(),
            raceStatus.ToString(),
            startedAtUtc,
            finishedAtUtc,
            resultSeconds);
}

public sealed class SimulationOfficialStartSource(EventDbContext db) : ISimulationOfficialStartSource
{
    public async Task<OfficialStartClocks?> GetClocksAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
            from distance in db.Distances.AsNoTracking()
            join race in db.RaceEvents.AsNoTracking() on distance.EventId equals race.Id
            where distance.EventId == eventId && distance.Id == distanceId
            select new { race.EventDate, distance.StartAt, race.StartTime }
        ).FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new OfficialStartClocks(row.EventDate, row.StartAt, row.StartTime);
    }
}

public sealed class EfSimulationFinishWriter(EventDbContext db, ISimulationDemoGate gate) : ISimulationFinishWriter
{
    public async Task<int> WriteAsync(
        Guid registrationId,
        Guid eventId,
        Guid distanceId,
        string bib,
        DateTime finishUtc,
        long resultSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!gate.Enabled)
        {
            throw new DomainException("Giả lập đang tắt.");
        }

        var entity = await db.Registrations.FirstOrDefaultAsync(registration => registration.Id == registrationId, cancellationToken);
        if (entity is null)
        {
            return 0;
        }

        bool finished;
        try
        {
            finished = entity.Finish(finishUtc, resultSeconds);
        }
        finally
        {
            db.Entry(entity).State = EntityState.Detached;
        }

        if (!finished)
        {
            return 0;
        }

        // bib is the run's original bib. A registration changed to another bib does not match.
        return await db.Registrations
            .Where(registration => registration.Id == registrationId
                                   && registration.EventId == eventId
                                   && registration.DistanceId == distanceId
                                   && registration.BibNumber == bib
                                   && registration.Status == RegistrationStatus.Confirmed
                                   && registration.RaceStatus == RaceStatus.NotStarted
                                   && registration.StartedAtUtc == null
                                   && registration.FinishedAtUtc == null
                                   && registration.ResultSeconds == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(registration => registration.FinishedAtUtc, finishUtc)
                .SetProperty(registration => registration.ResultSeconds, resultSeconds)
                .SetProperty(registration => registration.Status, RegistrationStatus.Finished)
                .SetProperty(registration => registration.RaceStatus, RaceStatus.Finished)
                .SetProperty(registration => registration.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
    }
}
