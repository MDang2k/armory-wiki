using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmoryWiki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameMuzzleVelocityToMps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "muzzle_velocity_ms",
                table: "variants",
                newName: "muzzle_velocity_mps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "muzzle_velocity_mps",
                table: "variants",
                newName: "muzzle_velocity_ms");
        }
    }
}
