using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrunoVehicleHire.Infrastructure.Migrations
{
    /// <summary>
    /// A deliberately empty (no-op) migration, mirroring <c>AddXminConcurrencyTokens</c>: mapping
    /// Customer's new shadow "xmin" concurrency-token property (see AppDbContext's Customer
    /// configuration) changes EF Core's own model snapshot, and scaffolding this migration is required
    /// purely to keep that snapshot in sync (otherwise PendingModelChangesWarning-as-error crashes
    /// startup). "xmin" is Postgres' own built-in system column, already present on every row of every
    /// table; the scaffolder's default Up()/Down() bodies (deleted here) tried to physically
    /// `ADD COLUMN "xmin"`/`DROP COLUMN "xmin"`, which Postgres rejects outright ("column name "xmin"
    /// conflicts with a system column name"). No DDL is needed or possible.
    /// </summary>
    public partial class AddCustomerXminConcurrencyToken : Migration
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
