using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class RaceStatusTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

    internal static EventRegistration Registration()
    {
        var reg = EventRegistration.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Runner", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        reg.Confirm("1001");
        return reg;
    }

    [Theory]
    [InlineData(RaceStatus.DNS)]
    [InlineData(RaceStatus.DNF)]
    [InlineData(RaceStatus.DQ)]
    public void Decision_RecordsAudit_AndBlocksStartFinishAndReversal(RaceStatus target)
    {
        var reg = Registration();
        if (target == RaceStatus.DNF) reg.Start(Now.AddHours(-1));
        var actor = Guid.NewGuid();
        var previous = reg.RaceStatus;
        var change = reg.ChangeRaceStatus(target, "  Runner stopped  ", actor, Now, Now);

        Assert.Equal(target, reg.RaceStatus);
        Assert.Equal(RegistrationStatus.Confirmed, reg.Status);
        Assert.Equal(previous, change.PreviousStatus);
        Assert.Equal(target, change.Status);
        Assert.Equal(reg.Id, change.RegistrationId);
        Assert.Equal(actor, change.ChangedByUserId);
        Assert.Equal(Now, change.ChangedAtUtc);
        Assert.Equal("Runner stopped", change.Reason);
        Assert.Throws<DomainException>(() => reg.Start(Now));
        Assert.Throws<DomainException>(() => reg.Finish(Now, 3600));
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DQ, "Other", actor, Now, Now));
    }

    [Fact]
    public void DqAfterFinish_PreservesRecordedResult()
    {
        var reg = Registration();
        reg.Start(Now.AddHours(-1));
        reg.Finish(Now, 3600);
        var change = reg.ChangeRaceStatus(RaceStatus.DQ, "Rule violation", Guid.NewGuid(), Now, null);

        Assert.Equal(RaceStatus.Finished, change.PreviousStatus);
        Assert.Equal(RegistrationStatus.Finished, reg.Status);
        Assert.Equal(Now, reg.FinishedAtUtc);
        Assert.Equal(3600, reg.ResultSeconds);
        Assert.Equal(Now.AddHours(-1), reg.StartedAtUtc);
        Assert.Throws<DomainException>(() => reg.Finish(Now, 3600));
    }

    [Fact]
    public void Dns_RequiresScheduleReached_AndNoStartOrFinish()
    {
        var reg = Registration();
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DNS, "Absent", Guid.NewGuid(), Now, null));
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DNS, "Absent", Guid.NewGuid(), Now, Now.AddSeconds(1)));
        reg.Start(Now);
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DNS, "Absent", Guid.NewGuid(), Now, Now));
        reg.Finish(Now.AddMinutes(1), 60);
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DNS, "Absent", Guid.NewGuid(), Now, Now));
    }

    [Fact]
    public void Dnf_RequiresRunning_AndNotFinished()
    {
        var reg = Registration();
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DNF, "Stopped", Guid.NewGuid(), Now, null));
        reg.Finish(Now, 3600);
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DNF, "Stopped", Guid.NewGuid(), Now, null));
    }

    [Theory]
    [InlineData(RegistrationStatus.PendingPayment)]
    [InlineData(RegistrationStatus.PendingApproval)]
    [InlineData(RegistrationStatus.Rejected)]
    [InlineData(RegistrationStatus.Cancelled)]
    public void InvalidRegistrationStatus_CannotReceiveDecision(RegistrationStatus status)
    {
        var reg = Registration();
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.Status))!.SetValue(reg, status);
        Assert.Throws<DomainException>(() => reg.ChangeRaceStatus(RaceStatus.DQ, "Violation", Guid.NewGuid(), Now, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Reason_IsRequired(string? reason)
    {
        Assert.Throws<DomainException>(() => Registration().ChangeRaceStatus(RaceStatus.DQ, reason!, Guid.NewGuid(), Now, null));
    }

    [Fact]
    public void StartFinish_SynchronizeRaceStatus_AndPreserveFinishFallback()
    {
        var reg = Registration();
        Assert.Equal(RaceStatus.NotStarted, reg.RaceStatus);
        reg.Start(Now);
        Assert.Equal(RaceStatus.Running, reg.RaceStatus);
        reg.Finish(Now.AddHours(1), 3600);
        Assert.Equal(RaceStatus.Finished, reg.RaceStatus);
        Assert.False(reg.Start(Now));

        var withoutStart = Registration();
        withoutStart.Finish(Now, 3600);
        Assert.Equal(RaceStatus.Finished, withoutStart.RaceStatus);
        Assert.Throws<DomainException>(() => withoutStart.Start(Now));
    }
}
