using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add`/`database update` construct <see cref="AppDbContext"/>
/// without invoking the Api's full composition root (Program.cs) at design time. The connection
/// string here only needs to be syntactically valid for the Npgsql provider to generate SQL --
/// it is never used at application runtime; Program.cs registers AppDbContext from real
/// configuration (appsettings.json / environment) instead.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=brunovehiclehire;Username=bruno;Password=bruno_dev_password");

        return new AppDbContext(optionsBuilder.Options);
    }
}
