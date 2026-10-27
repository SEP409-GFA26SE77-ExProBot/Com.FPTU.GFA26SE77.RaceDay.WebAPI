using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Map.Contracts.Events;
using RaceDay.Modules.Map.Domain.Courses;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Map.Application.Courses;

public interface IRaceCourseRepository
{
    Task<RaceCourse?> GetByEventDistanceAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RaceCourse>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task AddAsync(RaceCourse course, CancellationToken cancellationToken = default);
    void Update(RaceCourse course);
}

public interface IMapUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal static class CourseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CourseConfigDto Empty(Guid distanceId, string title) => new(
        title,
        distanceId,
        0,
        [],
        [],
        [],
        DateTime.UtcNow);

    public static CourseConfigDto FromEntity(RaceCourse course)
    {
        var dto = JsonSerializer.Deserialize<CourseConfigDto>(course.CourseJson, Options);
        if (dto is null)
        {
            return Empty(course.DistanceId, course.Title);
        }

        return dto with
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? course.Title : dto.Title,
            DistanceId = course.DistanceId,
            DistanceKm = dto.DistanceKm > 0 ? dto.DistanceKm : course.DistanceKm,
            Waypoints = dto.Waypoints ?? [],
            Polyline = dto.Polyline ?? [],
            Checkpoints = dto.Checkpoints ?? [],
            UpdatedAt = course.UpdatedAtUtc
        };
    }

    public static string Serialize(CourseConfigDto dto)
        => JsonSerializer.Serialize(dto, Options);

    public static bool IsPublicStatus(string status) =>
        status is "Approved" or "RegistrationOpen" or "Closed" or "Completed";
}

public sealed record GetCourseQuery(Guid EventId, Guid DistanceId) : IRequest<CourseConfigDto>;

public sealed class GetCourseQueryHandler(
    IRaceCourseRepository courses,
    IEventDistanceAccessQuery access,
    IEventStaffAccessQuery staffAccess,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetCourseQuery, CourseConfigDto>
{
    public async Task<CourseConfigDto> Handle(GetCourseQuery request, CancellationToken cancellationToken)
    {
        var info = await access.GetAsync(request.EventId, request.DistanceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải / cự ly.");

        await EnsureCanReadAsync(request.EventId, info, cancellationToken);

        var entity = await courses.GetByEventDistanceAsync(request.EventId, request.DistanceId, cancellationToken);
        if (entity is null)
        {
            return CourseJson.Empty(request.DistanceId, info.DistanceName);
        }

        return CourseJson.FromEntity(entity);
    }

    private async Task EnsureCanReadAsync(
        Guid eventId,
        EventDistanceAccessInfo info,
        CancellationToken cancellationToken)
    {
        if (currentUser.IsAdmin)
        {
            return;
        }

        if (currentUser.UserId is Guid uid && uid == info.OrganizerUserId)
        {
            return;
        }

        // Active event staff (coordinator / volunteer / photographer) — kể cả draft.
        if (currentUser.UserId is Guid staffUserId
            && await staffAccess.ExistsActiveStaffUserAsync(eventId, staffUserId, cancellationToken))
        {
            return;
        }

        if (CourseJson.IsPublicStatus(info.Status))
        {
            return;
        }

        throw new DomainException("Bạn không có quyền xem course của giải này.");
    }
}

public sealed record UpsertCourseCommand(
    Guid EventId,
    Guid DistanceId,
    UpsertCourseRequest Body) : IRequest<CourseConfigDto>;

public sealed class UpsertCourseCommandValidator : AbstractValidator<UpsertCourseCommand>
{
    private static readonly HashSet<string> AllowedTypes = ["start", "cp", "aid", "finish", "water", "medical", "cutoff"];

    public UpsertCourseCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.DistanceId).NotEmpty();
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.DistanceKm).GreaterThanOrEqualTo(0).LessThanOrEqualTo(500);
        RuleFor(x => x.Body.Waypoints).NotNull();
        RuleFor(x => x.Body.Polyline).NotNull();
        RuleFor(x => x.Body.Checkpoints).NotNull();
        RuleFor(x => x.Body.Waypoints!.Count).LessThanOrEqualTo(500);
        RuleFor(x => x.Body.Polyline!.Count).LessThanOrEqualTo(50_000);
        RuleFor(x => x.Body.Checkpoints!.Count).LessThanOrEqualTo(200);
        RuleForEach(x => x.Body.Checkpoints!).ChildRules(cp =>
        {
            cp.RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
            cp.RuleFor(c => c.Type).Must(t => AllowedTypes.Contains(t.Trim().ToLowerInvariant()))
                .WithMessage("Checkpoint type không hợp lệ.");
            cp.RuleFor(c => c.Km).GreaterThanOrEqualTo(0);
        });
        RuleForEach(x => x.Body.Waypoints!).ChildRules(p =>
        {
            p.RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
            p.RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        });
        RuleForEach(x => x.Body.Polyline!).ChildRules(p =>
        {
            p.RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
            p.RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        });
    }
}

public sealed class UpsertCourseCommandHandler(
    IRaceCourseRepository courses,
    IMapUnitOfWork unitOfWork,
    IEventDistanceAccessQuery access,
    ICurrentUserAccessor currentUser,
    IPublisher publisher) : IRequestHandler<UpsertCourseCommand, CourseConfigDto>
{
    public async Task<CourseConfigDto> Handle(UpsertCourseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var info = await access.GetAsync(request.EventId, request.DistanceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải / cự ly.");

        if (!currentUser.IsAdmin && info.OrganizerUserId != userId)
        {
            throw new DomainException("Chỉ organizer của giải mới được lưu course.");
        }

        var body = request.Body;
        var title = string.IsNullOrWhiteSpace(body.Title) ? info.DistanceName : body.Title!.Trim();
        var now = DateTime.UtcNow;
        var dto = new CourseConfigDto(
            title,
            request.DistanceId,
            body.DistanceKm,
            body.Waypoints ?? [],
            body.Polyline ?? [],
            NormalizeCheckpoints(body.Checkpoints),
            now);

        var json = CourseJson.Serialize(dto);
        var existing = await courses.GetByEventDistanceAsync(request.EventId, request.DistanceId, cancellationToken);
        if (existing is null)
        {
            var created = RaceCourse.Create(
                request.EventId,
                request.DistanceId,
                title,
                body.DistanceKm,
                json,
                userId);
            await courses.AddAsync(created, cancellationToken);
        }
        else
        {
            existing.Replace(title, body.DistanceKm, json, userId);
            courses.Update(existing);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // In-process (monolith) — avoid IAM outbox for Map writes.
        await publisher.Publish(
            new IntegrationEventOccurred(
                new CourseSavedIntegrationEvent(
                    request.EventId,
                    request.DistanceId,
                    title,
                    body.DistanceKm,
                    userId)),
            cancellationToken);

        return dto;
    }

    private static IReadOnlyList<RaceCheckpointDto> NormalizeCheckpoints(IReadOnlyList<RaceCheckpointDto>? checkpoints)
    {
        if (checkpoints is null || checkpoints.Count == 0)
        {
            return [];
        }

        return checkpoints.Select(c => c with
        {
            Id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N")[..12] : c.Id.Trim(),
            Name = c.Name.Trim(),
            Type = c.Type.Trim().ToLowerInvariant(),
            Cutoff = string.IsNullOrWhiteSpace(c.Cutoff) ? null : c.Cutoff.Trim()
        }).ToList();
    }
}

public sealed record ListCourseReadyQuery(Guid EventId) : IRequest<IReadOnlyDictionary<Guid, bool>>;

public sealed class ListCourseReadyQueryHandler(
    IRaceCourseRepository courses,
    IEventDistanceAccessQuery access,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListCourseReadyQuery, IReadOnlyDictionary<Guid, bool>>
{
    public async Task<IReadOnlyDictionary<Guid, bool>> Handle(ListCourseReadyQuery request, CancellationToken cancellationToken)
    {
        // Access check via first distance is awkward; verify organizer owns any distance by probing with empty — use list courses only if organizer/admin.
        // Callers pass eventId; we load all saved courses for event and mark ready by polyline.
        _ = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");

        var saved = await courses.ListByEventAsync(request.EventId, cancellationToken);
        if (saved.Count == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        // Authorize using first course's distance
        var probe = await access.GetAsync(request.EventId, saved[0].DistanceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && currentUser.UserId != probe.OrganizerUserId)
        {
            throw new DomainException("Bạn không có quyền xem course của giải này.");
        }

        var map = new Dictionary<Guid, bool>();
        foreach (var c in saved)
        {
            var dto = CourseJson.FromEntity(c);
            map[c.DistanceId] = dto.Polyline.Count >= 2 && dto.DistanceKm > 0;
        }

        return map;
    }
}
