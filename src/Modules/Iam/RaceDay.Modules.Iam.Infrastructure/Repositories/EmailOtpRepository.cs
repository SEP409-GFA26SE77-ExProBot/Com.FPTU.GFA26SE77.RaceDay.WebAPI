using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Infrastructure.Persistence;

namespace RaceDay.Modules.Iam.Infrastructure.Repositories;

public sealed class EmailOtpRepository(IamDbContext db) : IEmailOtpRepository
{
    public Task AddAsync(EmailOtp otp, CancellationToken cancellationToken = default)
        => db.EmailOtps.AddAsync(otp, cancellationToken).AsTask();

    public Task<EmailOtp?> GetLatestActiveAsync(string email, OtpPurpose purpose, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        return db.EmailOtps
            .Where(x => x.Email == normalized
                        && x.Purpose == purpose
                        && x.ConsumedOnUtc == null
                        && x.ExpiresOnUtc > now)
            .OrderByDescending(x => x.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task InvalidateActiveAsync(string email, OtpPurpose purpose, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var active = await db.EmailOtps
            .Where(x => x.Email == normalized && x.Purpose == purpose && x.ConsumedOnUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var otp in active)
        {
            otp.Consume();
        }
    }

    public void Update(EmailOtp otp) => db.EmailOtps.Update(otp);
}
