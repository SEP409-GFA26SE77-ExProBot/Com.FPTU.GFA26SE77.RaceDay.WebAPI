using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Iam.Application.Abstractions;
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
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork<IamDbContext>>();

        return services;
    }
}
