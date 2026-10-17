using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Infrastructure.Caching;
using RaceDay.Shared.Infrastructure.Email;
using RaceDay.Shared.Infrastructure.Messaging;
using RaceDay.Shared.Infrastructure.Messaging.Outbox;
using RaceDay.Shared.Infrastructure.Messaging.RabbitMq;
using RaceDay.Shared.Infrastructure.Persistence.Mongo;
using RaceDay.Shared.Infrastructure.Storage;

namespace RaceDay.Shared.Infrastructure;

public static class DependencyInjection
{
    private static int _mongoSerializersConfigured;

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
        services.AddSingleton<MediatorEventBus>();

        var storageProvider = configuration["Storage:Provider"] ?? "Local";
        if (string.Equals(storageProvider, "R2", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, CloudflareR2FileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, LocalFileStorage>();
        }

        if (!string.IsNullOrWhiteSpace(configuration["Smtp:Host"]))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        var rabbitConnection = configuration.GetConnectionString("RabbitMq");
        if (!string.IsNullOrWhiteSpace(rabbitConnection))
        {
            services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
        }
        else
        {
            services.AddSingleton<IRabbitMqPublisher, NullRabbitMqPublisher>();
        }

        // Default bus = in-process MediatR (+ RabbitMQ when configured).
        // Do NOT auto-switch to OutboxEventBus just because IAM registered IOutboxStore:
        // Event/Payment/Finance publish via IEventBus then SaveChanges on *their* DbContext —
        // IAM outbox rows never commit → RaceEventCreated / OrganizerRoleRequired silently drop.
        // OutboxEventBus stays available for modules that enqueue + SaveChanges the same store.
        services.AddScoped<OutboxEventBus>();
        services.AddScoped<IEventBus>(sp => sp.GetRequiredService<MediatorEventBus>());

        services.AddHostedService<OutboxDispatcher>();

        var mongoConnection = configuration.GetConnectionString("MongoDb");
        if (!string.IsNullOrWhiteSpace(mongoConnection))
        {
            EnsureMongoSerializers();
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

    private static void EnsureMongoSerializers()
    {
        if (Interlocked.Exchange(ref _mongoSerializersConfigured, 1) == 1)
        {
            return;
        }

        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
}
