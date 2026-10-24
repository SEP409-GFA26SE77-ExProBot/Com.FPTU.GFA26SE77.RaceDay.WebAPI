namespace RaceDay.Modules.Finance.Application.Wallet;

public sealed record BankAccountDto(
    string BankName,
    string AccountNumber,
    string AccountHolderName,
    string? Branch = null,
    string? TransferNote = null,
    string? Bin = null);

public sealed record EventLedgerDto(
    Guid EventId,
    string EventName,
    string EventStatus,
    int TicketsSold,
    decimal GrossRevenue,
    decimal PlatformFeePercent,
    decimal PlatformFeeAmount,
    decimal RefundedTotal,
    decimal NetToOrganizer,
    decimal EscrowHeld,
    decimal WithdrawnTotal,
    decimal PayoutPending,
    decimal AvailableToWithdraw,
    string WithdrawPolicyNote,
    string Currency);

public sealed record OrganizerWalletDto(
    decimal TotalRevenue,
    decimal AvailableBalance,
    decimal PendingEscrow,
    decimal WithdrawnTotal,
    string Currency,
    BankAccountDto? BankAccount,
    IReadOnlyList<EventLedgerDto> Events);

public sealed record PayoutRequestDto(
    Guid Id,
    Guid EventId,
    string EventName,
    decimal Amount,
    string? Note,
    string Status,
    string? RejectReason,
    BankAccountDto? BankAccountSnapshot,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? TransferredAt,
    string? ReceiptUrl);

public sealed record CreatePayoutRequestDto(decimal Amount, Guid EventId, string? Note);

public sealed record RefundRequestDto(
    Guid Id,
    Guid PaymentId,
    Guid RegistrationId,
    Guid? EventId,
    decimal Amount,
    string Reason,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? RejectReason);

public sealed record CreateRefundRequestDto(Guid PaymentId, string Reason, Guid? EventId = null, decimal? Amount = null);

public sealed record AdminPayoutRequestDto(
    Guid Id,
    Guid OrganizerId,
    string OrganizerName,
    Guid? EventId,
    string? EventName,
    decimal Amount,
    string? Note,
    string Status,
    string? RejectReason,
    BankAccountDto BankAccount,
    DateTime CreatedAt,
    string? ReceiptUrl);

public sealed record PlatformFinanceOverviewDto(
    decimal TotalEscrowHeld,
    decimal PlatformFeeCollected,
    decimal PendingPayoutAmount,
    decimal TransferredThisMonth,
    string Currency);

public sealed record PlatformFeeSettingsDto(
    decimal PlatformFeePercent,
    decimal PreEventWithdrawPercent,
    decimal PostEventWithdrawPercent,
    decimal MinPayoutAmount,
    string? Notes = null,
    decimal FullRefundPercent = 100,
    decimal PartialRefundPercent = 50,
    bool AllowRefundAfterEventStart = false);
