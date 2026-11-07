using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Domain;

namespace RaceDay.Modules.Simulation.Infrastructure;

public sealed class ScopedSimulationFieldSource(IServiceScopeFactory scopes) : ISimulationFieldSource
{
    public async Task<SimulationField> LoadAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var courses = scope.ServiceProvider.GetRequiredService<IRaceCourseReadQuery>();
        var candidates = scope.ServiceProvider.GetRequiredService<ISimulationCandidateQuery>();
        var course = await courses.GetAsync(eventId, distanceId, cancellationToken);
        var eligible = await candidates.ListEligibleAsync(eventId, distanceId, cancellationToken);

        var polyline = course?.Polyline.Select(point => new SimulationCoursePoint(point.Lat, point.Lng)).ToList()
                       ?? [];
        var checkpoints = course?.Checkpoints
                              .Select(checkpoint => new SimulationCourseCheckpoint(
                                  checkpoint.Id,
                                  checkpoint.Name,
                                  checkpoint.Km,
                                  checkpoint.Lat,
                                  checkpoint.Lng,
                                  checkpoint.Type))
                              .ToList()
                          ?? [];
        var athletes = eligible
            .OrderBy(row => row.RegistrationId)
            .Select(row => new SimulationAthleteSeed(
                row.RegistrationId,
                row.Bib,
                row.FullName,
                RaceSimulationEngine.ChipFor(row.RegistrationId)))
            .ToList();

        string? error = course is null
            ? "Chưa có course cho cự ly này."
            : SimulationCourseRules.Validate(course.DistanceKm, polyline, checkpoints);

        return new SimulationField
        {
            EventId = eventId,
            DistanceId = distanceId,
            Title = course?.Title ?? "",
            DistanceKm = course?.DistanceKm ?? 0,
            CourseError = error,
            Polyline = polyline,
            Checkpoints = checkpoints,
            Eligible = athletes
        };
    }
}

public sealed class ScopedSimulationCommitGateway(IServiceScopeFactory scopes) : ISimulationCommitGateway
{
    public async Task<SimulationCommitBatch> CommitAsync(
        SimulationCommitCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<ISimulationResultCommitPort>();
        return await port.CommitAsync(command, cancellationToken);
    }
}
