using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DoctorPayment.Api.Exceptions;

namespace DoctorPayment.Api.Clients;

/// <summary>The fields of a pharmacy order that payment needs, as the pharmacy-order service returns them.</summary>
public record PharmacyOrderSnapshot(int Id, string? OrderNumber, string? Status, decimal TotalAmount, List<PharmacyOrderLine>? Items);

public record PharmacyOrderLine(string? MedicineName, int Quantity, decimal UnitPrice);

/// <summary>An active order (Pending or ReadyForCollection) from the pharmacy-order service's staff list.</summary>
public record StaffOrderSnapshot(int Id, string? OrderNumber, string? Status, decimal? TotalAmount = null);

/// <summary>The pharmacy-order service refused a status change (not found, not allowed, or invalid); nothing changed there.</summary>
public sealed class OrderStatusChangeRefusedException : Exception
{
    public OrderStatusChangeRefusedException(string message)
        : base(message)
    {
    }
}

public interface IPharmacyOrderClient
{
    /// <summary>
    /// The patient's order, read with the patient's own token so the pharmacy-order service enforces ownership.
    /// Null when the order does not exist or is not theirs; throws <see cref="OrderServiceUnavailableException"/> otherwise.
    /// </summary>
    Task<PharmacyOrderSnapshot?> GetOrderAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// The order's current status when it is still active (Pending or ReadyForCollection), read with the staff member's
    /// token; null when it is not in the active list (collected, rejected or missing). Throws <see cref="OrderServiceUnavailableException"/>.
    /// </summary>
    Task<string?> GetActiveOrderStatusAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// GCN-42: the order (number, status and total) when it is still active, read with the staff member's token; null when
    /// it is not in the active list. Throws <see cref="OrderServiceUnavailableException"/>.
    /// </summary>
    Task<StaffOrderSnapshot?> GetActiveOrderAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the pharmacy-order service to change the order's status (it restores stock on rejection) and returns the new
    /// status. Throws <see cref="OrderStatusChangeRefusedException"/> when it refuses and <see cref="OrderServiceUnavailableException"/>
    /// when it cannot be reached.
    /// </summary>
    Task<string> UpdateOrderStatusAsync(int orderId, string status, string? reason, string? authorizationHeader, CancellationToken cancellationToken = default);
}

public class PharmacyOrderClient : IPharmacyOrderClient
{
    private const string UnavailableMessage = "We could not load your order right now. Please try again.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PharmacyOrderClient> _logger;

    public PharmacyOrderClient(HttpClient httpClient, ILogger<PharmacyOrderClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PharmacyOrderSnapshot?> GetOrderAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        if (_httpClient.BaseAddress is null)
        {
            _logger.LogError("PharmacyOrderService:BaseUrl is not configured, so orders cannot be read");
            throw new OrderServiceUnavailableException(UnavailableMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"orders/{orderId}");
        if (AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)) request.Headers.Authorization = authorization;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pharmacy-order service returned {StatusCode} for order {OrderId}", (int)response.StatusCode, orderId);
                throw new OrderServiceUnavailableException(UnavailableMessage);
            }

            return await response.Content.ReadFromJsonAsync<PharmacyOrderSnapshot>(cancellationToken)
                ?? throw new OrderServiceUnavailableException(UnavailableMessage);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(exception, "Could not read order {OrderId} from the pharmacy-order service", orderId);
            throw new OrderServiceUnavailableException(UnavailableMessage, exception);
        }
    }

    public async Task<string?> GetActiveOrderStatusAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        var orders = await SendAsync<List<StaffOrderSnapshot>>(HttpMethod.Get, "staff/orders", null, authorizationHeader, orderId, cancellationToken);
        return orders?.FirstOrDefault(order => order.Id == orderId)?.Status;
    }

    public async Task<StaffOrderSnapshot?> GetActiveOrderAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        var orders = await SendAsync<List<StaffOrderSnapshot>>(HttpMethod.Get, "staff/orders", null, authorizationHeader, orderId, cancellationToken);
        return orders?.FirstOrDefault(order => order.Id == orderId);
    }

    public async Task<string> UpdateOrderStatusAsync(int orderId, string status, string? reason, string? authorizationHeader,
        CancellationToken cancellationToken = default)
    {
        var updated = await SendAsync<PharmacyOrderSnapshot>(HttpMethod.Put, $"staff/orders/{orderId}/status",
            JsonContent.Create(new { status, reason }), authorizationHeader, orderId, cancellationToken);
        return updated?.Status ?? throw new OrderServiceUnavailableException(UnavailableMessage);
    }

    /// <summary>Staff calls: a 400, 404 or 409 is the pharmacy-order service refusing, with its message; anything else is unavailability.</summary>
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, HttpContent? content, string? authorizationHeader, int orderId,
        CancellationToken cancellationToken)
    {
        if (_httpClient.BaseAddress is null)
        {
            _logger.LogError("PharmacyOrderService:BaseUrl is not configured, so orders cannot be managed");
            throw new OrderServiceUnavailableException(UnavailableMessage);
        }

        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)) request.Headers.Authorization = authorization;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                throw new OrderStatusChangeRefusedException("Your account is not allowed to manage orders in the pharmacy system.");
            }
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                var refusal = await response.Content.ReadFromJsonAsync<RefusalBody>(cancellationToken);
                throw new OrderStatusChangeRefusedException(refusal?.Message ?? "The pharmacy could not apply this order status change.");
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pharmacy-order service returned {StatusCode} for {Method} {Path}", (int)response.StatusCode, method, path);
                throw new OrderServiceUnavailableException(UnavailableMessage);
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(exception, "Could not reach the pharmacy-order service for order {OrderId}", orderId);
            throw new OrderServiceUnavailableException(UnavailableMessage, exception);
        }
    }

    private sealed record RefusalBody(string? Message);
}
