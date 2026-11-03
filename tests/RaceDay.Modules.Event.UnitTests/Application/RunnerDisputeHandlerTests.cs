using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Disputes;
using RaceDay.Modules.Event.Domain.Disputes;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class RunnerDisputeHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly IRecentJoinedEventQuery _recentEvents = Substitute.For<IRecentJoinedEventQuery>();
    private readonly IEventDisputeRepository _disputes = Substitute.For<IEventDisputeRepository>();
    private readonly IEventUnitOfWork _unitOfWork = Substitute.For<IEventUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _userId = Guid.NewGuid();

    public RunnerDisputeHandlerTests() => _currentUser.UserId.Returns(_userId);

    [Fact]
    public async Task Create_EligibleRunner_PersistsDisputeWithAuthenticatedIdentity()
    {
        var race = new RecentJoinedEventDto(Guid.NewGuid(), "Recent Run", new(2026, 10, 25), null, "Hanoi");
        _recentEvents.GetAsync(_userId, race.Id, new(2026, 10, 26), Arg.Any<CancellationToken>()).Returns(race);

        var result = await CreateHandler().Handle(
            new(race.Id, " Timing issue ", " My BIB time is incorrect. "), CancellationToken.None);

        Assert.Equal("Timing issue", result.Title);
        Assert.Equal("My BIB time is incorrect.", result.Description);
        Assert.Equal("open", result.Status);
        Assert.Equal(race.Name, result.EventName);
        Assert.Equal(Now.UtcDateTime, result.CreatedAtUtc);
        await _disputes.Received(1).AddAsync(Arg.Is<EventDispute>(d =>
            d.RunnerUserId == _userId && d.EventId == race.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_IneligibleEvent_DoesNotSave()
    {
        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new(Guid.NewGuid(), "Timing issue", "Incorrect result"), CancellationToken.None));

        await _disputes.DidNotReceive().AddAsync(Arg.Any<EventDispute>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_AnonymousRunner_DoesNotReadOrSave()
    {
        _currentUser.UserId.Returns((Guid?)null);
        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().Handle(
            new(Guid.NewGuid(), "Timing issue", "Incorrect result"), CancellationToken.None));
        await _recentEvents.DidNotReceive().GetAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMine_UsesCurrentUserAndRequestedPage()
    {
        var expected = new RunnerPageDto<EventDisputeDto>([], 0, 2, 10);
        _disputes.ListByRunnerAsync(_userId, 2, 10, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new ListMyEventDisputesQueryHandler(_disputes, _currentUser)
            .Handle(new(2, 10), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ListRecent_UsesServerDateAndCurrentUser()
    {
        var expected = new RunnerPageDto<RecentJoinedEventDto>([], 0, 1, 20);
        _recentEvents.ListAsync(_userId, new(2026, 10, 26), 1, 20, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new ListRecentJoinedEventsQueryHandler(_recentEvents, _currentUser, new FixedTimeProvider())
            .Handle(new(), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Lists_AnonymousRunner_RejectBothQueries()
    {
        _currentUser.UserId.Returns((Guid?)null);
        await Assert.ThrowsAsync<DomainException>(() => new ListMyEventDisputesQueryHandler(_disputes, _currentUser)
            .Handle(new(), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => new ListRecentJoinedEventsQueryHandler(
            _recentEvents, _currentUser, new FixedTimeProvider()).Handle(new(), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void ListValidators_InvalidPagination_Reject(int page, int pageSize)
    {
        Assert.False(new ListMyEventDisputesQueryValidator().Validate(new ListMyEventDisputesQuery(page, pageSize)).IsValid);
        Assert.False(new ListRecentJoinedEventsQueryValidator().Validate(new ListRecentJoinedEventsQuery(page, pageSize)).IsValid);
    }

    [Fact]
    public void CreateValidator_EmptyOrOversizedFields_Reject()
    {
        var validator = new CreateEventDisputeCommandValidator();
        Assert.False(validator.Validate(new CreateEventDisputeCommand(Guid.Empty, "Title", "Description")).IsValid);
        Assert.False(validator.Validate(new CreateEventDisputeCommand(Guid.NewGuid(), " ", "Description")).IsValid);
        Assert.False(validator.Validate(new CreateEventDisputeCommand(Guid.NewGuid(), "Title", " ")).IsValid);
        Assert.False(validator.Validate(new CreateEventDisputeCommand(Guid.NewGuid(), new('x', 201), "Description")).IsValid);
        Assert.False(validator.Validate(new CreateEventDisputeCommand(Guid.NewGuid(), "Title", new('x', 4001))).IsValid);
    }

    private CreateEventDisputeCommandHandler CreateHandler() => new(
        _recentEvents, _disputes, _unitOfWork, _currentUser, new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
