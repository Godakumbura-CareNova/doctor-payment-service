using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DoctorPayment.Api.Tests.Integration;

/// <summary>
/// Boots the real API (JWT auth, validation, controllers, EF) against an in-memory database and exercises the
/// patient doctor-search flow end to end: search, no matches, invalid criteria, view a profile, and the doctor-side save
/// that feeds the search index.
/// </summary>
public sealed class DoctorSearchEndpointTests : IClassFixture<DoctorSearchEndpointTests.DoctorSearchApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DoctorSearchApiFactory _factory;

    public DoctorSearchEndpointTests(DoctorSearchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_RequiresAuthentication()
    {
        var response = await _factory.CreateClient().GetAsync("/api/doctors?search=Nimal");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_IsForbidden_ForNonPatients()
    {
        var client = _factory.ClientFor(role: "Doctor", userId: Guid.NewGuid(), fullName: "Dr Someone");

        var response = await client.GetAsync("/api/doctors");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_ReturnsMatchingDoctors_ForAPatient()
    {
        var doctorId = await _factory.SeedDoctorAsync("Nimal Perera", "Cardiologist", openSlots: 1);
        var client = _factory.PatientClient();

        var response = await client.GetAsync("/api/doctors?search=nimal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DoctorSearchResultDto>(Json);
        Assert.NotNull(result);
        var hit = Assert.Single(result.Items, item => item.DoctorId == doctorId);
        Assert.Equal("Nimal Perera", hit.FullName);
        Assert.Equal("Cardiologist", hit.Specialization);
        Assert.True(hit.Availability.HasAvailableSlots);
    }

    [Fact]
    public async Task Search_BySpecializationFilter_ReturnsOnlyThatSpecialization()
    {
        await _factory.SeedDoctorAsync("Saman Kumara", "Paediatrician");
        await _factory.SeedDoctorAsync("Dilani Wijesinghe", "Neurologist");
        var client = _factory.PatientClient();

        var response = await client.GetAsync("/api/doctors?specialization=Paediatrician");

        var result = await response.Content.ReadFromJsonAsync<DoctorSearchResultDto>(Json);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, item => Assert.Equal("Paediatrician", item.Specialization));
    }

    [Fact]
    public async Task Search_WithNoMatches_ReturnsOkAndEmptyList()
    {
        var client = _factory.PatientClient();

        var response = await client.GetAsync("/api/doctors?search=Nobody%20Matches%20This");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DoctorSearchResultDto>(Json);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Search_WithInvalidCriteria_ReturnsBadRequestWithFieldErrors()
    {
        var client = _factory.PatientClient();

        var response = await client.GetAsync("/api/doctors?search=x&page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Please check your search criteria.", body.RootElement.GetProperty("message").GetString());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Search", out _));
        Assert.True(errors.TryGetProperty("Page", out _));
    }

    [Fact]
    public async Task Specializations_ListsDistinctValues()
    {
        await _factory.SeedDoctorAsync("Doc One", "Radiologist");
        await _factory.SeedDoctorAsync("Doc Two", "Radiologist");
        var client = _factory.PatientClient();

        var specializations = await client.GetFromJsonAsync<List<string>>("/api/doctors/specializations", Json);

        Assert.NotNull(specializations);
        Assert.Single(specializations, s => s == "Radiologist");
    }

    [Fact]
    public async Task GetProfile_ReturnsPublishedProfile_WithAvailability()
    {
        var doctorId = await _factory.SeedDoctorAsync("Kasun Rajapaksa", "Orthopaedic Surgeon", openSlots: 2);
        var client = _factory.PatientClient();

        var response = await client.GetAsync($"/api/doctors/{doctorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<DoctorPublicProfileDto>(Json);
        Assert.NotNull(profile);
        Assert.Equal("Kasun Rajapaksa", profile.FullName);
        Assert.Equal("Orthopaedic Surgeon", profile.Specialization);
        Assert.Single(profile.Qualifications);
        Assert.True(profile.Availability.HasAvailableSlots);
        Assert.Equal(2, profile.UpcomingSlots.Count);
    }

    [Fact]
    public async Task GetProfile_ReturnsNotFound_ForUnknownDoctor()
    {
        var client = _factory.PatientClient();

        var response = await client.GetAsync($"/api/doctors/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_RejectsNonGuidIds()
    {
        var client = _factory.PatientClient();

        var response = await client.GetAsync("/api/doctors/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DoctorSavingTheirProfile_MakesThemSearchableByTheirTokenName()
    {
        var doctorId = Guid.NewGuid();
        var doctorClient = _factory.ClientFor("Doctor", doctorId, "Tharindu Bandara");
        var request = new UpdateDoctorProfileRequestDto
        {
            Specialization = "Psychiatrist",
            RegistrationNumber = "SLMC-77001",
            Qualifications = { new QualificationDto { Degree = "MBBS", Institution = "University of Ruhuna", StartYear = 2009, EndYear = 2014 } }
        };

        var save = await doctorClient.PutAsJsonAsync("/api/doctors/me/profile", request);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var search = await _factory.PatientClient().GetFromJsonAsync<DoctorSearchResultDto>("/api/doctors?search=Tharindu", Json);

        Assert.NotNull(search);
        var hit = Assert.Single(search.Items, item => item.DoctorId == doctorId);
        Assert.Equal("Tharindu Bandara", hit.FullName);
        Assert.Equal("Psychiatrist", hit.Specialization);
        Assert.False(hit.Availability.HasAvailableSlots);
    }

    public sealed class DoctorSearchApiFactory : WebApplicationFactory<Program>
    {
        private const string Secret = "doctor-search-test-jwt-secret-that-is-at-least-32-characters";
        private const string Issuer = "CareNova.UserAuth";
        private const string Audience = "CareNova.Client";
        private readonly string _databaseName = $"DoctorSearchApi-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("JwtSettings:Secret", Secret);
            builder.UseSetting("JwtSettings:Issuer", Issuer);
            builder.UseSetting("JwtSettings:Audience", Audience);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;User=unused;Password=unused;");

            builder.ConfigureServices(services =>
            {
                // Swap the MySQL context for the in-memory provider; everything above the DbContext stays real.
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) || d.ServiceType == typeof(DbContextOptions)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }

        public HttpClient PatientClient() => ClientFor("Patient", Guid.NewGuid(), "Test Patient");

        public HttpClient ClientFor(string role, Guid userId, string fullName)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(role, userId, fullName));
            return client;
        }

        public async Task<Guid> SeedDoctorAsync(string fullName, string specialization, int openSlots = 0)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var profile = new DoctorProfile
            {
                DoctorId = Guid.NewGuid(),
                FullName = fullName,
                Specialization = specialization,
                RegistrationNumber = $"SLMC-{Random.Shared.Next(10000, 99999)}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Qualifications = { new DoctorQualification { Degree = "MBBS", Institution = "University of Colombo", StartYear = 2005, EndYear = 2010 } }
            };
            for (var i = 1; i <= openSlots; i++)
            {
                profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = now.AddDays(i), EndUtc = now.AddDays(i).AddMinutes(20) });
            }

            dbContext.DoctorProfiles.Add(profile);
            await dbContext.SaveChangesAsync();
            return profile.DoctorId;
        }

        // Mirrors the claims the user-auth TokenService issues, signed with the test secret.
        private static string IssueToken(string role, Guid userId, string fullName)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(Issuer, Audience, claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
