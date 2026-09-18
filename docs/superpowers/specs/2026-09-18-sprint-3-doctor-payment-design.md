# Sprint 3 Doctor-Payment Repository Design

## Goal

Prepare the two Sprint 3 repositories so developers can implement GCN-34 through GCN-43 without changing the established Sprint 2 technology and delivery pattern.

## Approved decisions

- Use an independent ASP.NET Core 8 Web API, named `DoctorPayment.Api`.
- Deploy the service separately, behind the API-gateway path `/doctor-payment`.
- Use the separate MySQL database `carenova_doctor_payment`.
- Mirror the existing Sprint 2 layered backend design.
- Preserve the existing React/Vite frontend structure and add feature-oriented folders.
- Follow Sprint 2 Docker and GitHub Actions practices: no Kafka or Docker Compose in this sprint's scaffold.
- Do not commit production secrets; use local environment variables/.NET user secrets and Azure/GitHub secret stores.

## Backend layers

`Controllers`, `Services`, `Services/Interfaces`, `Repositories`, `Models`, `DTOs`, `Validators`, `Data`, `Config`, `Security`, `Exceptions`, and `Migrations` will be created under `src/DoctorPayment.Api`. Unit and integration tests will be separated under `tests/DoctorPayment.Api.Tests`.

Code uses meaningful domain names, not Jira identifiers. GCN-34/35 map to doctor search/profile, GCN-36 to profile management, GCN-37 to availability, GCN-38 to fee-change requests, and GCN-39–43 to medicine payments and receipts.

## Frontend additions

Add API integration, doctor/payment reusable component folders, page placeholders, service modules, validators, test folders, and routes. Configuration must use `VITE_DOCTOR_PAYMENT_API_BASE_URL`, never a hard-coded service URL.

## Delivery

Backend CI must restore, build, test, and Docker-build. CD builds an image, pushes it to Docker Hub, and deploys it to the dedicated Azure Web App. Frontend CI/CD receives the doctor-payment base URL as a build variable.
