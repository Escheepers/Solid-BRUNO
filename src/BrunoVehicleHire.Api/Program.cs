using System.Diagnostics;
using System.Reflection;
using BrunoVehicleHire.Api.Auth;
using BrunoVehicleHire.Api.ExceptionHandling;
using BrunoVehicleHire.Api.Logging;
using BrunoVehicleHire.Api.Swagger;
using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Queries;
using BrunoVehicleHire.Domain.Exceptions;
using BrunoVehicleHire.Infrastructure.BackgroundServices;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Repositories;
using BrunoVehicleHire.Infrastructure.Seeding;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

// spec-6-3: Serilog replaces the default ASP.NET Core logging provider -- a drop-in for every
// existing ILogger<T> call site (e.g. BookingCompletionSweepService's error log), no call site
// changes needed. Clear the default Console/Debug/EventSource providers WebApplication.
// CreateBuilder() already registered -- otherwise, combined with `writeToProviders: true` below,
// every event would print twice (once via Serilog's own Console sink, once forwarded back through
// the still-registered default Console provider).
builder.Logging.ClearProviders();

// Console logging is unconditional; the Loki sink is only added when Serilog:Loki:Enabled is true
// (default false), so a plain `dotnet run` with no observability stack running produces zero
// connection-refused retry noise.
// `writeToProviders: true` additionally routes every event through any standard
// Microsoft.Extensions.Logging ILoggerProvider registered via builder.Logging -- none remains
// registered by the app itself after ClearProviders() above, but it lets the PII-in-logs test
// (CustomerLoggingPiiTests) attach its own in-memory ILoggerProvider via WebApplicationFactory and
// observe every event a real request produces, without Program.cs needing any test-specific
// knowledge.
builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    if (context.Configuration.GetValue<bool>("Serilog:Loki:Enabled"))
    {
        var lokiUrl = context.Configuration["Serilog:Loki:Url"] ?? "http://localhost:3100";
        loggerConfiguration.WriteTo.GrafanaLoki(
            lokiUrl,
            labels: [new LokiLabel { Key = "app", Value = "bruno-vehicle-hire-api" }]);
    }
}, writeToProviders: true);

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

// spec-6-1: registered unconditionally (cheap -- it's just a class), but only ever RESOLVED and
// CALLED below when Seed:Enabled is true. Scoped to match AppDbContext's own lifetime, since
// DatabaseSeeder takes a constructor-injected AppDbContext.
builder.Services.AddScoped<ISeeder, DatabaseSeeder>();

// AD-17: BookingCompletionSweepService is a singleton BackgroundService, so its constructor-injected
// TimeProvider must be resolvable from the root container -- registered once, here, as the real
// system clock (tests that need a fixed clock call RunSweepAsync directly against their own instance,
// never through this registration).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<BookingCompletionSweepOptions>(
    builder.Configuration.GetSection("BookingCompletionSweep"));
builder.Services.AddHostedService<BookingCompletionSweepService>();

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

    // spec-swagger-examples: surfaces the XML doc comments already written throughout the codebase
    // (controllers/handlers/commands) as real Swagger descriptions. Both the Api's own XML file and
    // the Application project's (where the Commands/DTOs actually live) are needed -- Application's
    // is copied alongside Api's into the same output directory via the existing ProjectReference, so
    // it's found at the same AppContext.BaseDirectory. `includeControllerXmlComments: true` on the
    // Api file surfaces controller action <summary> text as operation descriptions.
    var apiXmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, apiXmlFile), includeControllerXmlComments: true);

    var applicationXmlFile = $"{typeof(GetVehiclesQuery).Assembly.GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, applicationXmlFile));

    // Provides realistic example JSON for the three Create commands + response DTOs (Design Notes:
    // an ISchemaFilter rather than XML <example> tags, since the Commands are positional records).
    options.SchemaFilter<ExampleSchemaFilter>();
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

    // spec-6-1: gated by the Seed:Enabled CONFIG flag, not environment name (Design Notes) -- every
    // existing WebApplicationFactory<Program>-based integration test already runs under
    // "Development", the same environment name a real evaluator's `dotnet run` uses, so environment
    // name alone can't tell "real local run" apart from "test run". Each of those test files
    // overrides Seed:Enabled to false the same way they already override ConnectionStrings:Postgres/
    // ApiKey:Key. appsettings.json defaults this to false; appsettings.Development.json turns it on.
    if (builder.Configuration.GetValue<bool>("Seed:Enabled"))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<ISeeder>();
        seeder.SeedIfEmptyAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}

// Configure the HTTP request pipeline.
// UseSerilogRequestLogging() is the very first middleware so its timer and final logged status
// code span the entire pipeline, including whatever GlobalExceptionHandler rewrites the response
// to for a caught exception -- the source of the dashboard's request-rate panel (spec-6-3).
// The request line's level follows its outcome (RequestLogLevel): 5xx -> Error, 4xx (e.g. a business-rule
// 409) -> Warning, otherwise Information -- so Grafana colours it red / amber / green.
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (context, httpContext) => context.Set("TraceId", Activity.Current?.TraceId.ToString());
    options.GetLevel = (httpContext, _, exception) => RequestLogLevel.For(httpContext.Response.StatusCode, exception);
});

// UseExceptionHandler() is the first exception-catching middleware (AD-8/Story 1.5) so
// GlobalExceptionHandler can catch exceptions thrown by any later middleware, not just endpoint
// handlers.
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
