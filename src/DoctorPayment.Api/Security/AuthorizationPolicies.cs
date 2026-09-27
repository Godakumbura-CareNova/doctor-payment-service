using Microsoft.AspNetCore.Authorization;

namespace DoctorPayment.Api.Security;

public static class AuthorizationPolicies
{
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";
    public const string Admin = "Admin";
    public const string PharmacyStaff = "PharmacyStaff";

    /// <summary>Who may view medicine payment transactions and order statuses: pharmacy staff and admins. Only pharmacy staff change them.</summary>
    public const string PaymentViewing = "PaymentViewing";

    public static IServiceCollection AddDoctorPaymentAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Doctor, policy => policy.RequireRole(Doctor));
            options.AddPolicy(Patient, policy => policy.RequireRole(Patient));
            options.AddPolicy(Admin, policy => policy.RequireRole(Admin));
            // User-auth issues both spellings for existing accounts.
            options.AddPolicy(PharmacyStaff, policy => policy.RequireRole(PharmacyStaff, "Pharmacy Staff"));
            options.AddPolicy(PaymentViewing, policy => policy.RequireRole(PharmacyStaff, "Pharmacy Staff", Admin));
        });

        return services;
    }
}
