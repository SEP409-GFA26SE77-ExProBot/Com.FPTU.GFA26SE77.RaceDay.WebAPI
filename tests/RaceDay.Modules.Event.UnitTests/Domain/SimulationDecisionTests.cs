using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class SimulationDecisionTests
{
    [Theory]
    [InlineData(RaceStatus.DNF)]
    [InlineData(RaceStatus.DNS)]
    [InlineData(RaceStatus.DQ)]
    public void Simulation_terminal_status_preserves_registration_and_records_source(RaceStatus status)
    {
        var registration = RaceStatusTests.Registration();
        var actor = Guid.NewGuid();
        var run = Guid.NewGuid();
        var at = DateTime.UtcNow;
        var audit = registration.ApplySimulationDecision(status, "  Decision  ", actor, at, run, "cp-1");
        Assert.Equal(status, registration.RaceStatus);
        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        Assert.Null(registration.ResultSeconds);
        Assert.Null(registration.FinishedAtUtc);
        Assert.Equal(run, audit.SimulationRunId);
        Assert.Equal("cp-1", audit.CheckpointExternalId);
        Assert.Equal(actor, audit.ChangedByUserId);
        Assert.Equal(at, audit.ChangedAtUtc);
        Assert.Equal("Decision", audit.Reason);
        Assert.Throws<DomainException>(() => registration.Finish(at, 100));
        Assert.Throws<DomainException>(() => registration.ApplySimulationDecision(status, "Again", actor, at, run, "cp-1"));
    }

    [Fact]
    public void Simulation_cannot_replace_a_real_started_or_finished_result()
    {
        var registration = RaceStatusTests.Registration();
        registration.Start(DateTime.UtcNow);
        Assert.Throws<DomainException>(() => registration.ApplySimulationDecision(RaceStatus.DNF,
            "Stopped", Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "cp"));
        registration.Finish(DateTime.UtcNow.AddMinutes(5), 300);
        Assert.Throws<DomainException>(() => registration.ApplySimulationDecision(RaceStatus.DQ,
            "Violation", Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "cp"));
    }
}
