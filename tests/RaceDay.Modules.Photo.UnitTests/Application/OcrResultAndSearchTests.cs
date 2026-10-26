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

    [Fact]
    public async Task Ocr_AutoTagged_Only_Keeps_Roster_Bibs()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        photo.MarkQueued();
        photo.MarkProcessing();

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.GetByIdWithTagsAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        var roster = Substitute.For<IEventBibRosterQuery>();
        roster.GetConfirmedBibsAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventBibRosterSnapshot(EventId, "v1", ["A0042"]));

        var uow = Substitute.For<IPhotoUnitOfWork>();
        await new ApplyOcrResultCommandHandler(photos, uow, roster).Handle(
            new ApplyOcrResultCommand(photo.Id, "auto_tagged", [
                new OcrResultTagDto("A0042", 0.98),
                new OcrResultTagDto("ZZ99", 0.99)
            ]),
            CancellationToken.None);

        Assert.Equal(PhotoStatus.Matched, photo.Status);
        Assert.Single(photo.Tags);
        Assert.Equal("A0042", photo.Tags.First().BibNormalized);
    }

    [Fact]
    public async Task Ocr_NoBib_Discards()
    {
        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", null, "a.jpg", "image/jpeg", 10);
        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.GetByIdWithTagsAsync(photo.Id, Arg.Any<CancellationToken>()).Returns(photo);

        await new ApplyOcrResultCommandHandler(
                photos,
                Substitute.For<IPhotoUnitOfWork>(),
                Substitute.For<IEventBibRosterQuery>())
            .Handle(new ApplyOcrResultCommand(photo.Id, "no_bib", []), CancellationToken.None);

        Assert.Equal(PhotoStatus.Discarded, photo.Status);
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
