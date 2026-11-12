using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class EventRegistrationTests
{
    [Fact]
    public void Create_Requires_Liability()
    {
        Assert.Throws<DomainException>(() => EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "A", "a@b.com", "0901111222", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, liabilityAccepted: false));
    }

    [Fact]
    public void Payment_Then_Approve_Reject_Flow()
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Nguyen Van A", "a@raceday.vn", "0901111222",
            new DateOnly(1995, 1, 1), "Male", "001234567890",
            170, 65, "O+", "Emergency", "0909999888",
            "Áo thun", "M", "Đỏ", """["none"]""", "cert.pdf", "/cert.pdf", true);

        Assert.Equal(RegistrationStatus.PendingPayment, reg.Status);
        reg.MarkPaidPendingApproval();
        Assert.Equal(RegistrationStatus.PendingApproval, reg.Status);

        reg.Confirm("A0042");
        Assert.Equal(RegistrationStatus.Confirmed, reg.Status);
        Assert.Equal("A0042", reg.BibNumber);
    }

    [Fact]
    public void Reject_From_PendingApproval()
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "B", "b@raceday.vn", "0902222333", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);
        reg.MarkPaidPendingApproval();
        reg.Reject("Thiếu giấy khám");
        Assert.Equal(RegistrationStatus.Rejected, reg.Status);
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("""["none"]""", false)]
    [InlineData("""["None"]""", false)]
    [InlineData("""["none","cardiovascular"]""", true)]
    [InlineData("""["injury"]""", true)]
    public void HasSpecialMedicalConditions_Matches_None_Policy(string json, bool expected)
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "M", "m@raceday.vn", "0905555666", null, null, null, null, null, null,
            null, null, null, null, null, json, null, null, true);
        Assert.Equal(expected, reg.HasSpecialMedicalConditions());
    }

    [Fact]
    public void Refund_Cancel_Clears_Bib_And_Status()
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "R", "r@raceday.vn", "0906666777", null, null, null, null, null, null,
            null, null, null, null, null, """["none"]""", null, null, true);
        reg.MarkPaidPendingApproval();
        reg.Confirm("50099");
        reg.MarkCancelledAfterRefund();
        Assert.Equal(RegistrationStatus.Cancelled, reg.Status);
        Assert.Null(reg.BibNumber);
    }

    [Fact]
    public void Finish_Sets_Time_Status_And_Is_Idempotent()
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "C", "c@raceday.vn", "0903333444", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);
        reg.Confirm("BIB-9");

        var finishedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var first = reg.Finish(finishedAt, 1234);
        var second = reg.Finish(DateTime.SpecifyKind(finishedAt.AddMinutes(1), DateTimeKind.Utc), 9999);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(RegistrationStatus.Finished, reg.Status);
        Assert.Equal(finishedAt, reg.FinishedAtUtc);
        Assert.Equal(1234, reg.ResultSeconds);
    }

    [Fact]
    public void SlotHold_Reserve_Clear_And_Paid_Flow()
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "H", "h@raceday.vn", "0907777888", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);

        var until = DateTime.UtcNow.AddMinutes(15);
        reg.ReserveSlotHold(until);
        Assert.True(reg.HasSlotHold);
        Assert.True(reg.IsSlotHoldActive(DateTime.UtcNow));
        Assert.False(reg.IsSlotHoldActive(until.AddSeconds(1)));

        reg.MarkPaidPendingApproval();
        Assert.False(reg.HasSlotHold);
        Assert.Equal(RegistrationStatus.PendingApproval, reg.Status);
    }

    [Fact]
    public void CancelUnpaid_Clears_SlotHold()
    {
        var reg = EventRegistration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "X", "x@raceday.vn", "0908888999", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);
        reg.ReserveSlotHold(DateTime.UtcNow.AddMinutes(10));
        reg.CancelUnpaid();
        Assert.Equal(RegistrationStatus.Cancelled, reg.Status);
        Assert.False(reg.HasSlotHold);
    }
}
