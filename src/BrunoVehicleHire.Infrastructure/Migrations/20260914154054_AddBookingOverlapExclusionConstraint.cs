using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrunoVehicleHire.Infrastructure.Migrations
{
    /// <summary>
    /// spec-4-2's database-level overlap backstop (AD-7's layer 2): a Postgres <c>EXCLUDE USING
    /// GIST</c> constraint on "Bookings" that rejects any insert/update whose ("VehicleId",
    /// daterange("StartDate", "EndDate")) pair overlaps another non-Cancelled row for the same
    /// vehicle. This codebase's first raw-SQL migration -- EF Core's fluent API has no representation
    /// for an exclusion constraint, so `dotnet ef migrations add` scaffolded this file empty; every
    /// statement below is hand-written. <c>daterange(start, end)</c>'s default bounds are
    /// <c>[start, end)</c> -- inclusive start, exclusive end -- which is exactly
    /// <see cref="BrunoVehicleHire.Domain.DateRange"/>'s own half-open semantics, so a same-day
    /// turnover (one booking's EndDate equal to another's StartDate) never trips this constraint.
    /// The btree_gist extension supplies the "=" operator class GiST needs for the uuid "VehicleId"
    /// column -- the range "&amp;&amp;" (overlap) operator is natively GiST-indexable on its own, but
    /// combining it with an equality column in the same exclusion constraint needs btree_gist's
    /// operator classes for every non-range column in the list.
    /// </summary>
    public partial class AddBookingOverlapExclusionConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                """);

            // WHERE "Status" <> 'Cancelled' mirrors AD-7's own clause verbatim (spec-4-2's
            // Boundaries): a Completed booking's historical date range still blocks a new
            // overlapping booking for that vehicle, but a Cancelled one never does.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Bookings"
                    ADD CONSTRAINT "EX_Bookings_VehicleId_DateRange_NoOverlap"
                    EXCLUDE USING gist (
                        "VehicleId" WITH =,
                        daterange("StartDate", "EndDate") WITH &&
                    )
                    WHERE ("Status" <> 'Cancelled');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drops the constraint only -- deliberately leaves btree_gist installed (spec-4-2's Code
            // Map): the extension is a harmless, shared database-level object that other constraints
            // could depend on later, and DROP EXTENSION is outside this migration's own concern.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Bookings" DROP CONSTRAINT "EX_Bookings_VehicleId_DateRange_NoOverlap";
                """);
        }
    }
}
