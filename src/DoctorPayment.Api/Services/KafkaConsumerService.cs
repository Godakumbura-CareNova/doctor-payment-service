using Confluent.Kafka;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Events;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Reads both topics: bank receipt events (staff notifications, GCN-40) and order status events (patient notifications, GCN-43).
/// </summary>
public class KafkaConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaSettings _settings;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(IServiceScopeFactory scopeFactory, IOptions<KafkaSettings> settings,
        ILogger<KafkaConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(new[] { EventTypes.PaymentTopic, EventTypes.OrderStatusTopic });

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
                using var scope = _scopeFactory.CreateScope();
                var handler = new KafkaNotificationHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>());
                await handler.HandleAsync(result.Topic, result.Message.Value, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Re-read the failed record; a later commit must not skip this notification.
                if (result != null) consumer.Seek(result.TopicPartitionOffset);
                _logger.LogError(exception, "Kafka event handling failed; the event will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
