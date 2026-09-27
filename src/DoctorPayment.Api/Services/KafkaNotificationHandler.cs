using System.Globalization;
using System.Text.Json;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Events;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Turns Kafka events into notifications: an uploaded bank receipt (payment topic) notifies pharmacy staff, and an order
/// status update (order-status topic) notifies the patient. A redelivered event (same event id) does not notify twice. Other events need no notification.
/// </summary>
public sealed class KafkaNotificationHandler(AppDbContext db)
{
    public async Task HandleAsync(string topic, string json, CancellationToken ct = default)
    {
        if (topic != EventTypes.PaymentTopic && topic != EventTypes.OrderStatusTopic) throw new JsonException("Unsupported Kafka event.");

        var paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(json) ?? throw new JsonException("Empty Kafka event.");
        if (topic == EventTypes.OrderStatusTopic)
        {
            if (paymentEvent.EventType == EventTypes.MedicineOrderStatusUpdated) await NotifyPatientAsync(paymentEvent, ct);
            return;
        }
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

    /// <summary>
    /// Creates the patient's notification for an order status change and records on the change whether it was delivered.
    /// A notification that can never be delivered (no patient) is recorded as failed instead of being retried forever.
    /// </summary>
    private async Task NotifyPatientAsync(PaymentEvent orderEvent, CancellationToken ct)
    {
        var change = await db.MedicineOrderStatusChanges.SingleOrDefaultAsync(item => item.EventId == orderEvent.EventId, ct);
        if (await db.PatientNotifications.AnyAsync(n => n.EventId == orderEvent.EventId, ct))
        {
            if (change is { NotificationStatus: not PatientNotificationStatus.Delivered })
            {
                change.NotificationStatus = PatientNotificationStatus.Delivered;
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        var data = orderEvent.Data.Deserialize<OrderStatusEventData>() ?? throw new JsonException("Order status event has no data.");
        if (data.PatientId == Guid.Empty)
        {
            if (change is not null)
            {
                change.NotificationStatus = PatientNotificationStatus.Failed;
                change.NotificationError = "The order has no patient to notify.";
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        var (title, message) = data.NewStatus switch
        {
            MedicineOrderStatuses.ReadyForCollection => ("Your order is ready for collection",
                $"Order {data.OrderNumber} is ready for collection at the pharmacy."),
            MedicineOrderStatuses.Collected => ("Your order was collected", $"Order {data.OrderNumber} was marked as collected. Thank you."),
            MedicineOrderStatuses.Rejected => ("Your order was rejected",
                $"Order {data.OrderNumber} was rejected by the pharmacy{(string.IsNullOrWhiteSpace(data.Reason) ? "." : $": {data.Reason}")}"),
            _ => ("Your order was updated", $"Order {data.OrderNumber} is now {MedicinePaymentTransactionService.OrderStatusLabel(data.NewStatus)}.")
        };
        db.PatientNotifications.Add(new PatientNotification
        {
            EventId = orderEvent.EventId,
            PatientId = data.PatientId,
            Title = title,
            Message = message.Length <= 1000 ? message : message[..997] + "...",
            OrderId = data.OrderId,
            OrderStatus = data.NewStatus,
            CreatedAtUtc = orderEvent.OccurredAtUtc
        });
        if (change is not null)
        {
            change.NotificationStatus = PatientNotificationStatus.Delivered;
            change.NotificationError = null;
            change.NotifiedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
