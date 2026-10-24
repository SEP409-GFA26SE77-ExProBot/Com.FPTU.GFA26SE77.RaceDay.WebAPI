using System.Text;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Application.Photos;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.UnitTests.Application;

public class PhotographerAuthAndUploadTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task BulkUpload_Denies_When_Not_Active_Photographer_Of_Event()
    {
        var staff = Substitute.For<IEventStaffAccessQuery>();
        staff.IsActivePhotographerAsync(EventId, UserId, Arg.Any<CancellationToken>()).Returns(false);

        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns(UserId);

        var handler = new BulkUploadPhotosCommandHandler(
            Substitute.For<IPhotoAssetRepository>(),
            Substitute.For<IPhotoUnitOfWork>(),
            current,
            staff,
            Substitute.For<IFileStorage>());

        var bytes = Encoding.UTF8.GetBytes("fake-jpeg");
        await using var stream = new MemoryStream(bytes);
        var cmd = new BulkUploadPhotosCommand(EventId, [
            new BulkUploadPhotoFile(stream, "a.jpg", "image/jpeg", bytes.Length)
        ]);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(cmd, CancellationToken.None));

        Assert.Contains("photographer Active", ex.Message, StringComparison.OrdinalIgnoreCase);
        await staff.Received(1).IsActivePhotographerAsync(EventId, UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkUpload_Creates_Pending_When_Photographer_Of_Event()
    {
        var staff = Substitute.For<IEventStaffAccessQuery>();
        staff.IsActivePhotographerAsync(EventId, UserId, Arg.Any<CancellationToken>()).Returns(true);

        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns(UserId);

        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ExistsShaAsync(EventId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        storage.UploadAsync(
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new StoredFile("a.jpg", "https://cdn/a.jpg", "image/jpeg", 12));

        PhotoAsset? added = null;
        photos.AddAsync(Arg.Do<PhotoAsset>(p => added = p), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var uow = Substitute.For<IPhotoUnitOfWork>();
        var handler = new BulkUploadPhotosCommandHandler(photos, uow, current, staff, storage);

        var bytes = Encoding.UTF8.GetBytes("fake-jpeg-bytes");
        await using var stream = new MemoryStream(bytes);
        var result = await handler.Handle(
            new BulkUploadPhotosCommand(EventId, [
                new BulkUploadPhotoFile(stream, "a.jpg", "image/jpeg", bytes.Length)
            ]),
            CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.NotNull(added);
        Assert.Equal(PhotoStatus.Pending, added!.Status);
        Assert.Equal("https://cdn/a.jpg", added.Url);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Process_Queues_Pending_Photos()
    {
        var staff = Substitute.For<IEventStaffAccessQuery>();
        staff.IsActivePhotographerAsync(EventId, UserId, Arg.Any<CancellationToken>()).Returns(true);
        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns(UserId);

        var photo = PhotoAsset.CreatePending(
            EventId, UserId, "https://cdn/a.jpg", "abc", "a.jpg", "image/jpeg", 10);
        var photos = Substitute.For<IPhotoAssetRepository>();
        photos.ListPendingForProcessAsync(EventId, 500, Arg.Any<CancellationToken>())
            .Returns([photo]);

        var uow = Substitute.For<IPhotoUnitOfWork>();
        var result = await new ProcessPendingPhotosCommandHandler(photos, uow, current, staff)
            .Handle(new ProcessPendingPhotosCommand(EventId), CancellationToken.None);

        Assert.Equal(1, result.QueuedCount);
        Assert.Equal(PhotoStatus.Queued, photo.Status);
    }

    [Fact]
    public async Task Jwt_Photographer_Role_Alone_Is_Not_Enough_Without_Staff_Row()
    {
        // Guard is staff query only — no JWT role check in handler.
        var staff = Substitute.For<IEventStaffAccessQuery>();
        staff.IsActivePhotographerAsync(EventId, UserId, Arg.Any<CancellationToken>()).Returns(false);
        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns(UserId);

        await Assert.ThrowsAsync<DomainException>(() =>
            BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
                EventId, UserId, staff, CancellationToken.None));
    }
}
