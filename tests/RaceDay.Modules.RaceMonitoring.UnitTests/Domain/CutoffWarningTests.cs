using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class CutoffWarningTests
{
    private static readonly DateTime Cutoff = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Raise_AfterCutoff_IsActiveWithSnapshot()
    {
        var last = new CutoffWarningLastPassage(Guid.NewGuid(), "Start", Cutoff.AddHours(-1));

        var warning = CutoffWarning.Raise(Target(), Cutoff, last, Cutoff.AddMinutes(3));

        Assert.Equal(CutoffWarningStatus.Active, warning.Status);
        Assert.True(warning.IsActive);
        Assert.Equal("1024", warning.BibNumber);
        Assert.Equal("Start", warning.LastCheckpointName);
        Assert.Equal(Cutoff.AddHours(-1), warning.LastPassedAtUtc);
    }

    [Fact]
    public void Raise_BeforeCutoff_Throws()
    {
        Assert.Throws<DomainException>(() => CutoffWarning.Raise(Target(), Cutoff, null, Cutoff.AddSeconds(-1)));
    }

    [Fact]
    public void Raise_LastPassageAtSameCheckpoint_Throws()
    {
        var target = Target();
        var last = new CutoffWarningLastPassage(target.CheckpointId, "CP1", Cutoff);

        Assert.Throws<DomainException>(() => CutoffWarning.Raise(target, Cutoff, last, Cutoff.AddMinutes(3)));
    }

    [Fact]
    public void Dismiss_Active_KeepsRecordAndStoresWhoAndWhy()
    {
        var warning = CutoffWarning.Raise(Target(), Cutoff, null, Cutoff.AddMinutes(3));
        var userId = Guid.NewGuid();

        warning.Dismiss(userId, "  Runner called in, safe at aid station  ", Cutoff.AddMinutes(10));

        Assert.Equal(CutoffWarningStatus.Dismissed, warning.Status);
        Assert.Equal(userId, warning.DismissedByUserId);
        Assert.Equal("Runner called in, safe at aid station", warning.DismissalNote);
        Assert.Equal(Cutoff.AddMinutes(10), warning.DismissedAtUtc);
    }

    [Fact]
    public void Dismiss_Twice_Throws()
    {
        var warning = CutoffWarning.Raise(Target(), Cutoff, null, Cutoff.AddMinutes(3));
        warning.Dismiss(Guid.NewGuid(), null, Cutoff.AddMinutes(4));

        Assert.Throws<DomainException>(() => warning.Dismiss(Guid.NewGuid(), null, Cutoff.AddMinutes(5)));
    }

    [Fact]
    public void Resolve_OnlyChangesActiveWarnings()
    {
        var warning = CutoffWarning.Raise(Target(), Cutoff, null, Cutoff.AddMinutes(3));
        var scanId = Guid.NewGuid();

        Assert.True(warning.Resolve(CutoffWarningResolution.PassedCheckpoint, Cutoff.AddMinutes(6), scanId));
        Assert.False(warning.Resolve(CutoffWarningResolution.RunnerStatusChanged, Cutoff.AddMinutes(7)));

        Assert.Equal(CutoffWarningStatus.Resolved, warning.Status);
        Assert.Equal(CutoffWarningResolution.PassedCheckpoint, warning.Resolution);
        Assert.Equal(scanId, warning.ResolvedByScanId);
    }

    [Fact]
    public void Resolve_AfterDismiss_DoesNothing()
    {
        var warning = CutoffWarning.Raise(Target(), Cutoff, null, Cutoff.AddMinutes(3));
        warning.Dismiss(Guid.NewGuid(), null, Cutoff.AddMinutes(4));

        Assert.False(warning.Resolve(CutoffWarningResolution.PassedCheckpoint, Cutoff.AddMinutes(5)));
        Assert.Equal(CutoffWarningStatus.Dismissed, warning.Status);
    }

    internal static CutoffWarningTarget Target(Guid? registrationId = null, Guid? checkpointId = null)
        => new(
            Guid.NewGuid(),
            "Hanoi Marathon",
            Guid.NewGuid(),
            "21KM",
            registrationId ?? Guid.NewGuid(),
            "1024",
            "Nguyen Van An",
            checkpointId ?? Guid.NewGuid(),
            "CP1",
            5.2,
            1);
}
