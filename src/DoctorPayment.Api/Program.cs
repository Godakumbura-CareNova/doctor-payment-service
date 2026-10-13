using System.Text;
using DoctorPayment.Api.Clients;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.DTOs.FeeChangeRequest;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services;
using DoctorPayment.Api.Services.Interfaces;
using DoctorPayment.Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("JwtSettings"));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("JwtSettings configuration is missing.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
{
    throw new InvalidOperationException("JwtSettings:Secret must be configured through user secrets or environment variables.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddDoctorPaymentAuthorization();

builder.Services.AddScoped<IValidator<UpdateDoctorProfileRequestDto>, UpdateDoctorProfileValidator>();
builder.Services.AddScoped<IDoctorProfileRepository, DoctorProfileRepository>();
builder.Services.AddScoped<IDoctorProfileService, DoctorProfileService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<MedicinePaymentDashboardService>();
builder.Services.AddScoped<IValidator<DoctorSearchQueryDto>, DoctorSearchQueryValidator>();
builder.Services.AddScoped<IDoctorSearchRepository, DoctorSearchRepository>();
builder.Services.AddScoped<IDoctorSearchService, DoctorSearchService>();

builder.Services.AddScoped<IValidator<CreateAvailabilityRequestDto>, CreateAvailabilityValidator>();
builder.Services.AddScoped<IValidator<UpdateAvailabilitySlotRequestDto>, UpdateAvailabilitySlotValidator>();
builder.Services.AddScoped<IDoctorAvailabilityRepository, DoctorAvailabilityRepository>();
builder.Services.AddScoped<IDoctorAvailabilityService, DoctorAvailabilityService>();

builder.Services.AddScoped<IValidator<CreateFeeChangeRequestDto>, CreateFeeChangeRequestValidator>();
builder.Services.AddScoped<IValidator<RejectFeeChangeRequestDto>, RejectFeeChangeRequestValidator>();
builder.Services.AddScoped<IFeeChangeRequestRepository, FeeChangeRequestRepository>();
builder.Services.AddScoped<IFeeChangeRequestService, FeeChangeRequestService>();
builder.Services.AddScoped<IDoctorNotificationRepository, DoctorNotificationRepository>();
builder.Services.AddScoped<IDoctorNotificationService, DoctorNotificationService>();

builder.Services.AddOptions<MedicinePaymentSettings>()
    .Bind(builder.Configuration.GetSection("MedicinePayments"));
builder.Services.AddHttpClient<IPharmacyOrderClient, PharmacyOrderClient>(client =>
{
    // Orders are read from the pharmacy-order service; a missing URL surfaces as "order cannot be loaded".
    var baseUrl = builder.Configuration["PharmacyOrderService:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IValidator<SelectPaymentMethodRequestDto>, SelectPaymentMethodValidator>();
builder.Services.AddScoped<IMedicinePaymentRepository, MedicinePaymentRepository>();
builder.Services.AddScoped<IMedicinePaymentService, MedicinePaymentService>();
builder.Services.AddScoped<IMedicinePaymentTransactionRepository, MedicinePaymentTransactionRepository>();
builder.Services.AddScoped<IMedicinePaymentTransactionService, MedicinePaymentTransactionService>();

builder.Services.AddOptions<ReceiptStorageSettings>()
    .Bind(builder.Configuration.GetSection("ReceiptStorage"));
builder.Services.AddOptions<KafkaSettings>()
    .Bind(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IReceiptFileStorage, ReceiptFileStorage>();
builder.Services.AddScoped<IBankTransferReceiptRepository, BankTransferReceiptRepository>();
builder.Services.AddScoped<IBankTransferReceiptService, BankTransferReceiptService>();
builder.Services.AddScoped<IKafkaEventProducer, KafkaEventProducer>();
builder.Services.AddHostedService<OutboxPublisherService>();

// GCN-41: online payments through PayHere (its sandbox while testing). The merchant secret comes from user secrets or
// environment variables, never appsettings.
builder.Services.AddOptions<PayHereSettings>()
    .Bind(builder.Configuration.GetSection("PayHere"));
builder.Services.AddSingleton<IOnlinePaymentGateway, PayHerePaymentGateway>();
builder.Services.AddScoped<IOnlinePaymentRepository, OnlinePaymentRepository>();
builder.Services.AddScoped<IOnlinePaymentService, OnlinePaymentService>();
builder.Services.AddHostedService<KafkaConsumerService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 0))));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
