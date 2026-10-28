using System.Text.Json;
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
    private static readonly DateTime FinishedAt = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    public RunnerCertificateQueryTests() => _currentUser.UserId.Returns(_userId);

    [Theory]
    [InlineData("User", RegistrationStatus.Finished)]
    [InlineData("Organizer", RegistrationStatus.Finished)]
    [InlineData("Runner", RegistrationStatus.Finished)]
    public async Task Handle_FinishedUserInAnyRole_ReturnsOwnCertificateAndResult(string role, RegistrationStatus status)
    {
        _currentUser.IsInRole(role).Returns(true);
        var (race, distance, registration) = CreateRaceRegistration(status);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(registration.Id, result.RegistrationId);
        Assert.Equal(registration.FullName, result.RunnerName);
        Assert.Equal("001001", result.Bib);
        Assert.Equal(race.Name, result.EventName);
        Assert.Equal($"{distance.Type} · {distance.Name}", result.Distance);
        Assert.Equal(race.EventDate, result.RaceDate);
        Assert.Equal("finished", result.Status);
        Assert.Equal("01:00:00", result.FinishTime);
        Assert.Equal(3600, result.ResultSeconds);
        Assert.Equal(FinishedAt, result.FinishedAtUtc);
        Assert.Equal(FinishedAt.AddHours(-1), result.StartedAtUtc);
        Assert.Equal(10, result.DistanceKm);
        Assert.Equal("06:00", result.Pace);
        await _registrations.Received(1).GetByIdAsync(registration.Id, Arg.Any<CancellationToken>());
        await _registrations.DidNotReceive().GetFinishedByEventAndUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
        await _registrations.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingRegistration_ReturnsClearError()
    {
        var registrationId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(registrationId), CancellationToken.None));

        Assert.Contains("Không tìm thấy đăng ký", error.Message);
        await _registrations.Received(1).GetByIdAsync(registrationId, Arg.Any<CancellationToken>());
        await _events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RegistrationStatus.PendingPayment)]
    [InlineData(RegistrationStatus.PendingApproval)]
    [InlineData(RegistrationStatus.Confirmed)]
    [InlineData(RegistrationStatus.Rejected)]
    [InlineData(RegistrationStatus.Cancelled)]
    public async Task Handle_UnfinishedRegistration_ReturnsNoCertificate(RegistrationStatus status)
    {
        var (_, _, registration) = CreateRaceRegistration(status);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.Null(result);
        await _events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Organizer")]
    [InlineData("Admin")]
    public async Task Handle_AnotherUsersRegistration_RejectsBeforeReadingEvent(string role)
    {
        _currentUser.IsInRole(role).Returns(true);
        var (_, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished, userId: Guid.NewGuid());
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(registration.Id), CancellationToken.None));

        Assert.Contains("Bạn không có quyền", error.Message);
        await _events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingRace_ReturnsClearError()
    {
        var (_, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);

        var error = await Assert.ThrowsAsync<DomainException>(() => Handler()
            .Handle(new(registration.Id), CancellationToken.None));

        Assert.Contains("Không tìm thấy thông tin giải đấu", error.Message);
    }

    [Fact]
    public async Task Handle_FinishedWithoutBib_ReturnsNullBib()
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished, bib: null);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Bib);
        Assert.Equal("finished", result.Status);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 3600L)]
    [InlineData(true, -1L)]
    public async Task Handle_FinishedWithIncompleteOrInvalidResult_ReturnsNoCertificate(bool hasFinishTimestamp, long? resultSeconds)
    {
        var (_, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        // Simulate legacy or incomplete data read from persistence.
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.FinishedAtUtc))!
            .SetValue(registration, hasFinishTimestamp ? FinishedAt : null);
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.ResultSeconds))!
            .SetValue(registration, resultSeconds);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.Null(result);
        await _events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0L, "00:00:00")]
    [InlineData(90061L, "25:01:01")]
    public async Task Handle_ValidElapsedSeconds_PreservesZeroAndHoursAbove24(long resultSeconds, string finishTime)
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished, resultSeconds: resultSeconds);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(finishTime, result.FinishTime);
        Assert.Equal(resultSeconds, result.ResultSeconds);
    }

    [Theory]
    [InlineData(3723L, 10, "06:12")]
    [InlineData(7200L, 21.0975, "05:41")]
    [InlineData(1798L, 5, "06:00")]
    [InlineData(0L, 10, "00:00")]
    [InlineData(90061L, 10, "150:06")]
    public async Task Handle_FinishedRace_CalculatesPacePerKilometre(long resultSeconds, double distanceKm, string pace)
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished,
            resultSeconds: resultSeconds, distanceKm: distanceKm);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(pace, result.Pace);
        Assert.Equal(distanceKm, result.DistanceKm);
    }

    [Fact]
    public async Task Handle_RunnerTimestampsDifferFromStoredResult_UsesFinishMinusStart()
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.StartedAtUtc))!
            .SetValue(registration, FinishedAt.AddMinutes(-10));
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(600, result.ResultSeconds);
        Assert.Equal("00:10:00", result.FinishTime);
        Assert.Equal("01:00", result.Pace);
    }

    [Fact]
    public async Task Handle_NoIndividualStartScan_UsesRecordedRaceResult()
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.StartedAtUtc))!.SetValue(registration, null);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.StartedAtUtc);
        Assert.Equal("01:00:00", result.FinishTime);
        Assert.Equal("06:00", result.Pace);
    }

    [Fact]
    public async Task Handle_FinishBeforeStart_ReturnsNoCertificate()
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.StartedAtUtc))!
            .SetValue(registration, FinishedAt.AddMinutes(1));
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        Assert.Null(await Handler().Handle(new(registration.Id), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Handle_InvalidDistance_ReturnsNoCertificate(double distanceKm)
    {
        var (race, distance, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        typeof(EventDistance).GetProperty(nameof(EventDistance.DistanceKm))!.SetValue(distance, distanceKm);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        Assert.Null(await Handler().Handle(new(registration.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_JsonResponse_ContainsOnlyCertificatePageFields()
    {
        var (race, _, registration) = CreateRaceRegistration(RegistrationStatus.Finished);
        _registrations.GetByIdAsync(registration.Id, Arg.Any<CancellationToken>()).Returns(registration);
        _events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await Handler().Handle(new(registration.Id), CancellationToken.None);
        Assert.NotNull(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var body = json.RootElement;

        Assert.Equal(
            ["bib", "distance", "distanceKm", "eventName", "finishedAtUtc", "finishTime", "pace", "raceDate", "registrationId", "resultSeconds", "runnerName", "startedAtUtc", "status"],
            body.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("2026-10-27", body.GetProperty("raceDate").GetString());
        Assert.Equal("001001", body.GetProperty("bib").GetString());
        Assert.Equal("finished", body.GetProperty("status").GetString());
        Assert.Equal(registration.Id, body.GetProperty("registrationId").GetGuid());
        Assert.Equal("01:00:00", body.GetProperty("finishTime").GetString());
        Assert.Equal(3600, body.GetProperty("resultSeconds").GetInt64());
        Assert.Equal(FinishedAt, body.GetProperty("finishedAtUtc").GetDateTime());
        Assert.Equal(FinishedAt.AddHours(-1), body.GetProperty("startedAtUtc").GetDateTime());
        Assert.Equal(10, body.GetProperty("distanceKm").GetDouble());
        Assert.Equal("06:00", body.GetProperty("pace").GetString());
    }

    [Fact]
    public void Validator_RequiresNonEmptyRegistrationId()
    {
        var validator = new GetRunnerCertificateQueryValidator();
        Assert.False(validator.Validate(new GetRunnerCertificateQuery(Guid.Empty)).IsValid);
        Assert.True(validator.Validate(new GetRunnerCertificateQuery(Guid.NewGuid())).IsValid);
    }

    private GetRunnerCertificateQueryHandler Handler() => new(_registrations, _events, _currentUser);

    private (RaceEvent Race, EventDistance Distance, EventRegistration Registration) CreateRaceRegistration(
        RegistrationStatus status, string? bib = "001001", Guid? userId = null, long resultSeconds = 3600, double distanceKm = 10)
    {
        var race = RaceEvent.CreateDraft(Guid.NewGuid(), "Certificate Run", "Hanoi", new(2026, 10, 27),
            new GeoPoint(21, 105), OrganizerInfo.Create("Organizer", "org@test.vn", "0900000000"), 100);
        var distance = EventDistance.Create(race.Id, "10K", distanceKm, 100, 0);
        race.ReplaceDistances([distance]);
        var registration = EventRegistration.Create(race.Id, distance.Id, userId ?? _userId,
            "Nguyen Van A", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        switch (status)
        {
            case RegistrationStatus.PendingApproval:
                registration.MarkPaidPendingApproval();
                break;
            case RegistrationStatus.Confirmed:
                registration.Confirm(bib);
                break;
            case RegistrationStatus.Finished:
                registration.Confirm(bib);
                registration.Start(FinishedAt.AddSeconds(-resultSeconds));
                registration.Finish(FinishedAt, resultSeconds);
                break;
            case RegistrationStatus.Rejected:
                registration.MarkPaidPendingApproval();
                registration.Reject("Missing documents");
                break;
            case RegistrationStatus.Cancelled:
                registration.CancelUnpaid();
                break;
        }
        return (race, distance, registration);
    }
}
