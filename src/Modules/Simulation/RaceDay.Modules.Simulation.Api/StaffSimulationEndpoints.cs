using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Api;

public static class StaffSimulationEndpoints
{
    public sealed record DecisionBody(string Status, string Reason, string? Evidence = null);
    public sealed record ReviewBody(bool Approve, string Reason);
    public sealed record DnfBody(string Reason);

    public static void MapStaffSimulationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/dnf",
            async (Guid runId, Guid registrationId, DnfBody body, SimulationSessionBook book,
                ISimulationAuthorizer authorizer, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            {
                var snapshot = await SimulationAccess.ReadAsync(book, authorizer, runId, ct);
                var athlete = snapshot.Athletes.SingleOrDefault(a => a.RegistrationId == registrationId)
                    ?? throw new DomainException("VĐV không thuộc lượt.");
                var checkpointId = athlete.Splits.LastOrDefault()?.CheckpointId ?? snapshot.Checkpoints[0].Id;
                return Results.Ok(await book.RecordDecisionAsync(runId, registrationId, "DNF", body.Reason,
                    null, UserId(user), checkpointId, organizer: true, cancellationToken: ct));
            });

        api.MapGet("/staff/events/{eventId:guid}/checkpoint", async (Guid eventId,
            [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            Results.Ok(await workspace.ReadAsync(eventId, UserId(user), ct)));

        api.MapPost("/staff/events/{eventId:guid}/runs/{runId:guid}/athletes/{registrationId:guid}/decisions",
            async (Guid eventId, Guid runId, Guid registrationId, DecisionBody body,
                [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ICurrentUserAccessor user, SimulationSessionBook book, CancellationToken ct) =>
            {
                var actorId = UserId(user);
                var view = await workspace.ReadAsync(eventId, actorId, ct);
                if (view.RunId != runId) throw new DomainException("Lượt đã thay đổi. Tải lại checkpoint trước khi thao tác.");
                await book.RecordDecisionAsync(runId, registrationId, body.Status, body.Reason, body.Evidence,
                    actorId, view.Checkpoint.Id, cancellationToken: ct);
                return Results.Ok(await workspace.ReadAsync(eventId, actorId, ct));
            });

        api.MapPost("/runs/{runId:guid}/decisions/{decisionId:guid}/review",
            async (Guid runId, Guid decisionId, ReviewBody body, SimulationSessionBook book,
                ISimulationAuthorizer authorizer, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            {
                await SimulationAccess.AuthorizeAsync(book, authorizer, runId, ct);
                return Results.Ok(await book.ReviewDecisionAsync(runId, decisionId, body.Approve, body.Reason, UserId(user), ct));
            });

        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/decisions",
            async (Guid runId, Guid registrationId, DecisionBody body, SimulationSessionBook book,
                ISimulationAuthorizer authorizer, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            {
                var snapshot = await SimulationAccess.ReadAsync(book, authorizer, runId, ct);
                var athlete = snapshot.Athletes.SingleOrDefault(a => a.RegistrationId == registrationId)
                    ?? throw new DomainException("VĐV không thuộc lượt.");
                var checkpointId = athlete.Splits.LastOrDefault()?.CheckpointId ?? snapshot.Checkpoints[0].Id;
                return Results.Ok(await book.RecordDecisionAsync(runId, registrationId, body.Status, body.Reason,
                    body.Evidence, UserId(user), checkpointId, organizer: true, cancellationToken: ct));
            });
    }

    private static Guid UserId(ICurrentUserAccessor user)
        => user.UserId ?? throw new DomainException("Cần đăng nhập.");
}
