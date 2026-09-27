using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>One medicine line of the order a payment is for, copied when the payment method was chosen.</summary>
public class MedicinePaymentItem
{
    public int Id { get; set; }

    public int MedicinePaymentId { get; set; }

    [MaxLength(200)]
    public string MedicineName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
