using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Api;

public static class StaffSimulationEndpoints
{
    public sealed record DecisionBody(string Status, string Reason, string? Evidence = null, Guid? OperationId = null);
    public sealed record ReviewBody(bool Approve, string Reason, Guid? OperationId = null);
    public sealed record DnfBody(string Reason, Guid? OperationId = null);

    public static void MapStaffSimulationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/dnf",
            async (Guid runId, Guid registrationId, DnfBody body, ClaimsPrincipal principal, SimulationSessionBook book,
                ISimulationAuthorizer authorizer, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            {
                var snapshot = await SimulationAccess.ReadAsync(book, authorizer, runId, ct);
                var athlete = snapshot.Athletes.SingleOrDefault(a => a.RegistrationId == registrationId)
                    ?? throw new DomainException("VĐV không thuộc lượt.");
                var checkpointId = athlete.Splits.LastOrDefault()?.CheckpointId ?? snapshot.Checkpoints[0].Id;
                return Results.Ok(await book.RecordDecisionAsync(runId, registrationId, "DNF", body.Reason,
                    null, UserId(user), checkpointId, organizer: true, cancellationToken: ct, operationId: body.OperationId, actorName: ActorName(principal)));
            });

        api.MapGet("/staff/events/{eventId:guid}/checkpoint", async (Guid eventId,
            [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            Results.Ok(await workspace.ReadAsync(eventId, UserId(user), ct)));

        api.MapGet("/staff/events/{eventId:guid}/checkpoint/history/runs", async (Guid eventId,
            [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ISimulationJournalStore journal,
            [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
        {
            var checkpoint = await workspace.ReadAsync(eventId, UserId(user), ct);
            return Results.Ok(await journal.RunsAsync(eventId, checkpoint.DistanceId, checkpoint.Checkpoint.Id, ct));
        });

        api.MapGet("/staff/events/{eventId:guid}/checkpoint/history", async (Guid eventId, Guid runId,
            string? kind, string? search, bool? mine, long? cursor,
            [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ISimulationJournalStore journal,
            [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
        {
            var checkpoint = await workspace.ReadAsync(eventId, UserId(user), ct);
            return Results.Ok(await journal.ReadAsync(new(eventId, checkpoint.DistanceId, checkpoint.Checkpoint.Id,
                runId, kind, search, mine == true ? UserId(user) : null, cursor), ct));
        });

        api.MapGet("/staff/events/{eventId:guid}/checkpoint/export", async (Guid eventId,
            [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ICheckpointResultExportStore exports,
            [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
        {
            var checkpoint = await workspace.ReadAsync(eventId, UserId(user), ct);
            return Results.Ok(checkpoint.RunId is Guid runId
                ? await exports.GetAsync(runId, checkpoint.Checkpoint.Id, ct) : null);
        });

        api.MapPost("/staff/events/{eventId:guid}/runs/{runId:guid}/checkpoint/export", async (
            Guid eventId, Guid runId, [FromServices] IStaffCheckpointWorkspace workspace,
            [FromServices] ICurrentUserAccessor user,
            [FromServices] SimulationSessionBook book, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var actorId = UserId(user);
            var checkpoint = await workspace.ReadAsync(eventId, actorId, ct);
            if (checkpoint.RunId != runId) throw new DomainException("Lượt đã thay đổi. Tải lại checkpoint trước khi gửi.");
            return Results.Ok(await book.SubmitCheckpointExportAsync(runId, checkpoint.Checkpoint.Id, actorId, ActorName(principal), ct,
                token => EnsureAssignmentAsync(workspace, eventId, actorId, checkpoint.DistanceId, checkpoint.Checkpoint.Id, token)));
        });

        api.MapGet("/events/{eventId:guid}/distances/{distanceId:guid}/checkpoint-exports", async (
            Guid eventId, Guid distanceId, [FromServices] ICheckpointResultExportStore exports,
            [FromServices] ISimulationAuthorizer authorizer, CancellationToken ct) =>
        {
            await authorizer.EnsureCanManageAsync(eventId, ct);
            return Results.Ok(await exports.ListAsync(eventId, distanceId, ct));
        });

        api.MapPost("/staff/events/{eventId:guid}/runs/{runId:guid}/athletes/{registrationId:guid}/decisions",
            async (Guid eventId, Guid runId, Guid registrationId, DecisionBody body, ClaimsPrincipal principal,
                [FromServices] IStaffCheckpointWorkspace workspace, [FromServices] ICurrentUserAccessor user, SimulationSessionBook book, CancellationToken ct) =>
            {
                var actorId = UserId(user);
                var view = await workspace.ReadAsync(eventId, actorId, ct);
                if (view.RunId != runId) throw new DomainException("Lượt đã thay đổi. Tải lại checkpoint trước khi thao tác.");
                await book.RecordDecisionAsync(runId, registrationId, body.Status, body.Reason, body.Evidence,
                    actorId, view.Checkpoint.Id, cancellationToken: ct, operationId: body.OperationId, actorName: ActorName(principal),
                    validateAccess: token => EnsureAssignmentAsync(workspace, eventId, actorId, view.DistanceId, view.Checkpoint.Id, token));
                return Results.Ok(await workspace.ReadAsync(eventId, actorId, ct));
            });

        api.MapPost("/runs/{runId:guid}/decisions/{decisionId:guid}/review",
            async (Guid runId, Guid decisionId, ReviewBody body, ClaimsPrincipal principal, SimulationSessionBook book,
                ISimulationAuthorizer authorizer, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            {
                await SimulationAccess.AuthorizeAsync(book, authorizer, runId, ct);
                return Results.Ok(await book.ReviewDecisionAsync(runId, decisionId, body.Approve, body.Reason, UserId(user), ct, body.OperationId, ActorName(principal)));
            });

        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/decisions",
            async (Guid runId, Guid registrationId, DecisionBody body, ClaimsPrincipal principal, SimulationSessionBook book,
                ISimulationAuthorizer authorizer, [FromServices] ICurrentUserAccessor user, CancellationToken ct) =>
            {
                var snapshot = await SimulationAccess.ReadAsync(book, authorizer, runId, ct);
                var athlete = snapshot.Athletes.SingleOrDefault(a => a.RegistrationId == registrationId)
                    ?? throw new DomainException("VĐV không thuộc lượt.");
                var checkpointId = athlete.Splits.LastOrDefault()?.CheckpointId ?? snapshot.Checkpoints[0].Id;
                return Results.Ok(await book.RecordDecisionAsync(runId, registrationId, body.Status, body.Reason,
                    body.Evidence, UserId(user), checkpointId, organizer: true, cancellationToken: ct, operationId: body.OperationId, actorName: ActorName(principal)));
            });
    }

    private static async Task EnsureAssignmentAsync(IStaffCheckpointWorkspace workspace, Guid eventId, Guid actorId,
        Guid distanceId, string checkpointId, CancellationToken ct)
    {
        var current = await workspace.ReadAsync(eventId, actorId, ct);
        if (current.DistanceId != distanceId || current.Checkpoint.Id != checkpointId)
            throw new StaffCheckpointAccessException("Phân công đã thay đổi. Tải lại checkpoint trước khi thao tác.");
    }

    private static string ActorName(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name")
        ?? principal.FindFirstValue("username") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";

    private static Guid UserId(ICurrentUserAccessor user)
        => user.UserId ?? throw new DomainException("Cần đăng nhập.");
}
