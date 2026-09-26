namespace DoctorPayment.Api.Services.Interfaces;

/// <summary>Keeps receipt files outside the web root.</summary>
public interface IReceiptFileStorage
{
    /// <summary>Stores the content under a new generated name ending in <paramref name="extension"/> and returns that name.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored receipt for reading, or null when it is missing.</summary>
    Stream? Open(string storedFileName);

    /// <summary>Removes a stored receipt; a missing file is ignored.</summary>
    void Delete(string storedFileName);
}
