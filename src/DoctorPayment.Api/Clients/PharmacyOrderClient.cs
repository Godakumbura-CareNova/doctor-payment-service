using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DoctorPayment.Api.Exceptions;

namespace DoctorPayment.Api.Clients;

/// <summary>The fields of a pharmacy order that payment needs, as the pharmacy-order service returns them.</summary>
public record PharmacyOrderSnapshot(int Id, string? OrderNumber, string? Status, decimal TotalAmount, List<PharmacyOrderLine>? Items);

public record PharmacyOrderLine(string? MedicineName, int Quantity, decimal UnitPrice);

public interface IPharmacyOrderClient
{
    /// <summary>
    /// The patient's order, read with the patient's own token so the pharmacy-order service enforces ownership.
    /// Null when the order does not exist or is not theirs; throws <see cref="OrderServiceUnavailableException"/> otherwise.
    /// </summary>
    Task<PharmacyOrderSnapshot?> GetOrderAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken = default);
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
}
