using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class RunnerOperationsTests
{
    internal static EventRegistration Registration(Guid? eventId = null, Guid? distanceId = null)
    {
        var reg = EventRegistration.Create(eventId ?? Guid.NewGuid(), distanceId ?? Guid.NewGuid(), Guid.NewGuid(),
            "Runner", "runner@example.com", "0901111222", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);
        reg.MarkPaidPendingApproval();
        reg.Confirm("TEST-1001");
        return reg;
    }

    [Theory]
    [InlineData(RunnerRaceStatus.Dns)]
    [InlineData(RunnerRaceStatus.Dnf)]
    [InlineData(RunnerRaceStatus.Dq)]
    public void TerminalStatus_BlocksScans_ButPreservesRegistration(RunnerRaceStatus status)
    {
        var reg = Registration();
        reg.SetRaceStatus(status, false);
        Assert.Throws<DomainException>(() => reg.Start(DateTime.UtcNow));
        Assert.Throws<DomainException>(() => reg.Finish(DateTime.UtcNow, 100));
        Assert.Throws<DomainException>(() => reg.CheckIn(Guid.NewGuid()));
        Assert.Equal(RegistrationStatus.Confirmed, reg.Status);
    }

    [Fact]
    public void CheckIn_DoesNotPreventDns_ButStartDoes()
    {
        var reg = Registration();
        reg.CheckIn(Guid.NewGuid());
        reg.SetRaceStatus(RunnerRaceStatus.Dns, false);
        reg.RestoreRaceStatus(true);
        reg.Start(DateTime.UtcNow);
        Assert.Throws<DomainException>(() => reg.SetRaceStatus(RunnerRaceStatus.Dns, true));
    }

    [Fact]
    public void DnfWithoutStartScan_DoesNotInventStartTime()
    {
        var reg = Registration();
        reg.SetRaceStatus(RunnerRaceStatus.Dnf, false);
        Assert.Null(reg.StartedAtUtc);
        Assert.Throws<ForbiddenException>(() => reg.RestoreRaceStatus(false));
        reg.RestoreRaceStatus(true);
        Assert.Equal(RunnerRaceStatus.NotStarted, reg.RaceStatus);
    }

    [Fact]
    public void DqAfterFinish_PreservesTiming_AndRestoreRecoversFinishedStatus()
    {
        var reg = Registration();
        var start = DateTime.UtcNow.AddHours(-1);
        var finish = start.AddMinutes(30);
        reg.Start(start);
        reg.Finish(finish, 1800);
        Assert.Equal("finished", RegistrationDtoMapper.ToDto(reg).Status);
        reg.SetRaceStatus(RunnerRaceStatus.Dq, false);
        Assert.Equal("dq", RegistrationDtoMapper.ToDto(reg).RaceStatus);
        Assert.NotEqual("finished", RegistrationDtoMapper.ToDto(reg).Status);
        Assert.Equal(start, reg.StartedAtUtc);
        Assert.Equal(finish, reg.FinishedAtUtc);
        Assert.Equal(1800L, reg.ResultSeconds);
        Assert.Throws<DomainException>(() => reg.SetRaceStatus(RunnerRaceStatus.Dnf, true));
        reg.RestoreRaceStatus(true);
        Assert.Equal("finished", RegistrationDtoMapper.ToDto(reg).Status);
    }

    [Fact]
    public void VolunteerCannotOverwriteTerminalDecision_CoordinatorCan()
    {
        var reg = Registration();
        reg.SetRaceStatus(RunnerRaceStatus.Dnf, false);
        Assert.Throws<ForbiddenException>(() => reg.SetRaceStatus(RunnerRaceStatus.Dq, false));
        reg.SetRaceStatus(RunnerRaceStatus.Dq, true);
        Assert.Equal(RunnerRaceStatus.Dq, reg.RaceStatus);
    }

    [Fact]
    public void BibLossAndReissue_PreserveIdentityAndTiming_AndSupportRepeatedLoss()
    {
        var reg = Registration();
        reg.CheckIn(Guid.NewGuid());
        reg.Start(DateTime.UtcNow.AddMinutes(-20));
        reg.Finish(DateTime.UtcNow, 1200);
        reg.SetRaceStatus(RunnerRaceStatus.Dq, false);
        var before = (reg.Id, reg.BibNumber, reg.CheckedInAtUtc, reg.StartedAtUtc, reg.FinishedAtUtc, reg.ResultSeconds, reg.RaceStatus);
        Assert.Throws<DomainException>(() => reg.SetBibStatus(PhysicalBibStatus.Reissued));
        reg.SetBibStatus(PhysicalBibStatus.Lost);
        reg.SetBibStatus(PhysicalBibStatus.Reissued);
        Assert.Equal(before, (reg.Id, reg.BibNumber, reg.CheckedInAtUtc, reg.StartedAtUtc, reg.FinishedAtUtc, reg.ResultSeconds, reg.RaceStatus));
        reg.SetBibStatus(PhysicalBibStatus.Lost);
        Assert.Equal(PhysicalBibStatus.Lost, reg.BibStatus);
        Assert.Throws<ConflictException>(() => reg.SetBibStatus(PhysicalBibStatus.Lost));
    }

    [Fact]
    public void CancelledRegistrationCannotReceiveOperations()
    {
        var reg = Registration();
        reg.MarkCancelledAfterRefund();
        Assert.Throws<DomainException>(() => reg.SetRaceStatus(RunnerRaceStatus.Dq, false));
        Assert.Throws<DomainException>(() => reg.SetBibStatus(PhysicalBibStatus.Lost));
    }

    [Fact]
    public void VersionChangesWithScans_ButNotRepeatedScans()
    {
        var reg = Registration();
        var version = reg.Version;
        reg.CheckIn(Guid.NewGuid());
        Assert.Equal(version + 1, reg.Version);
        Assert.False(reg.CheckIn(Guid.NewGuid()));
        Assert.Equal(version + 1, reg.Version);
        reg.Start(DateTime.UtcNow.AddMinutes(-5));
        reg.Finish(DateTime.UtcNow, 300);
        version = reg.Version;
        Assert.False(reg.Finish(DateTime.UtcNow, 999));
        Assert.False(reg.Start(DateTime.UtcNow));
        Assert.Equal(version, reg.Version);
        Assert.Throws<DomainException>(() => reg.MarkCancelledAfterRefund());
    }

    [Fact]
    public void FinishWithoutStart_CannotLaterAddStartAndInvalidateResult()
    {
        var reg = Registration();
        reg.Finish(DateTime.UtcNow, 300);
        Assert.Throws<DomainException>(() => reg.Start(DateTime.UtcNow));
        Assert.Equal(300L, reg.ResultSeconds);
    }
}
