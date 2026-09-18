# Doctor Payment Service

Independent ASP.NET Core 8 API for Sprint 3 doctor profiles, availability,
consultation fee-change requests, and medicine-payment transactions.

## Local setup

1. Create a MySQL database named `carenova_doctor_payment`.
2. Supply the connection string and JWT signing secret through .NET user secrets
   or environment variables. Do not place them in committed JSON files.
3. Run the API:

   ```powershell
   dotnet run --project src/DoctorPayment.Api
   ```

4. Confirm the service is running:

   ```powershell
   Invoke-WebRequest http://localhost:5000/health
   ```

The folders under `src/DoctorPayment.Api` are the developer workspace for the
Sprint 3 stories. Business endpoints, database entities, payment-provider
integration, and UI behavior are intentionally not implemented by this DevOps
scaffold.
