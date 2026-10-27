using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.UnitTests.Domain;

public class PhotoAssetWorkflowTests
{
    [Fact]
    public void ApplyOcrMatched_Sets_Matched_And_Tags()
    {
        var photo = PhotoAsset.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), "https://x/a.jpg", null, "a.jpg", "image/jpeg", 1);
        photo.MarkQueued();
        photo.MarkProcessing();
        photo.ApplyOcrMatched([("A0042", 0.99, "[0,0,1,1]")]);

        Assert.Equal(PhotoStatus.Matched, photo.Status);
        Assert.Single(photo.Tags);
        Assert.Equal("A0042", photo.Tags.First().BibNormalized);
        Assert.False(photo.IsPublished);
        Assert.False(photo.IsSearchable);
        Assert.True(photo.CanPublish);

        photo.Publish();
        Assert.True(photo.IsPublished);
        Assert.True(photo.IsSearchable);
    }

    [Fact]
    public void Discard_Clears_Tags()
    {
        var photo = PhotoAsset.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), "https://x/a.jpg", null, "a.jpg", "image/jpeg", 1);
        photo.ApplyOcrNeedsReview([("X1", 0.5, null)]);
        photo.Discard("no_bib");

        Assert.Equal(PhotoStatus.Discarded, photo.Status);
        Assert.Empty(photo.Tags);
        Assert.Equal("no_bib", photo.DiscardReason);
    }

    [Fact]
    public void CorrectBib_Only_From_Review_States()
    {
        var photo = PhotoAsset.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), "https://x/a.jpg", null, "a.jpg", "image/jpeg", 1);
        Assert.Throws<DomainException>(() => photo.CorrectBib("A1"));

        photo.ApplyOcrNeedsReview([("A1", 0.7, null)]);
        photo.CorrectBib("B2");
        Assert.Equal(PhotoStatus.ManualCorrected, photo.Status);
        Assert.Equal("B2", photo.Tags.Single().BibNormalized);
        Assert.Equal(PhotoTagSource.Manual, photo.Tags.Single().Source);
    }

    [Fact]
    public void Dispute_Open_Approve_Reject()
    {
        var photoId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var d = PhotoDispute.Open(photoId, eventId, Guid.NewGuid(), "Sai BIB");
        Assert.Equal(PhotoDisputeStatus.Open, d.Status);

        d.Approve(Guid.NewGuid());
        Assert.Equal(PhotoDisputeStatus.Approved, d.Status);

        var d2 = PhotoDispute.Open(photoId, eventId, Guid.NewGuid(), "Blur");
        d2.Reject(Guid.NewGuid(), "OK thật");
        Assert.Equal(PhotoDisputeStatus.Rejected, d2.Status);
    }
}
