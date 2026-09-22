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

### Doctor search and public profile (GCN-34)

Patients search the doctor directory and open a doctor's published profile.
The name shown in search is a snapshot of the doctor's user-auth name, taken
from the JWT each time the doctor saves their profile (`PUT /api/doctors/me/profile`).

| Method | Route                            | Auth           | Description                                                      |
| ------ | -------------------------------- | -------------- | ---------------------------------------------------------------- |
| GET    | `/api/doctors`                   | `Patient` role | Paged search; no criteria lists every doctor.                    |
| GET    | `/api/doctors/specializations`   | `Patient` role | Distinct specializations for the filter dropdown.                |
| GET    | `/api/doctors/{doctorId}`        | `Patient` role | Public profile with availability, or `404` if not published.     |

Query parameters for `GET /api/doctors`:

| Parameter        | Rules                                                                          |
| ---------------- | ------------------------------------------------------------------------------ |
| `search`         | Optional. 2–100 chars, matched against name **or** specialization (contains).   |
| `specialization` | Optional. Exact match, case-insensitive.                                        |
| `availableOnly`  | Optional `true`/`false`. Only doctors with an open upcoming consultation slot.  |
| `page`           | Default 1.                                                                      |
| `pageSize`       | Default 12, max 50.                                                             |

Response: `{ items, page, pageSize, totalCount, totalPages }`. Each item carries
`doctorId`, `fullName`, `specialization`, `registrationNumber`, `biography`,
`qualifications` (degree names), `yearsOfExperience` and
`availability { hasAvailableSlots, availableSlotCount, nextAvailableSlotUtc }`.
Doctors with open slots are listed first, then alphabetically. Invalid criteria
return `400` with the same `{ message, errors }` shape as the profile endpoints.

Availability is read from the `DoctorAvailabilitySlots` table (open = not
booked and starts in the future). Managing slots belongs to the availability
story; until then the table is populated directly.

## Tests

```powershell
dotnet test DoctorPayment.sln
```

Unit tests (`tests/DoctorPayment.Api.Tests/Unit`) cover the validators, services
and controllers for the doctor profile and doctor search stories.
`Integration/DoctorSearchEndpointTests` boots the API with JWT auth against an
in-memory database and exercises the search flow end to end;
`Integration/HealthEndpointTests` is the scaffold smoke test.
