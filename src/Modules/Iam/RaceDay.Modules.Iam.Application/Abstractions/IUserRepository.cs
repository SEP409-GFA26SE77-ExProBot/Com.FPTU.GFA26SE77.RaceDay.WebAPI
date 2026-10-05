using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Application.Abstractions;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<User?> GetByIdWithProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<User> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface IJwtTokenService
{
    string CreateAccessToken(User user, string roleName);
}

public interface IEmailOtpRepository
{
    Task AddAsync(EmailOtp otp, CancellationToken cancellationToken = default);
    Task<EmailOtp?> GetLatestActiveAsync(string email, OtpPurpose purpose, CancellationToken cancellationToken = default);
    Task InvalidateActiveAsync(string email, OtpPurpose purpose, CancellationToken cancellationToken = default);
    void Update(EmailOtp otp);
}

public interface IEmailOtpService
{
    Task<string> IssueAndSendAsync(
        string email,
        string username,
        Guid? userId,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default);
    Task<EmailOtp> VerifyAsync(string email, OtpPurpose purpose, string code, CancellationToken cancellationToken = default);
}
