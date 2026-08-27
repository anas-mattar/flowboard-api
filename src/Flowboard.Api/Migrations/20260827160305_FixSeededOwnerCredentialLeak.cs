using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixSeededOwnerCredentialLeak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately NOT the EF-scaffolded default (which would restore the leaked,
            // publicly-known password hash this migration exists to remove). Rolling back
            // must not resurrect a known-compromised credential; the sentinel is a safe no-op.
            migrationBuilder.UpdateData(
                table: "User",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$12$DISABLED.NO.CREDENTIAL.SHIPPED.IN.THIS.MIGRATION.SEED");
        }
    }
}
