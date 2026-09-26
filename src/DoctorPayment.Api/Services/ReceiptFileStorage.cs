using DoctorPayment.Api.Config;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

public class ReceiptFileStorage : IReceiptFileStorage
{
    private readonly string _root;

    public ReceiptFileStorage(IOptions<ReceiptStorageSettings> settings, IWebHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, settings.Value.UploadPath));
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var path = PathFor(storedFileName);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        return storedFileName;
    }

    public Stream? Open(string storedFileName)
    {
        var path = PathFor(storedFileName);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Delete(string storedFileName)
    {
        var path = PathFor(storedFileName);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Stored names are generated here, but refuse anything that would leave the receipts folder anyway.</summary>
    private string PathFor(string storedFileName)
    {
        var path = Path.GetFullPath(Path.Combine(_root, Path.GetFileName(storedFileName)));
        if (!path.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid receipt path.");
        return path;
    }
}
