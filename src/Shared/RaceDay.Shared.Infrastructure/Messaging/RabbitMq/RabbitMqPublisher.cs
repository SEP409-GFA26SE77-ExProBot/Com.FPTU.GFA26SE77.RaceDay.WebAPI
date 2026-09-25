using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace RaceDay.Shared.Infrastructure.Messaging.RabbitMq;

public interface IRabbitMqPublisher
{
    bool IsEnabled { get; }
    Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default);
}

public sealed class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable
{
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly ConnectionFactory? _factory;
    private readonly string _exchange;
    private IConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsEnabled { get; }

    public RabbitMqPublisher(IConfiguration configuration, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("RabbitMq");
        IsEnabled = !string.IsNullOrWhiteSpace(connectionString);
        _exchange = configuration["RabbitMq:Exchange"] ?? "raceday.integration";
        if (IsEnabled)
        {
            _factory = new ConnectionFactory { Uri = new Uri(connectionString!) };
        }
    }

    public async Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || _factory is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _connection ??= await _factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.ExchangeDeclareAsync(
                exchange: _exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = Encoding.UTF8.GetBytes(payload);
            var props = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = eventType
            };

            await channel.BasicPublishAsync(
                exchange: _exchange,
                routingKey: eventType,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Published {EventType} to RabbitMQ exchange {Exchange}", eventType, _exchange);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ publish failed for {EventType}; outbox will retry", eventType);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}

public sealed class NullRabbitMqPublisher : IRabbitMqPublisher
{
    public bool IsEnabled => false;

    public Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
