using BrunoVehicleHire.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
