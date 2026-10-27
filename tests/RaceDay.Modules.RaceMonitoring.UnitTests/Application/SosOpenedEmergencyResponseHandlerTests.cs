using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Device.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class SosOpenedEmergencyResponseHandlerTests
{
    [Fact]
    public async Task Opens_One_Medical_Medium_Response_And_Leaves_Source_Sos_Untouched()
    {
        var sosAlertId = Guid.NewGuid();
        var (responses, unitOfWork) = Pipeline();
        responses.ExistsBySourceSosAlertIdAsync(sosAlertId, Arg.Any<CancellationToken>()).Returns(false);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        EmergencyResponse? added = null;
        responses.When(x => x.Add(Arg.Any<EmergencyResponse>()))
            .Do(call => added = call.Arg<EmergencyResponse>());

        await Handler(responses, unitOfWork).Handle(
            new IntegrationEventOccurred(Opened(sosAlertId, Guid.NewGuid())),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(sosAlertId, added.SourceSosAlertId);
        Assert.Equal(EmergencyIncidentType.Medical, added.IncidentType);
        Assert.Equal(EmergencyPriority.Medium, added.Priority);
        Assert.Equal(EmergencyResponseStatus.Open, added.Status);
        Assert.Null(added.AssignedAtUtc);
        Assert.Null(added.ResolvedAtUtc);
        Assert.Null(added.ResolutionNotes);
        Assert.Null(added.CancelledAtUtc);
        Assert.Null(added.CancellationReason);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opens_Response_When_Sos_Has_No_Event()
    {
        var sosAlertId = Guid.NewGuid();
        var (responses, unitOfWork) = Pipeline();
        responses.ExistsBySourceSosAlertIdAsync(sosAlertId, Arg.Any<CancellationToken>()).Returns(false);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        await Handler(responses, unitOfWork).Handle(
            new IntegrationEventOccurred(Opened(sosAlertId, null)),
            CancellationToken.None);

        responses.Received(1).Add(Arg.Is<EmergencyResponse>(row =>
            row.SourceSosAlertId == sosAlertId
            && row.Status == EmergencyResponseStatus.Open));
    }

    [Fact]
    public async Task Skips_When_Response_Already_Exists()
    {
        var sosAlertId = Guid.NewGuid();
        var (responses, unitOfWork) = Pipeline();
        responses.ExistsBySourceSosAlertIdAsync(sosAlertId, Arg.Any<CancellationToken>()).Returns(true);

        await Handler(responses, unitOfWork).Handle(
            new IntegrationEventOccurred(Opened(sosAlertId, Guid.NewGuid())),
            CancellationToken.None);

        responses.DidNotReceive().Add(Arg.Any<EmergencyResponse>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Treats_Unique_Race_As_Already_Opened()
    {
        var sosAlertId = Guid.NewGuid();
        var (responses, unitOfWork) = Pipeline();
        responses.ExistsBySourceSosAlertIdAsync(sosAlertId, Arg.Any<CancellationToken>()).Returns(false);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(0);

        await Handler(responses, unitOfWork).Handle(
            new IntegrationEventOccurred(Opened(sosAlertId, Guid.NewGuid())),
            CancellationToken.None);

        responses.Received(1).Add(Arg.Any<EmergencyResponse>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_Empty_Sos_Id()
    {
        var (responses, unitOfWork) = Pipeline();

        await Handler(responses, unitOfWork).Handle(
            new IntegrationEventOccurred(Opened(Guid.Empty, Guid.NewGuid())),
            CancellationToken.None);

        await responses.DidNotReceive().ExistsBySourceSosAlertIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        responses.DidNotReceive().Add(Arg.Any<EmergencyResponse>());
    }

    [Fact]
    public async Task Ignores_Other_Integration_Events()
    {
        var (responses, unitOfWork) = Pipeline();

        await Handler(responses, unitOfWork).Handle(
            new IntegrationEventOccurred(new WearDeviceLinkedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), "watch-1")),
            CancellationToken.None);

        await responses.DidNotReceive().ExistsBySourceSosAlertIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        responses.DidNotReceive().Add(Arg.Any<EmergencyResponse>());
    }

    private static SosOpenedEmergencyResponseHandler Handler(
        IEmergencyResponseRepository responses,
        IRaceMonitoringUnitOfWork unitOfWork) =>
        new(responses, unitOfWork, NullLogger<SosOpenedEmergencyResponseHandler>.Instance);

    private static (IEmergencyResponseRepository Responses, IRaceMonitoringUnitOfWork UnitOfWork) Pipeline() =>
        (Substitute.For<IEmergencyResponseRepository>(), Substitute.For<IRaceMonitoringUnitOfWork>());

    private static SosOpenedIntegrationEvent Opened(Guid sosAlertId, Guid? eventId) =>
        new(sosAlertId, Guid.NewGuid(), eventId, 10.77, 106.70);
}
