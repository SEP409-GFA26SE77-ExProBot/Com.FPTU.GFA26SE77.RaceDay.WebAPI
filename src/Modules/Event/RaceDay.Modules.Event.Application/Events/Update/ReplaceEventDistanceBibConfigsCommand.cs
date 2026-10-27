using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events.Update;

public sealed record DistanceBibConfigInput(
    Guid DistanceId,
    string? BibPrefix,
    int BibRangeStart,
    int BibRangeEnd);

public sealed record ReplaceEventDistanceBibConfigsCommand(
    Guid EventId,
    IReadOnlyList<DistanceBibConfigInput> Distances) : IRequest<RaceEventDto>;

public sealed class DistanceBibConfigInputValidator : AbstractValidator<DistanceBibConfigInput>
{
    public DistanceBibConfigInputValidator()
    {
        RuleFor(x => x.DistanceId).NotEmpty();
        RuleFor(x => x.BibRangeStart).InclusiveBetween(1, 9999);
        RuleFor(x => x.BibRangeEnd).InclusiveBetween(1, 9999);
        RuleFor(x => x)
            .Must(x => x.BibRangeEnd >= x.BibRangeStart)
            .WithMessage("Đến số phải ≥ từ số.");
        RuleFor(x => x.BibPrefix)
            .MaximumLength(12)
            .When(x => !string.IsNullOrWhiteSpace(x.BibPrefix));
        RuleFor(x => x.BibPrefix)
            .Matches(@"^[A-Za-z0-9]+-?$")
            .When(x => !string.IsNullOrWhiteSpace(x.BibPrefix))
            .WithMessage("Prefix BIB chỉ gồm chữ/số (A–Z, 0–9), có thể kết thúc bằng '-'.");
    }
}

public sealed class ReplaceEventDistanceBibConfigsCommandValidator
    : AbstractValidator<ReplaceEventDistanceBibConfigsCommand>
{
    public ReplaceEventDistanceBibConfigsCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Distances).NotEmpty()
            .WithMessage("Cần cấu hình BIB cho ít nhất một cự ly.");
        RuleForEach(x => x.Distances).SetValidator(new DistanceBibConfigInputValidator());
    }
}

public sealed class ReplaceEventDistanceBibConfigsCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork)
    : IRequestHandler<ReplaceEventDistanceBibConfigsCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(
        ReplaceEventDistanceBibConfigsCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        race.ApplyDistanceBibConfigs(
            request.Distances
                .Select(d => (d.DistanceId, d.BibPrefix, d.BibRangeStart, d.BibRangeEnd))
                .ToList());

        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải sau khi lưu BIB.");

        return RaceEventDtoMapper.ToDetail(refreshed);
    }
}
