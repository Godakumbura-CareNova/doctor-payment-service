using Microsoft.AspNetCore.Authorization;

namespace DoctorPayment.Api.Security;

public static class AuthorizationPolicies
{
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";
    public const string Admin = "Admin";
    public const string PharmacyStaff = "PharmacyStaff";

    public static IServiceCollection AddDoctorPaymentAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Doctor, policy => policy.RequireRole(Doctor));
            options.AddPolicy(Patient, policy => policy.RequireRole(Patient));
            options.AddPolicy(Admin, policy => policy.RequireRole(Admin));
            // User-auth issues both spellings for existing accounts.
            options.AddPolicy(PharmacyStaff, policy => policy.RequireRole(PharmacyStaff, "Pharmacy Staff"));
        });

        return services;
    }
}
