using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Wasta.Application;
using Wasta.Infrastructure;
using Wasta.Infrastructure.Identity;
using Wasta.WebApi;
using Wasta.WebApi.Auth;
using Wasta.WebApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWastaApplication();
builder.Services.AddWastaInfrastructure(builder.Configuration);

// Behind a load balancer RemoteIpAddress is the proxy, which would put every
// caller in one rate-limit bucket and log the wrong client address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    // Fail at startup, loudly. A missing key must never fall back to a default:
    // a predictable signing key means anyone can mint an admin token.
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 characters. Set it with "
        + "`dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\"` in development, or the "
        + "Jwt__SigningKey environment variable in production.");
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
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            RoleClaimType = ClaimTypes.Role,

            // Default is five minutes, which keeps an expired token working long
            // after it should have died.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddScoped<IAuthorizationHandler, VerifiedCompanyHandler>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.SeekerOnly, p => p.RequireRole("Seeker"))
    .AddPolicy(Policies.CompanyOnly, p => p.RequireRole("Company"))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole("Admin"))
    .AddPolicy(Policies.VerifiedCompanyOnly, p =>
        p.RequireRole("Company").AddRequirements(new VerifiedCompanyRequirement()));

// Enums go out as names, not integers. A client switching on 1/2/3 is
// unreadable, and renumbering the enum would silently change the contract.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddExceptionHandler<Wasta.WebApi.DomainExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<
    Wasta.Application.Features.Localization.ICurrentLanguage,
    Wasta.WebApi.Localization.HttpCurrentLanguage>();

builder.Services.AddWastaRateLimiting(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Wasta API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the access token. Swagger adds the \"Bearer \" prefix.",
    });
    options.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

// One handler for everything unhandled. Stack traces stop here.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Reference data and a placeholder assessment, so the flow is exercisable
    // before real content exists. Idempotent, and Development-only: production
    // content is administered, not seeded on boot.
    using var scope = app.Services.CreateScope();
    var seedDb = scope.ServiceProvider.GetRequiredService<Wasta.Infrastructure.Persistence.WastaDbContext>();
    await Wasta.Infrastructure.Persistence.DatabaseSeeder.SeedAsync(seedDb);

    // Only when both are configured. No default admin, ever.
    await Wasta.Infrastructure.Persistence.DatabaseSeeder.SeedAdminAsync(
        seedDb,
        scope.ServiceProvider.GetRequiredService<Wasta.Application.Abstractions.IPasswordHasher>(),
        builder.Configuration["Seed:AdminEmail"],
        builder.Configuration["Seed:AdminPassword"]);
}

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so per-user and per-company partitions can read claims.
app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapAssessmentEndpoints();
app.MapJobEndpoints();
app.MapApplicationEndpoints();
app.MapTalentPoolEndpoints();
app.MapAdminEndpoints();
app.MapFileEndpoints();
app.MapNotificationEndpoints();
app.MapLocalizationEndpoints();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
    .WithTags("Health")
    .WithSummary("Liveness. Does not touch the database.");

app.MapGet("/health/ready", async (Wasta.Infrastructure.Persistence.WastaDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ready" })
            : Results.Problem("Database is unreachable.", statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithTags("Health")
    .WithSummary("Readiness. Verifies the database connection.");

app.Run();

/// <summary>Exposed so the integration tests can boot the real host.</summary>
public partial class Program;
