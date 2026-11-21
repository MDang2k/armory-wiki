using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmoryWiki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceSeedLogAndRowVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "tags",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "people",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "organizations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "operating_mechanisms",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "countries",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "cartridges",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "reference_seed_log",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    seeded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_seed_log", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_seed_log");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "people");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "operating_mechanisms");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "cartridges");
        }
    }
}
