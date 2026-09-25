using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.Domain.Profiles;

public sealed class Profile : Entity
{
    public string? Description { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public VerificationStatus VerificationStatus { get; private set; } = VerificationStatus.None;
    public DateTime? VerifiedAt { get; private set; }
    public string? VerifiedBy { get; private set; }

    private Profile()
    {
    }

    public static Profile CreateForUser(Guid userId, string? description = null, string? contactEmail = null, string? contactPhone = null)
    {
        return new Profile
        {
            Id = userId,
            Description = description,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            VerificationStatus = VerificationStatus.None
        };
    }

    public void UpdateContact(string? description, string? contactEmail, string? contactPhone)
    {
        Description = description;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
    }

    public void MarkVerified(string verifiedBy)
    {
        VerificationStatus = VerificationStatus.Verified;
        VerifiedAt = DateTime.UtcNow;
        VerifiedBy = verifiedBy;
    }
}
