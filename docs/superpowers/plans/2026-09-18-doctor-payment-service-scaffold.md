# Doctor-Payment Service Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a buildable, testable .NET 8 foundation for the independent Sprint 3 doctor-payment service.

**Architecture:** The service is an ASP.NET Core Web API with the same layered folders used by the Sprint 2 pharmacy service. This scaffold exposes only a health endpoint; developer teams add story behavior behind controllers, services, repositories, DTOs, and validators in later tasks.

**Tech Stack:** .NET 8, ASP.NET Core, xUnit, Microsoft.AspNetCore.Mvc.Testing, Docker, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-18-sprint-3-doctor-payment-design.md`

## Global Constraints

- Deploy as a separate service behind `/doctor-payment` with the independent database name `carenova_doctor_payment`.
- Use .NET 8 and preserve the Sprint 2 Controller → Service → Repository layering.
- Do not add Kafka or Docker Compose in this scaffold.
- Never commit database passwords, JWT keys, payment keys, Docker Hub tokens, or Azure publish profiles.
- Use meaningful domain names; do not use GCN ticket IDs as production folder or file names.

---

### Task 1: Bootstrap a buildable and testable .NET 8 API

**Files:**
- Create: `DoctorPayment.sln`
- Create: `src/DoctorPayment.Api/DoctorPayment.Api.csproj`
- Create: `src/DoctorPayment.Api/Program.cs`
- Create: `src/DoctorPayment.Api/Controllers/HealthController.cs`
- Create: `tests/DoctorPayment.Api.Tests/DoctorPayment.Api.Tests.csproj`
- Create: `tests/DoctorPayment.Api.Tests/Integration/HealthEndpointTests.cs`

**Interfaces:**
- Produces: `GET /health` returning HTTP 200 and `{ "status": "Healthy" }`.

- [ ] **Step 1: Write the failing health-endpoint integration test**

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails before the API exists**

Run: `dotnet test tests/DoctorPayment.Api.Tests --filter "FullyQualifiedName~HealthEndpointTests"`

Expected: FAIL because `DoctorPayment.Api` and `Program` do not exist.

- [ ] **Step 3: Create the solution, web project, test project, and minimal endpoint**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.Run();
public partial class Program { }
```

```csharp
// Controllers/HealthController.cs
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Get() => Ok(new { status = "Healthy" });
}
```

Use `dotnet new sln -n DoctorPayment`, `dotnet new webapi --no-openapi -n DoctorPayment.Api -o src/DoctorPayment.Api`, and `dotnet new xunit -n DoctorPayment.Api.Tests -o tests/DoctorPayment.Api.Tests`. Add a project reference from the test project to the API project and add `Microsoft.AspNetCore.Mvc.Testing` version `8.0.*` to the test project.

- [ ] **Step 4: Run build and health test**

Run: `dotnet build DoctorPayment.sln --configuration Release` and `dotnet test tests/DoctorPayment.Api.Tests --filter "FullyQualifiedName~HealthEndpointTests"`

Expected: both commands exit with code 0.

- [ ] **Step 5: Commit the bootstrap**

```bash
git add DoctorPayment.sln src/DoctorPayment.Api tests/DoctorPayment.Api.Tests
git commit -m "chore: bootstrap doctor payment API"
```

### Task 2: Create the agreed backend module boundaries and safe configuration

**Files:**
- Create: `src/DoctorPayment.Api/{Config,Data,DTOs,Exceptions,Migrations,Models,Repositories,Security,Services/Interfaces,Validators}/.gitkeep`
- Create: `src/DoctorPayment.Api/appsettings.json`
- Create: `src/DoctorPayment.Api/appsettings.Development.json`
- Create: `src/DoctorPayment.Api/appsettings.Production.json`
- Create: `.gitignore`
- Modify: `README.md`

**Interfaces:**
- Consumes: the API project from Task 1.
- Produces: tracked empty module boundaries and documented configuration keys for later implementations.

- [ ] **Step 1: Write the failing configuration test**

```csharp
[Fact]
public void DevelopmentConfiguration_DoesNotContainSecrets()
{
    var content = File.ReadAllText("src/DoctorPayment.Api/appsettings.Development.json");

    Assert.DoesNotContain("Password=", content, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Secret": "", content, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the test to verify it fails while the configuration file is absent**

Run: `dotnet test tests/DoctorPayment.Api.Tests --filter "FullyQualifiedName~DevelopmentConfiguration_DoesNotContainSecrets"`

Expected: FAIL because `appsettings.Development.json` is absent.

- [ ] **Step 3: Add empty tracked module folders and non-secret configuration**

Use `.gitkeep` in every empty folder. Put only safe defaults in `appsettings.json`:

```json
{
  "ConnectionStrings": { "DefaultConnection": "" },
  "JwtSettings": { "Issuer": "CareNova", "Audience": "CareNovaClients" },
  "Cors": { "AllowedOrigins": ["http://localhost:3000"] }
}
```

Add `bin/`, `obj/`, `.vs/`, `appsettings.Local.json`, and `.env` to `.gitignore`. Update the README with the local MySQL database name and instructions to configure database credentials and JWT secrets using user secrets or environment variables.

- [ ] **Step 4: Run the configuration test and inspect ignored files**

Run: `dotnet test tests/DoctorPayment.Api.Tests --filter "FullyQualifiedName~DevelopmentConfiguration"` and `git check-ignore -v appsettings.Local.json .env`

Expected: the test exits with code 0; both local-secret file names are ignored.

- [ ] **Step 5: Commit module boundaries and configuration**

```bash
git add src/DoctorPayment.Api .gitignore README.md tests/DoctorPayment.Api.Tests
git commit -m "chore: add doctor payment service structure"
```

### Task 3: Add Docker and GitHub Actions delivery checks

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/cd.yml`

**Interfaces:**
- Consumes: `DoctorPayment.sln` and `src/DoctorPayment.Api/DoctorPayment.Api.csproj`.
- Produces: a Docker image exposing port 8080 and CI/CD workflows matching Sprint 2 branch conventions.

- [ ] **Step 1: Write the Docker health verification command**

```powershell
docker build -t carenova-doctor-payment:local .
docker run --rm -d --name doctor-payment-check -p 8080:8080 carenova-doctor-payment:local
Invoke-WebRequest http://localhost:8080/health | Select-Object -ExpandProperty StatusCode
docker stop doctor-payment-check
```

- [ ] **Step 2: Run it to verify it fails before the Dockerfile exists**

Run: the PowerShell commands from Step 1.

Expected: `docker build` fails because `Dockerfile` is absent.

- [ ] **Step 3: Add the multi-stage Dockerfile and workflows**

The Dockerfile builds with `mcr.microsoft.com/dotnet/sdk:8.0`, publishes `DoctorPayment.Api.csproj`, runs on `mcr.microsoft.com/dotnet/aspnet:8.0`, exposes `8080`, and starts `DoctorPayment.Api.dll`.

CI triggers on `main`, `develop`, `feature/**`, and pull requests to `main`/ `develop`; it runs restore, Release build, tests, and `docker build -t carenova-doctor-payment:ci-test .`.

CD triggers on the team-selected release branch, uses `docker/login-action@v3`, `docker/build-push-action@v6`, and `azure/webapps-deploy@v3`; all registry and Azure credentials are referenced only as GitHub secrets.

- [ ] **Step 4: Verify the image and workflow inputs**

Run: the Step 1 Docker health commands and `dotnet test DoctorPayment.sln --configuration Release`.

Expected: `/health` returns 200 and all tests pass.

- [ ] **Step 5: Commit delivery configuration**

```bash
git add Dockerfile .dockerignore .github/workflows
git commit -m "ci: add doctor payment build and deployment workflows"
```

