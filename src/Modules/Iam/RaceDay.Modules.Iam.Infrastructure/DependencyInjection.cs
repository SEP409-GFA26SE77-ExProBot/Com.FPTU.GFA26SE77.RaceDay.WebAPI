using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Infrastructure.Identity;
using RaceDay.Modules.Iam.Infrastructure.Messaging;
using RaceDay.Modules.Iam.Infrastructure.Persistence;
using RaceDay.Modules.Iam.Infrastructure.Repositories;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Infrastructure.Persistence.Ef;

namespace RaceDay.Modules.Iam.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIamInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IamDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam")));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IEmailOtpRepository, EmailOtpRepository>();
        services.AddScoped<IEmailOtpService, EmailOtpService>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork<IamDbContext>>();
        services.AddScoped<IOutboxStore, IamOutboxStore>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddHostedService<IamDataSeeder>();

        return services;
    }
}
