using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmoryWiki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantWikidataId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "wikidata_id",
                table: "variants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_variants_wikidata_id",
                table: "variants",
                column: "wikidata_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_variants_wikidata_id",
                table: "variants");

            migrationBuilder.DropColumn(
                name: "wikidata_id",
                table: "variants");
        }
    }
}
