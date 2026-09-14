using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add`/`database update` construct <see cref="AppDbContext"/>
/// without invoking the Api's full composition root (Program.cs) at design time. The connection
/// string here only needs to be syntactically valid for the Npgsql provider to generate SQL --
/// it is never used at application runtime; Program.cs registers AppDbContext from real
/// configuration (appsettings.json / environment) instead. Likewise, the
/// <see cref="IDataProtectionProvider"/> supplied here only needs to be non-null so
/// <see cref="AppDbContext"/>'s constructor can create its PII protector -- migrations generate
/// schema, never encrypt/decrypt real data, so keys persisted to a throwaway temp directory (rather
/// than Program.cs's real, docker-volume-backed key ring) are fine here.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=brunovehiclehire;Username=bruno;Password=bruno_dev_password");

        var designTimeKeyDirectory = new DirectoryInfo(
            Path.Combine(Path.GetTempPath(), "BrunoVehicleHire.DesignTimeDataProtectionKeys"));
        var dataProtectionProvider = DataProtectionProvider.Create(designTimeKeyDirectory);

        return new AppDbContext(optionsBuilder.Options, dataProtectionProvider);
    }
}
