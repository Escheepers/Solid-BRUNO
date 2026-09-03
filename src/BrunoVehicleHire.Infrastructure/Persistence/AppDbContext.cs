using BrunoVehicleHire.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// EF Core composition root for the Postgres database. One DbSet (Vehicle) with a unique index on
/// RegistrationNumber, plus a global query filter (AD-13) so soft-deleted vehicles never surface
/// through any EF Core query anywhere in the app, present or future. Additional aggregates arrive
/// with their own stories.
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

            // AD-13: soft-deleted vehicles never surface through EF Core queries anywhere in the
            // app -- query-time only, so this needs no new migration.
            entity.HasQueryFilter(v => !v.IsDeleted);
        });
    }
}
