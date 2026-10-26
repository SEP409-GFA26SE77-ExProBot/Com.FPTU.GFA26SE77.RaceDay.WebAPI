using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Application.Disputes;
using RaceDay.Modules.Photo.Application.Photos;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.UnitTests.Application;

public class OcrResultAndSearchTests
{
    private static readonly Guid EventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static IPhotoOcrSettings CreateSettings(double auto = 0.85, double review = 0.50)
    {
        var s = Substitute.For<IPhotoOcrSettings>();
        s.ThresholdAuto.Returns(auto);
        s.ThresholdReviewMin.Returns(review);
        return s;
    }

    [Fact]
    public async Task Ocr_HighConf_Roster_Matched_Drops_Noise()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        photo.MarkQueued();
        photo.MarkProcessing();

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ReloadForOcrApplyAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        var roster = Substitute.For<IEventBibRosterQuery>();
        roster.GetConfirmedBibsAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventBibRosterSnapshot(EventId, "v1", ["A0042"]));

        var uow = Substitute.For<IPhotoUnitOfWork>();
        await new ApplyOcrResultCommandHandler(photos, uow, roster, CreateSettings()).Handle(
            new ApplyOcrResultCommand(photo.Id, "needs_review", [
                new OcrResultTagDto("A0042", 0.98),
                new OcrResultTagDto("ZZ99", 0.99),
                new OcrResultTagDto("2026", 0.90)
            ]),
            CancellationToken.None);

        Assert.Equal(PhotoStatus.Matched, photo.Status);
        Assert.Single(photo.Tags);
        Assert.Equal("A0042", photo.Tags.First().BibNormalized);
    }

    [Fact]
    public async Task Ocr_MidConf_Roster_NeedsReview()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        photo.MarkQueued();
        photo.MarkProcessing();

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ReloadForOcrApplyAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        var roster = Substitute.For<IEventBibRosterQuery>();
        roster.GetConfirmedBibsAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventBibRosterSnapshot(EventId, "v1", ["50001"]));

        await new ApplyOcrResultCommandHandler(
                photos,
                Substitute.For<IPhotoUnitOfWork>(),
                roster,
                CreateSettings())
            .Handle(new ApplyOcrResultCommand(photo.Id, "needs_review", [
                new OcrResultTagDto("50001", 0.72)
            ]), CancellationToken.None);

        // weighted 0.804 but match boost capped at model+0.04 → 0.76 < auto 0.85 → review
        Assert.Equal(PhotoStatus.NeedsReview, photo.Status);
        Assert.Equal("50001", photo.Tags.First().BibNormalized);
        Assert.InRange(photo.Tags.First().Confidence, 0.75, 0.77);
    }

    [Fact]
    public async Task Ocr_Partial_Suffix_NeedsReview_Not_Matched()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        photo.MarkQueued();
        photo.MarkProcessing();

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ReloadForOcrApplyAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        var roster = Substitute.For<IEventBibRosterQuery>();
        roster.GetConfirmedBibsAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventBibRosterSnapshot(EventId, "v1", ["50001"]));

        await new ApplyOcrResultCommandHandler(
                photos,
                Substitute.For<IPhotoUnitOfWork>(),
                roster,
                CreateSettings())
            .Handle(new ApplyOcrResultCommand(photo.Id, "auto_tagged", [
                new OcrResultTagDto("0001", 0.95)
            ]), CancellationToken.None);

        Assert.Equal(PhotoStatus.NeedsReview, photo.Status);
        Assert.Equal("50001", photo.Tags.First().BibNormalized);
        Assert.True(photo.Tags.First().Confidence < 0.95);
    }

    [Fact]
    public async Task Ocr_NoBib_Discards()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ReloadForOcrApplyAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        await new ApplyOcrResultCommandHandler(
                photos,
                Substitute.For<IPhotoUnitOfWork>(),
                Substitute.For<IEventBibRosterQuery>(),
                CreateSettings())
            .Handle(new ApplyOcrResultCommand(photo.Id, "no_bib", []), CancellationToken.None);

        Assert.Equal(PhotoStatus.Discarded, photo.Status);
    }

    [Fact]
    public async Task Ocr_Only_Noise_Outside_Roster_Discards()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        photo.MarkQueued();
        photo.MarkProcessing();

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ReloadForOcrApplyAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        var roster = Substitute.For<IEventBibRosterQuery>();
        roster.GetConfirmedBibsAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventBibRosterSnapshot(EventId, "v1", ["50001"]));

        await new ApplyOcrResultCommandHandler(
                photos,
                Substitute.For<IPhotoUnitOfWork>(),
                roster,
                CreateSettings())
            .Handle(new ApplyOcrResultCommand(photo.Id, "needs_review", [
                new OcrResultTagDto("992", 0.99),
                new OcrResultTagDto("60389", 0.99),
                new OcrResultTagDto("0992", 0.99)
            ]), CancellationToken.None);

        Assert.Equal(PhotoStatus.Discarded, photo.Status);
        Assert.Equal("bib_not_in_roster", photo.DiscardReason);
        Assert.Empty(photo.Tags);
    }

    [Fact]
    public async Task Search_Requires_Login()
    {
        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<DomainException>(() =>
            new SearchPhotosQueryHandler(Substitute.For<IPhotoAssetRepository>(), current)
                .Handle(new SearchPhotosQuery(EventId, "A1"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateDispute_Marks_RunnerReported()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        photo.ApplyOcrMatched([("A1", 1, null)]);

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.GetByIdWithTagsAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);
        var disputes = Substitute.For<IPhotoDisputeRepository>();
        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns(UserId);

        var dto = await new CreatePhotoDisputeCommandHandler(
                photos, disputes, Substitute.For<IPhotoUnitOfWork>(), current)
            .Handle(new CreatePhotoDisputeCommand(photo.Id, "Sai người"), CancellationToken.None);

        Assert.Equal(PhotoStatus.RunnerReported, photo.Status);
        Assert.Equal("open", dto.Status);
        await disputes.Received(1).AddAsync(Arg.Any<PhotoDispute>(), Arg.Any<CancellationToken>());
    }
}
