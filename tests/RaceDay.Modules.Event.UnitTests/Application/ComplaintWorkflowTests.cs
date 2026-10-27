using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Complaints;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class ComplaintWorkflowTests
{
    private readonly Guid _runnerId = Guid.NewGuid();
    private readonly Guid _organizerId = Guid.NewGuid();
    private readonly IEventComplaintRepository _complaints = Substitute.For<IEventComplaintRepository>();
    private readonly IRaceEventRepository _events = Substitute.For<IRaceEventRepository>();
    private readonly ICurrentUserAccessor _current = Substitute.For<ICurrentUserAccessor>();
    private readonly IEventUnitOfWork _unitOfWork = Substitute.For<IEventUnitOfWork>();

    [Theory]
    [InlineData("runner")]
    [InlineData("organizer")]
    [InlineData("admin")]
    public async Task Detail_ReporterOwnerOrAdmin_ReturnsFullComplaintAndResponse(string actor)
    {
        var (race, complaint) = SetupComplaint();
        complaint.Reply(_organizerId, "Timing result verified.", ComplaintStatus.Accepted);
        _current.UserId.Returns(actor == "runner" ? _runnerId : actor == "organizer" ? _organizerId : Guid.NewGuid());
        _current.IsAdmin.Returns(actor == "admin");

        var result = await DetailHandler().Handle(new(complaint.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(complaint.Id, result.Id);
        Assert.Equal(race.Name, result.EventName);
        Assert.Equal("10K", result.DistanceName);
        Assert.Equal("Timing result verified.", result.ResponseContent);
        Assert.Equal("Accepted", result.Status);
        Assert.Equal("Processed", result.ProcessingStatus);
        Assert.Equal(complaint.RepliedAtUtc, result.RepliedAtUtc);
        Assert.Equal("https://example.com/evidence", Assert.Single(result.EvidenceLinks));
    }

    [Fact]
    public async Task Detail_UnrelatedUser_DoesNotExposeComplaint()
    {
        var (_, complaint) = SetupComplaint();
        _current.UserId.Returns(Guid.NewGuid());

        Assert.Null(await DetailHandler().Handle(new(complaint.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Detail_UnknownComplaint_ReturnsNull()
    {
        _current.UserId.Returns(_runnerId);
        Assert.Null(await DetailHandler().Handle(new(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Detail_Anonymous_DoesNotReadComplaint()
    {
        await Assert.ThrowsAsync<DomainException>(() => DetailHandler().Handle(new(Guid.NewGuid()), CancellationToken.None));
        await _complaints.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MyList_WithoutEventOrStatus_UsesCurrentUserAndPreservesAllStatuses()
    {
        var (_, complaint) = SetupComplaint();
        _current.UserId.Returns(_runnerId);
        complaint.Reply(_organizerId, "Verified response", ComplaintStatus.Rejected);
        _complaints.SearchMyComplaintsAsync(_runnerId, null, null, 2, 10, Arg.Any<CancellationToken>())
            .Returns((new[] { complaint }, 11));

        var result = await new GetMyComplaintsQueryHandler(_complaints, _events, _current)
            .Handle(new(null, null, 2, 10), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Rejected", item.Status);
        Assert.Equal("Verified response", item.ResponseContent);
        Assert.Equal("10K", item.DistanceName);
        Assert.Equal(11, result.TotalCount);
        Assert.Equal(2, result.Page);
        await _complaints.Received(1).SearchMyComplaintsAsync(_runnerId, null, null, 2, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrganizerList_ExplicitForeignEvent_ReturnsEmptyWithoutReadingComplaints()
    {
        var (race, _) = SetupComplaint();
        _current.UserId.Returns(Guid.NewGuid());
        _events.ListByOrganizerAsync(_current.UserId!.Value, Arg.Any<CancellationToken>()).Returns(Array.Empty<RaceEvent>());

        var result = await new ListOrganizerComplaintsQueryHandler(_events, _complaints, _current)
            .Handle(new(race.Id, null, null, null, 1, 10), CancellationToken.None);

        Assert.Empty(result.Items);
        await _complaints.DidNotReceive().SearchOrganizerComplaintsAsync(
            Arg.Any<IEnumerable<Guid>>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<DateTime?>(),
            Arg.Any<DateTime?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    public async Task Reply_EventOwner_UpdatesDecisionAndResponse(string decision)
    {
        var (_, complaint) = SetupComplaint();
        _current.UserId.Returns(_organizerId);

        var result = await new ReplyComplaintCommandHandler(_events, _complaints, _current, _unitOfWork)
            .Handle(new(complaint.Id, "The organizer's decision.", decision), CancellationToken.None);

        Assert.Equal(decision.ToLowerInvariant(), result.Status);
        Assert.Equal("The organizer's decision.", result.ResponseContent);
        Assert.NotNull(result.RepliedAtUtc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_UnrelatedOrganizer_DoesNotUpdateComplaint()
    {
        var (_, complaint) = SetupComplaint();
        _current.UserId.Returns(Guid.NewGuid());

        await Assert.ThrowsAsync<DomainException>(() => new ReplyComplaintCommandHandler(_events, _complaints, _current, _unitOfWork)
            .Handle(new(complaint.Id, "Response", "Accepted"), CancellationToken.None));

        Assert.Equal(ComplaintStatus.Pending, complaint.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_AlreadyProcessed_DoesNotChangeOriginalDecision()
    {
        var (_, complaint) = SetupComplaint();
        _current.UserId.Returns(_organizerId);
        complaint.Reply(_organizerId, "Original response", ComplaintStatus.Accepted);

        await Assert.ThrowsAsync<DomainException>(() => new ReplyComplaintCommandHandler(_events, _complaints, _current, _unitOfWork)
            .Handle(new(complaint.Id, "Changed response", "Rejected"), CancellationToken.None));

        Assert.Equal(ComplaintStatus.Accepted, complaint.Status);
        Assert.Equal("Original response", complaint.ResponseContent);
    }

    [Fact]
    public void Create_RequiresOneToFiveHttpEvidenceLinks()
    {
        var validator = new CreateComplaintCommandValidator();
        CreateComplaintCommand Command(IReadOnlyList<string>? links) => new(Guid.NewGuid(), Guid.NewGuid(), "Title", "Content", links);

        Assert.False(validator.Validate(Command(null)).IsValid);
        Assert.False(validator.Validate(Command([])).IsValid);
        Assert.False(validator.Validate(Command([" "])).IsValid);
        Assert.False(validator.Validate(Command(["javascript:alert(1)"])).IsValid);
        Assert.False(validator.Validate(Command(Enumerable.Repeat("https://example.com/proof", 6).ToArray())).IsValid);
        Assert.True(validator.Validate(Command(["https://example.com/proof"])).IsValid);
        Assert.True(validator.Validate(Command(Enumerable.Repeat("https://example.com/proof", 5).ToArray())).IsValid);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 10)]
    public void Lists_InvalidPagination_Reject(int page, int pageSize)
    {
        Assert.False(new GetMyComplaintsQueryValidator().Validate(new GetMyComplaintsQuery(null, null, page, pageSize)).IsValid);
        Assert.False(new ListOrganizerComplaintsQueryValidator().Validate(new ListOrganizerComplaintsQuery(null, null, null, null, page, pageSize)).IsValid);
    }

    [Fact]
    public void Reply_RequiresResponseAndSupportedDecision()
    {
        var validator = new ReplyComplaintCommandValidator();
        Assert.False(validator.Validate(new ReplyComplaintCommand(Guid.NewGuid(), " ", "Accepted")).IsValid);
        Assert.False(validator.Validate(new ReplyComplaintCommand(Guid.NewGuid(), "Response", "Pending")).IsValid);
        Assert.False(validator.Validate(new ReplyComplaintCommand(Guid.NewGuid(), "Response", null!)).IsValid);
    }

    private GetComplaintDetailQueryHandler DetailHandler() => new(_complaints, _events, _current);

    private (RaceEvent Race, EventComplaint Complaint) SetupComplaint()
    {
        var race = RaceEvent.CreateDraft(_organizerId, "Complaint Run", "Hanoi", new(2026, 10, 28),
            new GeoPoint(21, 105), OrganizerInfo.Create("Organizer", "org@example.com", "0900000000"), 100);
        var distance = EventDistance.Create(race.Id, "10K", 10, 100, 0);
        race.ReplaceDistances([distance]);
        var complaint = EventComplaint.Create(race.Id, distance.Id, _runnerId, "Timing correction", "Incorrect finish time", ["https://example.com/evidence"]);
        _complaints.GetByIdAsync(complaint.Id, Arg.Any<CancellationToken>()).Returns(complaint);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        return (race, complaint);
    }
}
