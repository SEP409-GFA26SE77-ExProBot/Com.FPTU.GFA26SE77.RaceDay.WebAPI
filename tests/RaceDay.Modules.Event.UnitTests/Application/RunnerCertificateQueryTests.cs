using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Certificates;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class RunnerCertificateQueryTests
{
    private readonly IEventRegistrationRepository _registrations = Substitute.For<IEventRegistrationRepository>();
    private readonly IRaceEventRepository _events = Substitute.For<IRaceEventRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _userId = Guid.NewGuid();
    private static readonly DateTime FinishedAt = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

    public RunnerCertificateQueryTests() => _currentUser.UserId.Returns(_userId);

    [Theory]
    [InlineData("User")]
    [InlineData("Organizer")]
    [InlineData("Runner")]
    public async Task Handle_FinishedUserInAnyRole_ReturnsOwnCertificate(string role)
    {
        _currentUser.IsInRole(role).Returns(true);
        var (race, distance, registration) = CreateRaceRegistration(finished: true);
        _registrations.GetFinishedByEventAndUserAsync(race.Id, _userId, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(race.Id), CancellationToken.None);

        Assert.Equal(registration.FullName, result.RunnerName);
        Assert.Equal(registration.BibNumber, result.BibNumber);
        Assert.Equal(race.Name, result.RaceName);
        Assert.Equal(distance.Name, result.DistanceName);
        Assert.Equal("01:00:00", result.FinishTime);
        Assert.Equal("06:00", result.Pace);
        Assert.Equal(FinishedAt, result.CompletedAtUtc);
        await _registrations.Received(1).GetFinishedByEventAndUserAsync(race.Id, _userId, Arg.Any<CancellationToken>());
        await _registrations.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Handle_MissingTokenUser_RejectsBeforeReadingRegistrations(string? identity)
    {
        _currentUser.UserId.Returns(identity is null ? (Guid?)null : Guid.Parse(identity));

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(Guid.NewGuid()), CancellationToken.None));

        Assert.Contains("Cần đăng nhập", error.Message);
        await _registrations.DidNotReceive().GetFinishedByEventAndUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoFinishedRegistrationForTokenUser_ReturnsClearError()
    {
        var eventId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(eventId), CancellationToken.None));

        Assert.Contains("Bạn chưa hoàn thành giải chạy này", error.Message);
        await _registrations.Received(1).GetFinishedByEventAndUserAsync(eventId, _userId, Arg.Any<CancellationToken>());
        await _events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnfinishedRegistration_RejectsCertificate()
    {
        var (race, _, registration) = CreateRaceRegistration(finished: false);
        _registrations.GetFinishedByEventAndUserAsync(race.Id, _userId, Arg.Any<CancellationToken>()).Returns(registration);

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(race.Id), CancellationToken.None));

        Assert.Contains("Bạn chưa hoàn thành giải chạy này", error.Message);
        await _events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingRace_ReturnsClearError()
    {
        var (race, _, registration) = CreateRaceRegistration(finished: true);
        _registrations.GetFinishedByEventAndUserAsync(race.Id, _userId, Arg.Any<CancellationToken>()).Returns(registration);

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(race.Id), CancellationToken.None));

        Assert.Contains("Không tìm thấy thông tin giải đấu", error.Message);
    }

    [Fact]
    public void Validator_RequiresNonEmptyEventId()
    {
        var validator = new GetRunnerCertificateQueryValidator();
        Assert.False(validator.Validate(new GetRunnerCertificateQuery(Guid.Empty)).IsValid);
        Assert.True(validator.Validate(new GetRunnerCertificateQuery(Guid.NewGuid())).IsValid);
    }

    private GetRunnerCertificateQueryHandler Handler() => new(_registrations, _events, _currentUser);

    private (RaceEvent Race, EventDistance Distance, EventRegistration Registration) CreateRaceRegistration(bool finished)
    {
        var race = RaceEvent.CreateDraft(Guid.NewGuid(), "Certificate Run", "Hanoi", new(2026, 10, 27),
            new GeoPoint(21, 105), OrganizerInfo.Create("Organizer", "org@test.vn", "0900000000"), 100);
        var distance = EventDistance.Create(race.Id, "10K", 10, 100, 0);
        race.ReplaceDistances([distance]);
        var registration = EventRegistration.Create(race.Id, distance.Id, _userId,
            "Nguyen Van A", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        registration.Confirm("1001");
        if (finished)
        {
            registration.Start(FinishedAt.AddHours(-1));
            registration.Finish(FinishedAt, 3600);
        }
        return (race, distance, registration);
    }
}
