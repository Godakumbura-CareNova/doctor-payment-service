using DoctorPayment.Api.Config;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Sends saved outbox events to Kafka. A failed send is recorded and retried up to the limit. It never touches the
/// payment, so a receipt whose event could not be sent stays waiting for verification; it is never treated as verified.
/// </summary>
public class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly KafkaSettings _settings;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await PublishPendingEventsAsync(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IKafkaEventProducer>(),
                    _settings.PublishRetryLimit,
                    _logger,
                    stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Unable to publish pending Kafka events.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Publishes up to 20 unsent events, oldest first, recording each attempt and any error.</summary>
    public static async Task PublishPendingEventsAsync(
        AppDbContext dbContext,
        IKafkaEventProducer producer,
        int retryLimit,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var entries = await dbContext.EventOutboxEntries
            .Where(entry => entry.PublishedAtUtc == null && entry.PublishAttempts < retryLimit)
            .OrderBy(entry => entry.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            entry.PublishAttempts++;

            try
            {
                await producer.PublishAsync(entry, cancellationToken);
                entry.PublishedAtUtc = DateTime.UtcNow;
                entry.LastError = null;
            }
            catch (Exception exception)
            {
                entry.LastError = exception.Message;
                logger.LogWarning(exception, "Kafka publish failed for outbox event {EventId}.", entry.EventId);
            }
        }

        if (entries.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
