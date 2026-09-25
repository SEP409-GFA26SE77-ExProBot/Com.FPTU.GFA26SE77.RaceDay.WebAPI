namespace RaceDay.Modules.Iam.Domain.Users;

public enum Gender : short
{
    Unknown = 0,
    Male = 1,
    Female = 2
}

/// <summary>
/// Account lifecycle status (login gate).
/// Pending = registered, waiting email OTP; Active = verified; Locked = admin lock.
/// </summary>
public enum UserStatus : short
{
    Locked = 0,
    Active = 1,
    Pending = 2
}

/// <summary>
/// Profile KYC / organizer verification — independent from email OTP (<see cref="User.EmailVerified"/>).
/// </summary>
public enum VerificationStatus : short
{
    None = 0,
    Pending = 1,
    Verified = 2
}

public enum OtpPurpose : short
{
    EmailVerification = 1,
    PasswordReset = 2
}
