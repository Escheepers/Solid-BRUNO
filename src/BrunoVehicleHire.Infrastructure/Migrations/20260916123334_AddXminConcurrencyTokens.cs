using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrunoVehicleHire.Infrastructure.Migrations
{
    /// <summary>
    /// A deliberately empty (no-op) migration. Scaffolding this migration (via `dotnet ef migrations
    /// add`) is required purely to reconcile EF Core's own model snapshot -- mapping Vehicle/Booking's
    /// new shadow "xmin" concurrency-token property (see AppDbContext's Vehicle/Booking
    /// configuration) trips the PendingModelChangesWarning-as-error EF Core 9+ enables by default,
    /// even though "xmin" is Postgres' own built-in system column, already present on every row of
    /// every table since table creation. The scaffolder's default Up()/Down() bodies (deleted here)
    /// tried to physically `ADD COLUMN "xmin"`/`DROP COLUMN "xmin"` -- Postgres rejects that outright
    /// ("column name "xmin" conflicts with a system column name") since user code can never create or
    /// drop a real column with that reserved name. No DDL is needed or possible here; this migration
    /// exists only to make EF Core's change-tracking aware its own model is once again in sync with
    /// the last-recorded snapshot.
    /// </summary>
    public partial class AddXminConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
