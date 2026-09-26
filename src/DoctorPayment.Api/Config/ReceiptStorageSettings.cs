namespace DoctorPayment.Api.Config;

/// <summary>Where bank transfer receipts are kept and which files are accepted. Bound from "ReceiptStorage".</summary>
public class ReceiptStorageSettings
{
    /// <summary>Relative to the content root. Kept outside wwwroot so receipts are only served to pharmacy staff.</summary>
    public string UploadPath { get; set; } = "uploads/receipts";

    public int MaxFileSizeMb { get; set; } = 5;

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}
