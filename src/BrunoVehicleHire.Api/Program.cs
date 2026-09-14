using BrunoVehicleHire.Api.Auth;
using BrunoVehicleHire.Api.ExceptionHandling;
using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Queries;
using BrunoVehicleHire.Domain.Exceptions;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKey"));

builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, options => { });

// A global FallbackPolicy requires every endpoint to authenticate with the ApiKey scheme unless
// explicitly marked [AllowAnonymous] -- no per-endpoint [Authorize] attribute is needed anywhere.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(ApiKeyDefaults.Scheme)
        .RequireAuthenticatedUser()
        .Build();
});

// AD-8: RFC 9457 ProblemDetails for every unhandled exception, produced by GlobalExceptionHandler
// and written via the built-in IProblemDetailsService.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// BookingDto.Status (BookingStatus) is the first enum ever exposed on this API's wire shape --
// serialize it by name ("Active"), not its numeric ordinal, so it round-trips human-readably and
// stays insertion-order-independent if the enum's members are ever reordered, mirroring how
// AppDbContext.OnModelCreating already stores it (HasConversion<string>()) at the persistence layer.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// The first real CQRS slice (Story 1.7): MediatR pipeline with the shared ValidationBehavior
// (AD-10) registered as an open behavior so every current and future request gets its registered
// FluentValidation validators run before the handler executes. The MediatR Community license key
// is read from configuration (dotnet user-secrets in Development) -- never committed to
// appsettings.json.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(GetVehiclesQuery).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.LicenseKey = builder.Configuration["MediatR:LicenseKey"];
});

// FluentValidation.DependencyInjectionExtensions -- never FluentValidation.AspNetCore (AD-10
// explicitly names it deprecated).
builder.Services.AddValidatorsFromAssembly(typeof(GetVehiclesQuery).Assembly);

builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Swashbuckle: declares the ApiKey header security scheme + a global security requirement so
// Swagger UI's "Authorize" dialog exercises the real enforcement pipeline (AD-11).
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(ApiKeyDefaults.Scheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyDefaults.HeaderName,
        Scheme = ApiKeyDefaults.Scheme,
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(ApiKeyDefaults.Scheme, document)] = new List<string>(),
    });
});

// Deliberately NOT captured into a local variable read eagerly here: builder.Configuration is
// Microsoft.Extensions.Configuration's ConfigurationManager, a live/mutable instance, and this
// options delegate isn't invoked until AppDbContext is actually resolved from DI (post-Build()).
// Reading it inside the delegate means it reflects the FULLY composed configuration -- including
// any source WebApplicationFactory.WithWebHostBuilder's ConfigureAppConfiguration layers on for
// integration tests. Capturing it into a variable evaluated at this point in top-level Program.cs
// (before Build()) would silently freeze in appsettings.json's value, invisible to any later
// config override -- exactly the ApiKey:Key option below avoids by binding lazily via IOptions.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.")));

// Data Protection keys are persisted to a file system path backed by the docker-compose-mounted
// volume, not the ephemeral in-container default, so encrypted data (Epic 3's PII value
// converter) survives a `docker-compose down`/`up` cycle.
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"] ?? "./dataprotection-keys";
var keyRingDirectory = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, keyRingPath));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keyRingDirectory);

var app = builder.Build();

// Apply any pending EF Core migrations on startup so a fresh Postgres instance reaches the
// current schema with a single `docker-compose up` + `dotnet run` -- no manual migration step.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
// UseExceptionHandler() is the first pipeline middleware (AD-8/Story 1.5) so GlobalExceptionHandler
// can catch exceptions thrown by any later middleware, not just endpoint handlers.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsEnvironment("Testing"))
{
    // Diagnostic-only endpoints so Story 1.5's own integration tests can exercise the full
    // pipeline (auth + GlobalExceptionHandler) against a real HTTP request. Mapped only under the
    // "Testing" environment name, which the real app is never started with (Development/
    // Production only) -- zero Production/Development attack surface. Still mapped after
    // UseAuthentication()/UseAuthorization() so the global FallbackPolicy (Story 1.4) protects
    // them like every other endpoint.
    app.MapGet("/api/test/throw-domain-rule", () =>
    {
        throw new DomainRuleViolationException("Vehicle", "Year", "Year must be between 1900 and next year.");
    });

    app.MapGet("/api/test/throw-unhandled", () =>
    {
        throw new InvalidOperationException("some internal detail that must never reach the client");
    });
}

app.Run();

// Marker type so WebApplicationFactory<Program> (used by integration tests) can reference this
// top-level-statements Program class, which is otherwise generated as internal-only.
public partial class Program { }
