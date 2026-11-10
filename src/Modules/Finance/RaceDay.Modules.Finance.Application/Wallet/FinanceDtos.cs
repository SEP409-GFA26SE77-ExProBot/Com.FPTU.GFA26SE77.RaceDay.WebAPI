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
    string Currency,
    /// <summary>too_early | advance | final</summary>
    string WithdrawPhase = "too_early",
    decimal AdvanceCapAmount = 0,
    decimal FinalCapAmount = 0,
    int? DaysUntilEvent = null);

public sealed record RefundQuoteDto(
    Guid PaymentId,
    Guid RegistrationId,
    Guid EventId,
    decimal PaidAmount,
    decimal RefundPercent,
    decimal RefundAmount,
    decimal NonRefundableFee,
    string PolicyNote,
    int? DaysUntilEvent);

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
    string? RejectReason,
    bool IsSystemErrorClaim = false,
    decimal PaidAmount = 0,
    decimal RefundPercent = 0,
    Guid? UserId = null);

public sealed record ManagerSystemErrorRefundDto(
    Guid Id,
    Guid PaymentId,
    Guid RegistrationId,
    Guid EventId,
    Guid UserId,
    decimal PaidAmount,
    decimal RefundAmount,
    decimal RefundPercent,
    string Reason,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? RejectReason,
    string? EventName = null,
    string? RegistrationStatus = null,
    string? RunnerName = null,
    string? RunnerEmail = null,
    string? BibNumber = null,
    /// <summary>Lý do đã bỏ prefix / khối đính kèm.</summary>
    string? ReasonText = null,
    IReadOnlyList<string>? EvidenceUrls = null);

public sealed record CreateRefundRequestDto(
    Guid PaymentId,
    string Reason,
    Guid? EventId = null,
    decimal? Amount = null,
    /// <summary>Khiếu nại lỗi hệ thống → hoàn 100% (bỏ qua mốc ngày).</summary>
    bool SystemErrorClaim = false);

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
    decimal PartialRefundPercent = 85,
    bool AllowRefundAfterEventStart = false);
