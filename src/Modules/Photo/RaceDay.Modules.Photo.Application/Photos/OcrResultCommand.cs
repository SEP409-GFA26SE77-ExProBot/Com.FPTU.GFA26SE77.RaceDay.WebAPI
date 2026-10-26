using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Application.Photos;

public sealed record OcrResultTagDto(
    string Bib,
    double Confidence,
    IReadOnlyList<double>? Bbox = null);

public sealed record ApplyOcrResultCommand(
    Guid PhotoId,
    string Status,
    IReadOnlyList<OcrResultTagDto> Tags,
    string? DuplicateOf = null,
    IReadOnlyList<string>? Reasons = null) : IRequest;

public sealed class ApplyOcrResultCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    IEventBibRosterQuery rosterQuery) : IRequestHandler<ApplyOcrResultCommand>
{
    public async Task Handle(ApplyOcrResultCommand request, CancellationToken cancellationToken)
    {
        var photo = await photos.GetByIdWithTagsAsync(request.PhotoId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy ảnh.");

        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (status is "no_bib" or "duplicate" or "failed")
        {
            var reason = status == "duplicate"
                ? $"duplicate:{request.DuplicateOf}"
                : string.Join(',', request.Reasons ?? ["ocr_" + status]);
            photo.Discard(reason);
            photos.Update(photo);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var roster = await rosterQuery.GetConfirmedBibsAsync(photo.EventId, cancellationToken);
        var rosterSet = new HashSet<string>(
            roster.Bibs.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()),
            StringComparer.OrdinalIgnoreCase);

        // Resolve OCR tokens → Exact roster forms (A0020 / 210020), mirroring Event FormatBib.
        var mapped = request.Tags
            .Select(t =>
            {
                var raw = (t.Bib ?? string.Empty).Trim().ToUpperInvariant();
                var resolved = BibRosterResolver.Resolve(raw, rosterSet) ?? raw;
                return (
                    Bib: resolved,
                    Confidence: Math.Clamp(t.Confidence, 0, 1),
                    BboxJson: t.Bbox is { Count: >= 4 }
                        ? $"[{string.Join(',', t.Bbox.Take(4))}]"
                        : null,
                    InRoster: rosterSet.Count == 0
                        || BibRosterResolver.Resolve(raw, rosterSet) is not null);
            })
            .Where(t => t.Bib.Length > 0)
            .GroupBy(t => t.Bib, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .ToList();

        var inRoster = mapped.Where(t => t.InRoster).Select(t => (t.Bib, t.Confidence, t.BboxJson)).ToList();
        var allMapped = mapped.Select(t => (t.Bib, t.Confidence, t.BboxJson)).ToList();

        if (status == "auto_tagged" && inRoster.Count > 0)
        {
            photo.ApplyOcrMatched(inRoster);
        }
        else if (allMapped.Count > 0)
        {
            // Có ứng viên nhưng không auto / không ∈ roster → review
            photo.ApplyOcrNeedsReview(allMapped);
        }
        else
        {
            photo.Discard("no_bib");
        }

        photos.Update(photo);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
