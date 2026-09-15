using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Infrastructure.Caching;
using RaceDay.Shared.Infrastructure.Messaging.RabbitMq;
using RaceDay.Shared.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace RaceDay.Shared.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IEventBus, RabbitMqEventBus>();

        var mongoConnection = configuration.GetConnectionString("MongoDb");
        if (!string.IsNullOrWhiteSpace(mongoConnection))
        {
            services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnection));
            services.AddSingleton<IMongoDbContext>(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                var databaseName = configuration["MongoDb:Database"] ?? "raceday";
                return new MongoDbContext(client.GetDatabase(databaseName));
            });
        }

        return services;
    }
}
