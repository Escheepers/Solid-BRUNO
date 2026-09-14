using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// EF Core composition root for the Postgres database. DbSets for Vehicle (unique index on
/// RegistrationNumber) and Customer (PII columns transparently encrypted -- see below), each with a
/// global query filter (AD-13) so soft-deleted rows never surface through any EF Core query anywhere
/// in the app, present or future. Additional aggregates arrive with their own stories.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// The Data Protection purpose string scoping every key derived for PII encryption -- a fixed,
    /// stable value (changing it would make every previously-encrypted row unreadable).
    /// </summary>
    private const string PiiProtectorPurpose = "BrunoVehicleHire.PII";

    private readonly IDataProtector _piiProtector;

    public AppDbContext(DbContextOptions<AppDbContext> options, IDataProtectionProvider dataProtectionProvider)
        : base(options)
    {
        _piiProtector = dataProtectionProvider.CreateProtector(PiiProtectorPurpose);
    }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Customer> Customers => Set<Customer>();

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

        // AD-12's first real implementation: Email/PhoneNumber are encrypted transparently via a
        // ValueConverter backed by the Data Protection API -- Customer (Domain) never knows this
        // happens. Data Protection is non-deterministic (a random IV per call), so the actual unique
        // constraint for "Email unique" lives on the EmailHash shadow property (a deterministic
        // SHA-256 of the normalized email, computed in SaveChangesAsync below) instead of on the
        // encrypted Email column itself, which could never enforce it correctly.
        var piiConverter = new ValueConverter<string, string>(
            plaintext => _piiProtector.Protect(plaintext),
            ciphertext => _piiProtector.Unprotect(ciphertext));

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.FirstName)
                .IsRequired();

            entity.Property(c => c.LastName)
                .IsRequired();

            entity.Property(c => c.Email)
                .IsRequired()
                .HasConversion(piiConverter);

            entity.Property(c => c.PhoneNumber)
                .IsRequired()
                .HasConversion(piiConverter);

            // Persistence-only uniqueness sidecar -- see EmailHasher's and this class's doc comments.
            // Customer (Domain) has no property for this; it exists purely as an EF Core shadow
            // property, never exposed outside Infrastructure.
            entity.Property<string>("EmailHash")
                .IsRequired();

            entity.HasIndex("EmailHash")
                .IsUnique();

            // AD-13: mirrors Vehicle's exact query filter pattern. IsAnonymized customers stay in
            // default listings (per domain-model.md) -- this filter is deliberately IsDeleted-only.
            entity.HasQueryFilter(c => !c.IsDeleted);
        });
    }

    /// <summary>
    /// Computes and sets the EmailHash shadow property for every added/modified Customer BEFORE
    /// calling the base implementation -- this is the one place that still has both the plaintext
    /// Email (readable here, before the value converter transforms it for the parameter) and direct
    /// access to the shadow property. See spec-3-1's Design Notes for why this can't live in
    /// Customer.Create() instead.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetEmailHashesForPendingCustomers();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        SetEmailHashesForPendingCustomers();
        return base.SaveChanges();
    }

    private void SetEmailHashesForPendingCustomers()
    {
        foreach (var entry in ChangeTracker.Entries<Customer>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property("EmailHash").CurrentValue = EmailHasher.Compute(entry.Entity.Email);
            }
        }
    }
}
