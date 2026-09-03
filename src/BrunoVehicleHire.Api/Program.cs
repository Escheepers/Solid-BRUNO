using BrunoVehicleHire.Api.Auth;
using BrunoVehicleHire.Api.ExceptionHandling;
using BrunoVehicleHire.Domain.Exceptions;
using BrunoVehicleHire.Infrastructure.Persistence;
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

builder.Services.AddControllers();

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

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

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
