using System.Globalization;
using System.Text.Json;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Events;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Turns Kafka events into notifications: an uploaded bank receipt (payment topic) notifies pharmacy staff, and an order
/// status update (order-status topic) notifies the patient. GCN-42: an approved or rejected bank transfer (validation
/// topic) notifies the patient. GCN-41: an online payment result notifies the patient, and a successful one also tells
/// pharmacy staff. A redelivered event (same event id) does not notify twice. Other events need no notification.
/// </summary>
public sealed class KafkaNotificationHandler(AppDbContext db)
{
    public async Task HandleAsync(string topic, string json, CancellationToken ct = default)
    {
        if (topic != EventTypes.PaymentTopic && topic != EventTypes.OrderStatusTopic && topic != EventTypes.BankTransferValidationTopic
            && topic != EventTypes.OnlinePaymentTopic)
        {
            throw new JsonException("Unsupported Kafka event.");
        }

        var paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(json) ?? throw new JsonException("Empty Kafka event.");
        if (topic == EventTypes.OnlinePaymentTopic)
        {
            await HandleOnlinePaymentResultAsync(paymentEvent, ct);
            return;
        }
        if (topic == EventTypes.BankTransferValidationTopic)
        {
            await HandleValidationResultAsync(paymentEvent, ct);
            return;
        }
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

    /// <summary>
    /// GCN-42: tells the patient their bank transfer was approved (the payment is confirmed) or rejected (with the reason,
    /// so they can upload a clearer receipt).
    /// </summary>
    private async Task HandleValidationResultAsync(PaymentEvent validationEvent, CancellationToken ct)
    {
        var data = validationEvent.Data.Deserialize<BankTransferValidationEventData>()
            ?? throw new JsonException("Bank transfer validation event has no data.");
        var amount = $"LKR {data.Amount.ToString("N2", CultureInfo.InvariantCulture)}";

        var (title, message, status) = validationEvent.EventType switch
        {
            EventTypes.BankTransferApproved => ("Your bank transfer was approved",
                $"The pharmacy verified your bank transfer of {amount} for order {data.OrderNumber}. Your payment is confirmed.",
                BankTransferNotificationStatuses.Approved),
            EventTypes.BankTransferRejected => ("Your bank transfer was rejected",
                $"The pharmacy could not verify your bank transfer for order {data.OrderNumber}: {data.Reason} Please upload a clear, valid receipt.",
                BankTransferNotificationStatuses.Rejected),
            _ => (null, null, null)
        };
        if (title is null || data.PatientId == Guid.Empty) return;
        if (await db.PatientNotifications.AnyAsync(n => n.EventId == validationEvent.EventId, ct)) return;

        db.PatientNotifications.Add(new PatientNotification
        {
            EventId = validationEvent.EventId,
            PatientId = data.PatientId,
            Title = title,
            Message = Cut(message!),
            OrderId = data.OrderId,
            OrderStatus = status!,
            CreatedAtUtc = validationEvent.OccurredAtUtc
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// GCN-41: tells the patient whether their online payment went through (a failed one says why and whether they can
    /// try again); a successful one also tells pharmacy staff the order is paid. Sandbox payments say so.
    /// </summary>
    private async Task HandleOnlinePaymentResultAsync(PaymentEvent paymentEvent, CancellationToken ct)
    {
        if (paymentEvent.EventType is not (EventTypes.OnlinePaymentSucceeded or EventTypes.OnlinePaymentFailed)) return;
        var data = paymentEvent.Data.Deserialize<OnlinePaymentEventData>() ?? throw new JsonException("Online payment event has no data.");
        var amount = $"LKR {data.Amount.ToString("N2", CultureInfo.InvariantCulture)}";
        var test = data.IsTest ? " (sandbox test payment)" : string.Empty;
        var succeeded = paymentEvent.EventType == EventTypes.OnlinePaymentSucceeded;
        var customer = string.IsNullOrWhiteSpace(data.CustomerName) ? "A patient" : data.CustomerName;
        var next = data.CanRetry ? "Your order is not paid; you can try again." : "Your order is not paid; please choose another payment method.";

        if (data.PatientId != Guid.Empty && !await db.PatientNotifications.AnyAsync(n => n.EventId == paymentEvent.EventId, ct))
        {
            db.PatientNotifications.Add(new PatientNotification
            {
                EventId = paymentEvent.EventId,
                PatientId = data.PatientId,
                Title = succeeded ? "Your online payment was successful" : "Your online payment failed",
                Message = Cut(succeeded
                    ? $"We received your online payment of {amount} for order {data.OrderNumber}{test}. Your order is paid."
                    : $"Your online payment of {amount} for order {data.OrderNumber} did not go through{test}: {data.FailureMessage} {next}"),
                OrderId = data.OrderId,
                OrderStatus = succeeded ? OnlinePaymentNotificationStatuses.Succeeded : OnlinePaymentNotificationStatuses.Failed,
                CreatedAtUtc = paymentEvent.OccurredAtUtc
            });
        }
        if (succeeded && !await db.PaymentStaffNotifications.AnyAsync(n => n.EventId == paymentEvent.EventId, ct))
        {
            db.PaymentStaffNotifications.Add(new PaymentStaffNotification
            {
                EventId = paymentEvent.EventId,
                NotificationType = paymentEvent.EventType,
                Title = "Online payment received",
                Message = Cut($"{customer} paid {amount} online for order {data.OrderNumber}{test}. The order is paid."),
                OrderId = data.OrderId,
                PaymentId = data.PaymentId,
                CreatedAtUtc = paymentEvent.OccurredAtUtc
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Cut(string message) => message.Length <= 1000 ? message : message[..997] + "...";
}

/// <summary>GCN-42: what a patient's bank transfer notification is about (kept in <see cref="PatientNotification.OrderStatus"/>).</summary>
public static class BankTransferNotificationStatuses
{
    public const string Approved = "PaymentApproved";
    public const string Rejected = "PaymentRejected";
}

/// <summary>GCN-41: what a patient's online payment notification is about (kept in <see cref="PatientNotification.OrderStatus"/>).</summary>
public static class OnlinePaymentNotificationStatuses
{
    public const string Succeeded = "OnlinePaymentSucceeded";
    public const string Failed = "OnlinePaymentFailed";
}
