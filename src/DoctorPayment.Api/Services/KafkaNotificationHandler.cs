using System.Globalization;
using System.Text.Json;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Events;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Turns an uploaded bank receipt event into a notification asking pharmacy staff to verify it. A redelivered event
/// (same event id) does not notify twice. Verified and rejected events need no staff notification.
/// </summary>
public sealed class KafkaNotificationHandler(AppDbContext db)
{
    public async Task HandleAsync(string topic, string json, CancellationToken ct = default)
    {
        if (topic != EventTypes.PaymentTopic) throw new JsonException("Unsupported Kafka event.");

        var paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(json) ?? throw new JsonException("Empty Kafka event.");
        if (paymentEvent.EventType != EventTypes.BankReceiptUploaded) return;
        if (await db.PaymentStaffNotifications.AnyAsync(n => n.EventId == paymentEvent.EventId, ct)) return;

        var data = paymentEvent.Data.Deserialize<BankReceiptEventData>() ?? throw new JsonException("Bank receipt event has no data.");
        var items = data.Items is { Count: > 0 }
            ? string.Join(", ", data.Items.Select(item => $"{item.Quantity} x {item.MedicineName}")) + ", "
            : string.Empty;
        var message = $"{data.CustomerName} uploaded a bank transfer receipt for order {data.OrderNumber} " +
            $"({items}LKR {data.Amount.ToString("N2", CultureInfo.InvariantCulture)}). Verify it before the order is confirmed.";
        db.PaymentStaffNotifications.Add(new PaymentStaffNotification
        {
            EventId = paymentEvent.EventId,
            NotificationType = paymentEvent.EventType,
            Title = "Bank receipt uploaded for verification",
            // The column holds 1000 characters; a very long order is cut rather than failing the event.
            Message = message.Length <= 1000 ? message : message[..997] + "...",
            OrderId = data.OrderId,
            PaymentId = data.PaymentId,
            CreatedAtUtc = paymentEvent.OccurredAtUtc
        });
        await db.SaveChangesAsync(ct);
    }
}
