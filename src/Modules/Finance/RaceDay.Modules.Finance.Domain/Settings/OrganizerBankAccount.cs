using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Domain.Settings;

public sealed class OrganizerBankAccount : AggregateRoot
{
    public Guid OrganizerUserId { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string AccountHolderName { get; private set; } = string.Empty;
    public string? Branch { get; private set; }
    public string? TransferNote { get; private set; }
    public string? Bin { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private OrganizerBankAccount()
    {
    }

    public static OrganizerBankAccount Create(
        Guid organizerUserId,
        string bankName,
        string accountNumber,
        string accountHolderName,
        string? branch,
        string? transferNote,
        string? bin)
    {
        if (organizerUserId == Guid.Empty)
        {
            throw new DomainException("OrganizerUserId không hợp lệ.");
        }

        var a = new OrganizerBankAccount { OrganizerUserId = organizerUserId };
        a.Replace(bankName, accountNumber, accountHolderName, branch, transferNote, bin);
        return a;
    }

    public void Replace(
        string bankName,
        string accountNumber,
        string accountHolderName,
        string? branch,
        string? transferNote,
        string? bin)
    {
        if (string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(accountHolderName))
        {
            throw new DomainException("Thiếu thông tin tài khoản ngân hàng.");
        }

        BankName = bankName.Trim();
        AccountNumber = accountNumber.Trim();
        AccountHolderName = accountHolderName.Trim();
        Branch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();
        TransferNote = string.IsNullOrWhiteSpace(transferNote) ? null : transferNote.Trim();
        Bin = string.IsNullOrWhiteSpace(bin) ? null : bin.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
