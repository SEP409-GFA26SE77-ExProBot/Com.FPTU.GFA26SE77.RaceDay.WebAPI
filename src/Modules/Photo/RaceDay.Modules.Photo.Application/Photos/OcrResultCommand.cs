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

/// <summary>
/// Roster-first + fused confidence (chuẩn mạnh nhất):
/// final = 0.7×model + 0.3×match − penalty.
/// Matched: match≥0.95, final≥auto, không ambiguous.
/// NeedsReview: match≥0.70 và (final trong band review | ambiguous | partial).
/// Discard: không hit roster đủ match.
/// Chỉ lưu bib roster — detail không hiện số lạ.
/// </summary>
public sealed class ApplyOcrResultCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    IEventBibRosterQuery rosterQuery,
    IPhotoOcrSettings ocrSettings) : IRequestHandler<ApplyOcrResultCommand>
{
    public async Task Handle(ApplyOcrResultCommand request, CancellationToken cancellationToken)
    {
        var photo = await photos.ReloadForOcrApplyAsync(request.PhotoId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy ảnh.");

        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        // duplicate/failed: discard ngay. no_bib chỉ discard khi không còn tag —
        // nếu OCR vẫn gửi candidate thì WebAPI fused roster xử lý (tránh loại sớm phía service).
        if (status is "duplicate" or "failed"
            || (status == "no_bib" && (request.Tags is null || request.Tags.Count == 0)))
        {
            var reason = status == "duplicate"
                ? $"duplicate:{request.DuplicateOf}"
                : string.Join(',', request.Reasons ?? ["ocr_" + status]);
            photo.Discard(reason);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var roster = await rosterQuery.GetConfirmedBibsAsync(photo.EventId, cancellationToken);
        var rosterSet = new HashSet<string>(
            roster.Bibs.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (rosterSet.Count == 0)
        {
            photo.Discard("empty_roster");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var thrAuto = ocrSettings.ThresholdAuto;
        var thrReview = ocrSettings.ThresholdReviewMin;

        var fused = new List<(string Bib, double FinalConf, double MatchScore, bool Ambiguous, string? BboxJson)>();
        foreach (var t in request.Tags)
        {
            var raw = (t.Bib ?? string.Empty).Trim();
            var hit = BibConfidenceFusion.FindBest(raw, rosterSet);
            if (hit is null)
            {
                continue;
            }

            var model = Math.Clamp(t.Confidence, 0, 1);
            var finalConf = BibConfidenceFusion.Fuse(model, hit.Value.MatchScore, hit.Value.Ambiguous);
            var bbox = t.Bbox is { Count: >= 4 }
                ? $"[{string.Join(',', t.Bbox.Take(4))}]"
                : null;
            fused.Add((hit.Value.RosterBib, finalConf, hit.Value.MatchScore, hit.Value.Ambiguous, bbox));
        }

        // Một bib roster: giữ conf fused cao nhất.
        var bestPerBib = fused
            .GroupBy(x => x.Bib, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.FinalConf).First())
            .ToList();

        var autoHits = bestPerBib
            .Where(x =>
                !x.Ambiguous
                && x.MatchScore >= BibConfidenceFusion.MatchAutoMin
                && x.FinalConf >= thrAuto)
            .Select(x => (x.Bib, x.FinalConf, x.BboxJson))
            .ToList();

        if (autoHits.Count > 0)
        {
            photo.ApplyOcrMatched(autoHits);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var reviewHits = bestPerBib
            .Where(x =>
                x.MatchScore >= BibConfidenceFusion.MatchReviewMin
                && (x.FinalConf >= thrReview
                    || x.Ambiguous
                    || x.MatchScore < BibConfidenceFusion.MatchAutoMin))
            .Select(x => (x.Bib, x.FinalConf, x.BboxJson))
            .ToList();

        // Ambiguous / partial luôn review nếu đã qua match sàn.
        if (reviewHits.Count == 0)
        {
            reviewHits = bestPerBib
                .Where(x => x.MatchScore >= BibConfidenceFusion.MatchReviewMin && x.FinalConf >= thrReview)
                .Select(x => (x.Bib, x.FinalConf, x.BboxJson))
                .ToList();
        }

        if (reviewHits.Count > 0)
        {
            photo.ApplyOcrNeedsReview(reviewHits);
        }
        else
        {
            photo.Discard(request.Tags.Count > 0 ? "bib_not_in_roster" : "no_bib");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
