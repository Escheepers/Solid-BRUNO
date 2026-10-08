using System.Security.Cryptography;
using System.Text;

namespace BrunoVehicleHire.Infrastructure.Helpers;

/// <summary>
/// Computes the deterministic SHA-256 hash of a normalized email address, used solely as
/// <c>AppDbContext</c>'s <c>EmailHash</c> shadow-property value -- a persistence-layer sidecar that
/// lets a DB-level unique index enforce "Email unique" even though the actual <c>Email</c> column is
/// encrypted (via ASP.NET Core's Data Protection API, which is non-deterministic and would never
/// produce matching ciphertext for the same plaintext twice). This is the ONE place the hash is
/// computed -- both <c>AppDbContext.SaveChangesAsync</c>'s override and
/// <c>CustomerRepository.ExistsByEmailAsync</c> call this method, never duplicating the logic (DRY).
/// Normalizes via <c>Trim().ToLowerInvariant()</c> first so "Jane@Example.com" and
/// " jane@example.com " -- the same real-world address -- hash identically (spec-3-1's Design Notes).
/// </summary>
public static class EmailHasher
{
    public static string Compute(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }
}
