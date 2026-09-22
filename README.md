# Doctor Payment Service

Independent ASP.NET Core 8 API for Sprint 3 doctor profiles, availability,
consultation fee-change requests, and medicine-payment transactions.

## Local setup

1. Create a MySQL database named `carenova_doctor_payment`.
2. Supply the connection string and JWT signing secret through .NET user secrets
   or environment variables. Do not place them in committed JSON files.
   The JWT secret, issuer and audience must match the user-auth service so the
   tokens it issues are accepted here.

   ```powershell
   cd src/DoctorPayment.Api
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=carenova_doctor_payment;User=root;Password=<password>;"
   dotnet user-secrets set "JwtSettings:Secret" "<same secret as carenova-user-auth-service>"
   ```

3. Apply the database migrations:

   ```powershell
   dotnet ef database update --project src/DoctorPayment.Api
   ```

4. Run the API (listens on `http://localhost:5135`, Swagger UI at `/swagger`):

   ```powershell
   dotnet run --project src/DoctorPayment.Api
   ```

5. Confirm the service is running:

   ```powershell
   Invoke-WebRequest http://localhost:5135/health
   ```

## Implemented features

### Doctor professional profile (GCN-36)

Doctors maintain their own professional profile. Identity details (name, email,
phone, address) stay in the user-auth service; this service stores the
professional information only.

| Method | Route                     | Auth            | Description                                   |
| ------ | ------------------------- | --------------- | --------------------------------------------- |
| GET    | `/api/doctors/me/profile` | `Doctor` role   | Returns the profile, or `404` if not created. |
| PUT    | `/api/doctors/me/profile` | `Doctor` role   | Creates or fully replaces the profile.        |
| DELETE | `/api/doctors/me/profile` | `Doctor` role   | Deletes the profile (`204`), or `404` if none. |

The profile is always resolved from the JWT subject; a doctor can only read and
update their own record. Other roles receive `403`, anonymous callers `401`.

Request body for `PUT`:

```json
{
  "specialization": "General Practitioner",
  "registrationNumber": "SLMC-12345",
  "biography": "Optional, up to 2000 characters.",
  "qualifications": [
    { "degree": "MBBS", "institution": "University of Colombo", "startYear": 2010, "endYear": 2015 }
  ],
  "experiences": [
    { "position": "Senior Medical Officer", "organization": "District General Hospital, Colombo", "startYear": 2016, "endYear": null }
  ],
  "otherDetails": [
    { "title": "Member, Sri Lanka Medical Association", "details": "Optional, up to 300 characters.", "startYear": 2018, "endYear": null }
  ]
}
```

Validation failures return `400` with `{ "message": "...", "errors": { "Field": ["..."] } }`,
where nested keys look like `Qualifications[0].Degree`. A `null` end year means
"Present" (in progress / still current). `otherDetails` is an optional section
for memberships, awards, publications, training and similar; it may be empty.

## Tests

```powershell
dotnet test DoctorPayment.sln
```

Unit tests (`tests/DoctorPayment.Api.Tests/Unit`) cover the validator, service
and controller for the doctor profile story. `Integration/HealthEndpointTests`
is the scaffold smoke test that boots the API.
