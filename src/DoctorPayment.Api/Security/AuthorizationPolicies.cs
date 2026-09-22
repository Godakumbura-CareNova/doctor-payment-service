using Microsoft.AspNetCore.Authorization;

namespace DoctorPayment.Api.Security;

public static class AuthorizationPolicies
{
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";

    public static IServiceCollection AddDoctorPaymentAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Doctor, policy => policy.RequireRole(Doctor));
            options.AddPolicy(Patient, policy => policy.RequireRole(Patient));
        });

        return services;
    }
}
