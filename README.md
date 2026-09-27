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
  "consultationFee": 2500.00,
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
`consultationFee` is optional (omit or `null` = not set); when given it must be
0–1,000,000 with at most two decimals and is stored in LKR. Patients see it on
the public profile and in search results (GCN-35).

`profilePicturePath` is optional and records **which** user-auth picture belongs
to this doctor, e.g. `/uploads/profile-pictures/abc.jpg`. The picture itself is
uploaded to and served by the user-auth service as a static file; this service
stores only the path (a snapshot, like `fullName`) so patients can find it
without this service calling user-auth. An absolute URL is reduced to its path,
and anything outside `/uploads/profile-pictures/` is rejected with `400`, since
the value comes from the client. No endpoint here serves image bytes.

### Doctor availability (GCN-37)

Doctors publish the consultation slots patients see on their public profile.
A window is split into consecutive slots of `slotDurationMinutes`; any remainder
shorter than one slot is dropped.

| Method | Route                                    | Auth          | Description                                                                 |
| ------ | ---------------------------------------- | ------------- | --------------------------------------------------------------------------- |
| GET    | `/api/doctors/me/availability`           | `Doctor` role | Upcoming slots (booked included), soonest first. `[]` when none/no profile. |
| POST   | `/api/doctors/me/availability`           | `Doctor` role | Adds a window; returns `201` with the slots created.                        |
| DELETE | `/api/doctors/me/availability/{slotId}`  | `Doctor` role | Removes an open slot (`204`); `404` if unknown/not yours, `409` if booked.  |

Request body for `POST` (times are UTC; a value without a zone suffix is treated as UTC):

```json
{ "startUtc": "2026-10-01T09:00:00Z", "endUtc": "2026-10-01T11:00:00Z", "slotDurationMinutes": 20 }
```

Rules: end after start, window at most 12 hours, slot length 10–240 minutes and
no longer than the window (`400` with `{ message, errors }`); start must be in
the future and at most 90 days ahead (`400`); the window must not overlap
existing slots and a doctor may hold at most 200 open upcoming slots (`409`);
the doctor must have a professional profile (`409`).

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
booked and starts in the future); doctors manage the rows through the
availability endpoints below. Each item and the public profile also carry
`consultationFee` (nullable).

### Medicine order payment method

At checkout, a patient chooses how to pay before placing a pharmacy order:
online, bank transfer or cash. The frontend lists the methods for the cart total,
places the order, then saves the method against the new order. The order itself lives in the pharmacy-order service; this
service reads it with the patient's own token (so that service decides whether
the order is theirs) and takes the amount from it, never from the client.

| Method | Route                                           | Auth           | Description                                                      |
| ------ | ----------------------------------------------- | -------------- | ---------------------------------------------------------------- |
| GET    | `/api/medicine-payments/methods?amount=`         | `Patient` role | Methods for a checkout total, before the order exists (advisory; re-checked on `PUT`). |
| GET    | `/api/medicine-orders/{orderId}/payment-options` | `Patient` role | Every method, marked available or not, plus any method chosen.   |
| PUT    | `/api/medicine-orders/{orderId}/payment`         | `Patient` role | Chooses or changes the method; repeating the same choice is safe. |
| GET    | `/api/medicine-orders/{orderId}/payment`         | `Patient` role | The chosen method, or `404` if none yet.                         |

`PUT` takes `{ "method": "Online" | "BankTransfer" | "Cash" }` and returns the
payment with a `nextStep`:

| Method         | Status                  | `nextStep`                 | What the patient sees                               |
| -------------- | ----------------------- | -------------------------- | --------------------------------------------------- |
| `Online`       | `AwaitingOnlinePayment` | `OnlinePayment`            | The online payment page (gateway comes in a later story). |
| `BankTransfer` | `AwaitingBankTransfer`  | `BankTransferInstructions` | Bank details (`bankTransfer`) and the receipt upload option. |
| `Cash`         | `PayOnCollection`       | `PayOnCollection`          | Confirmation to pay at the counter; no online payment. |

Errors: `400` no/unknown method; `404` order not found or not the patient's;
`422` order rejected, collected or with inconsistent details; `409` method not
available for this order (switched off, bank details missing, or cash above
`CashMaxAmount`); `503` the pharmacy-order service could not be reached (the
client offers a retry). All carry `{ "message": "..." }`.

### Bank transfer receipt upload (GCN-40)

After choosing bank transfer the patient uploads the transfer receipt. The
pharmacy order **stays Pending**: uploading never confirms it. The payment
waits for pharmacy staff, and only a staff verification marks it `Paid`.

| Method | Route                                                   | Auth                 | Description |
| ------ | ------------------------------------------------------- | -------------------- | ----------- |
| POST   | `/api/medicine-orders/{orderId}/payment/receipt`         | `Patient` role       | Multipart form field `file`: JPG, JPEG or PNG, max 5 MB. Returns `{ message, orderStatus, payment }`. |
| GET    | `/api/staff/bank-transfer-receipts?status=`              | `PharmacyStaff` role | `PendingVerification` (default), `Verified`, `Rejected` or `All`. |
| GET    | `/api/staff/bank-transfer-receipts/{receiptId}/file`     | `PharmacyStaff` role | The receipt image. |
| POST   | `/api/staff/bank-transfer-receipts/{receiptId}/verify`   | `PharmacyStaff` role | Receipt `Verified`, payment `Paid`. |
| POST   | `/api/staff/bank-transfer-receipts/{receiptId}/reject`   | `PharmacyStaff` role | `{ "reason": "..." }`; payment `ReceiptRejected`, the patient can upload again. |
| GET    | `/api/staff/payment-notifications`                       | `PharmacyStaff` role | Notifications created from the Kafka events. |
| PUT    | `/api/staff/payment-notifications/{id}/read`             | `PharmacyStaff` role | Marks a notification read. |

Payment status after bank transfer: `AwaitingBankTransfer` → (upload)
`ReceiptUnderVerification` → (staff) `Paid` or `ReceiptRejected` → (upload
again) `ReceiptUnderVerification`. While a receipt is under verification or
paid, the payment method cannot be changed.

Files are checked by extension **and** content (JPEG/PNG signature), so a
renamed PDF is refused. They are stored under `ReceiptStorage:UploadPath`
(default `uploads/receipts`, outside `wwwroot`) and only served to staff.

Upload errors: `400` no file, unsupported format or over 5 MB; `404` order not
found or not the patient's; `422` the receipt cannot be associated with the
order (bank transfer not chosen, order rejected/collected, or the order total
changed); `409` a receipt is already under verification or verified; `503`
storing failed or the order could not be loaded (nothing is saved, so the
patient can retry).

**Kafka** follows the pharmacy-order service's outbox pattern. The receipt,
the payment status and an `EventOutboxEntries` row are written in one save.
`OutboxPublisherService` publishes pending rows to
`carenova.payment.events.v1` every 5 s, retrying up to
`Kafka:PublishRetryLimit` times and recording `LastError`. A publish failure
never changes the payment: it stays `ReceiptUnderVerification` and the staff
list shows the event as `Failed`. `KafkaConsumerService` (the verification
process) reads `BankReceiptUploaded` events and creates a
`PaymentStaffNotifications` row. Events: `BankReceiptUploaded`,
`BankReceiptVerified`, `BankReceiptRejected`. Each carries the order id and
number, amount, patient id, customer name (from the JWT `name` claim) and a
message. Locally, start the broker with the pharmacy-order service's
`docker-compose.kafka.yml` (`localhost:9092`).

Configuration:

| Key                                    | Meaning                                                                 |
| -------------------------------------- | ----------------------------------------------------------------------- |
| `PharmacyOrderService:BaseUrl`         | Pharmacy-order API including `/api` locally (`http://localhost:5134/api`); the gateway's `/pharmacy` in production. |
| `MedicinePayments:EnabledMethods`      | Optional array of methods to offer; omit to offer all three.            |
| `MedicinePayments:CashMaxAmount`       | Largest order total payable in cash (default `25000`).                   |
| `MedicinePayments:BankTransfer:*`      | `BankName`, `Branch`, `AccountName`, `AccountNumber`. Bank transfer is shown as unavailable until these are set. Only sample values are committed (development); set the real account in Azure app settings, e.g. `MedicinePayments__BankTransfer__AccountNumber`. |

### Medicine payment transactions and order statuses (GCN-43)

Pharmacy staff and admins view every medicine payment and its order status
(`PaymentViewing` policy). **Only pharmacy staff change statuses**
(`PharmacyStaff` policy); admins get a read-only view and `403` on changes.
Patients and doctors get `403` everywhere here.

| Method | Route | Who | Description |
| ------ | ----- | --- | ----------- |
| GET | `/api/staff/medicine-payments?status=&method=&search=` | Staff, Admin | Newest-changed first (max 200) with counts per group. `status`: `Pending`, `Successful`, `Failed`, `Rejected` or `All`. |
| GET | `/api/staff/medicine-payments/{paymentId}` | Staff, Admin | Payment, customer, medicines, receipts, payment and order status history, and the allowed changes (empty for admins). |
| PUT | `/api/staff/medicine-payments/{paymentId}/status` | Staff | `{ "status": "Paid" \| "Failed", "note": "..." }` (note required for `Failed`). |
| PUT | `/api/staff/medicine-orders/{orderId}/status` | Staff | The same order status update, addressed by pharmacy order id. The Medicine Orders page uses this; `404` with `code: "NoPaymentRecord"` when the order has no payment here, and the page then updates the pharmacy directly. |
| PUT | `/api/staff/medicine-payments/{paymentId}/order-status` | Staff | `{ "status": "ReadyForCollection" \| "Rejected" \| "Collected", "reason": "..." }` (reason required for `Rejected`). |
| GET | `/api/patient/notifications` | Patient | The patient's order status notifications. `PUT .../{id}/read` and `PUT .../read-all` mark them read. |

Payment groups: **Pending** = `AwaitingOnlinePayment`, `AwaitingBankTransfer`,
`PayOnCollection`, `ReceiptUnderVerification`; **Successful** = `Paid`;
**Failed** = `Failed`; **Rejected** = `ReceiptRejected`. Staff may move online
or cash payments awaiting payment to `Paid` or `Failed`, and bank transfers
awaiting transfer (or with a rejected receipt) to `Failed`. A bank transfer is
`Paid` only through receipt verification (GCN-40).

**Order status rules:** `Pending` → `ReadyForCollection` or `Rejected`, and
`ReadyForCollection` → `Collected`. In addition:

- An online or bank-transfer order cannot be ready for collection until its
  payment is `Paid`.
- No order can be collected until its payment is `Paid` (cash is marked paid
  at the counter first).

The status is checked against the pharmacy's live status. Invalid changes
return `422` with a message, the pharmacy is not called, and no event is
published.

**Flow:**

1. doctor-payment forwards the change to the pharmacy-order service
   (`PUT /api/staff/orders/{id}/status` with the staff token). That service
   owns the order and restores stock on rejection.
2. doctor-payment records a `MedicineOrderStatusChanges` row and queues a
   `MedicineOrderStatusUpdated` event on its own topic,
   `carenova.order-status.events.v1`. GCN-40 receipt events stay on
   `carenova.payment.events.v1`. Both use the same outbox, publisher and
   consumer (which subscribes to both topics). The event carries the
   order id and number, payment id, patient id and name, previous status, new
   status, reason and time.
3. `KafkaConsumerService` creates the patient's `PatientNotifications` row and
   marks the change's notification `Delivered`. An event with no patient is
   marked `Failed` with the error.

**Failures:**

- A publish failure is retried up to `Kafka:PublishRetryLimit`. The detail page
  then shows the event as `Failed` and the notification as still `Pending`, so
  it is never treated as processed.
- If the pharmacy is unreachable, the API returns `503` and nothing changes.
- If the pharmacy applied the change but it could not be recorded here, the
  API returns `500` with a message.

`MedicinePayments.OrderStatus` keeps the last known order status, so admins can
see it; it is refreshed from the pharmacy when staff open a transaction. Order
details (`MedicinePaymentItems`, `CustomerName`) are copied when the patient
chooses the method. Payments chosen before GCN-43 show "not recorded".

## Tests

```powershell
dotnet test DoctorPayment.sln
```

Unit tests (`tests/DoctorPayment.Api.Tests/Unit`) cover the validators, services
and controllers for the doctor profile, doctor search and availability stories.
`Integration/DoctorSearchEndpointTests` boots the API with JWT auth against an
in-memory database and exercises search, fee and availability end to end;
`Integration/HealthEndpointTests` is the scaffold smoke test.
`Unit/MedicinePaymentTests` covers choosing a payment method (each method,
unavailable methods, invalid orders, order-service failures).
`Unit/BankTransferReceiptTests` covers GCN-40: valid uploads, file format,
content and size checks, missing files, storage failures, association
failures, the Kafka event and notification, verification, rejection and
Kafka publish failures.
