using BrunoVehicleHire.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// EF Core composition root for the Postgres database. Schema-only for this story:
/// one DbSet (Vehicle) with a unique index on RegistrationNumber. Repositories,
/// query filters, and additional aggregates arrive with their own stories.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("Vehicles");

            entity.HasKey(v => v.Id);

            entity.Property(v => v.RegistrationNumber)
                .IsRequired();

            entity.HasIndex(v => v.RegistrationNumber)
                .IsUnique();

            entity.Property(v => v.Make)
                .IsRequired();

            entity.Property(v => v.Model)
                .IsRequired();

            entity.Property(v => v.DailyRate)
                .HasColumnType("numeric(18,2)");
        });
    }
}
