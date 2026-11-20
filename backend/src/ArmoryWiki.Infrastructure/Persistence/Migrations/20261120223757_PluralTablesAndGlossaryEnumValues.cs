using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmoryWiki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PluralTablesAndGlossaryEnumValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_adoption_countries_country_id",
                table: "adoption");

            migrationBuilder.DropForeignKey(
                name: "fk_adoption_firearms_firearm_id",
                table: "adoption");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_designer_firearms_firearm_id",
                table: "firearm_designer");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_designer_organizations_organization_id",
                table: "firearm_designer");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_designer_people_person_id",
                table: "firearm_designer");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_manufacturer_firearms_firearm_id",
                table: "firearm_manufacturer");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_manufacturer_organizations_organization_id",
                table: "firearm_manufacturer");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_tag_firearms_firearm_id",
                table: "firearm_tag");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_tag_tags_tag_id",
                table: "firearm_tag");

            migrationBuilder.DropForeignKey(
                name: "fk_variant_cartridge_cartridges_cartridge_id",
                table: "variant_cartridge");

            migrationBuilder.DropForeignKey(
                name: "fk_variant_cartridge_variants_variant_id",
                table: "variant_cartridge");

            migrationBuilder.DropPrimaryKey(
                name: "pk_variant_cartridge",
                table: "variant_cartridge");

            migrationBuilder.DropPrimaryKey(
                name: "pk_firearm_tag",
                table: "firearm_tag");

            migrationBuilder.DropPrimaryKey(
                name: "pk_firearm_manufacturer",
                table: "firearm_manufacturer");

            migrationBuilder.DropPrimaryKey(
                name: "pk_firearm_designer",
                table: "firearm_designer");

            migrationBuilder.DropPrimaryKey(
                name: "pk_adoption",
                table: "adoption");

            migrationBuilder.RenameTable(
                name: "variant_cartridge",
                newName: "variant_cartridges");

            migrationBuilder.RenameTable(
                name: "firearm_tag",
                newName: "firearm_tags");

            migrationBuilder.RenameTable(
                name: "firearm_manufacturer",
                newName: "firearm_manufacturers");

            migrationBuilder.RenameTable(
                name: "firearm_designer",
                newName: "firearm_designers");

            migrationBuilder.RenameTable(
                name: "adoption",
                newName: "adoptions");

            migrationBuilder.RenameIndex(
                name: "ix_variant_cartridge_cartridge_id",
                table: "variant_cartridges",
                newName: "ix_variant_cartridges_cartridge_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_tag_tag_id",
                table: "firearm_tags",
                newName: "ix_firearm_tags_tag_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_manufacturer_organization_id",
                table: "firearm_manufacturers",
                newName: "ix_firearm_manufacturers_organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_manufacturer_firearm_id",
                table: "firearm_manufacturers",
                newName: "ix_firearm_manufacturers_firearm_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_designer_person_id",
                table: "firearm_designers",
                newName: "ix_firearm_designers_person_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_designer_organization_id",
                table: "firearm_designers",
                newName: "ix_firearm_designers_organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_designer_firearm_id_person_id_organization_id_role",
                table: "firearm_designers",
                newName: "ix_firearm_designers_firearm_id_person_id_organization_id_role");

            migrationBuilder.RenameIndex(
                name: "ix_adoption_firearm_id",
                table: "adoptions",
                newName: "ix_adoptions_firearm_id");

            migrationBuilder.RenameIndex(
                name: "ix_adoption_country_id",
                table: "adoptions",
                newName: "ix_adoptions_country_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_variant_cartridges",
                table: "variant_cartridges",
                columns: new[] { "variant_id", "cartridge_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_firearm_tags",
                table: "firearm_tags",
                columns: new[] { "firearm_id", "tag_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_firearm_manufacturers",
                table: "firearm_manufacturers",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_firearm_designers",
                table: "firearm_designers",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_adoptions",
                table: "adoptions",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_adoptions_countries_country_id",
                table: "adoptions",
                column: "country_id",
                principalTable: "countries",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_adoptions_firearms_firearm_id",
                table: "adoptions",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_designers_firearms_firearm_id",
                table: "firearm_designers",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_designers_organizations_organization_id",
                table: "firearm_designers",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_designers_people_person_id",
                table: "firearm_designers",
                column: "person_id",
                principalTable: "people",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_manufacturers_firearms_firearm_id",
                table: "firearm_manufacturers",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_manufacturers_organizations_organization_id",
                table: "firearm_manufacturers",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_tags_firearms_firearm_id",
                table: "firearm_tags",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_tags_tags_tag_id",
                table: "firearm_tags",
                column: "tag_id",
                principalTable: "tags",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_variant_cartridges_cartridges_cartridge_id",
                table: "variant_cartridges",
                column: "cartridge_id",
                principalTable: "cartridges",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_variant_cartridges_variants_variant_id",
                table: "variant_cartridges",
                column: "variant_id",
                principalTable: "variants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Hand-added. Enums are stored as strings, so values renamed for GLOSSARY.md are rewritten in place:
            // FiringModes.Single -> SemiAutomatic (manually operated firearms have no firing modes),
            // FeedSystem.Single -> SingleShot, DesignerRole.Refinement -> Developer.
            migrationBuilder.Sql("""
                UPDATE variants
                SET firing_modes = regexp_replace(firing_modes, '\mSingle\M', 'SemiAutomatic', 'g')
                WHERE firing_modes ~ '\mSingle\M';

                UPDATE variants AS v
                SET firing_modes = 'None'
                FROM firearms AS f
                JOIN operating_mechanisms AS m ON m.id = f.operating_mechanism_id
                WHERE f.id = v.firearm_id AND m.path LIKE '/manual/%';

                UPDATE firearms SET feed_system = 'SingleShot' WHERE feed_system = 'Single';

                UPDATE firearm_designers SET role = 'Developer' WHERE role = 'Refinement';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hand-added, runs before the tables get their old names back.
            // Manually operated firearms keep 'None'; their old 'Single' value is not restored.
            migrationBuilder.Sql("""
                UPDATE variants
                SET firing_modes = regexp_replace(firing_modes, '\mSemiAutomatic\M', 'Single', 'g')
                WHERE firing_modes ~ '\mSemiAutomatic\M';

                UPDATE firearms SET feed_system = 'Single' WHERE feed_system = 'SingleShot';

                UPDATE firearm_designers SET role = 'Refinement' WHERE role = 'Developer';
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_adoptions_countries_country_id",
                table: "adoptions");

            migrationBuilder.DropForeignKey(
                name: "fk_adoptions_firearms_firearm_id",
                table: "adoptions");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_designers_firearms_firearm_id",
                table: "firearm_designers");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_designers_organizations_organization_id",
                table: "firearm_designers");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_designers_people_person_id",
                table: "firearm_designers");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_manufacturers_firearms_firearm_id",
                table: "firearm_manufacturers");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_manufacturers_organizations_organization_id",
                table: "firearm_manufacturers");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_tags_firearms_firearm_id",
                table: "firearm_tags");

            migrationBuilder.DropForeignKey(
                name: "fk_firearm_tags_tags_tag_id",
                table: "firearm_tags");

            migrationBuilder.DropForeignKey(
                name: "fk_variant_cartridges_cartridges_cartridge_id",
                table: "variant_cartridges");

            migrationBuilder.DropForeignKey(
                name: "fk_variant_cartridges_variants_variant_id",
                table: "variant_cartridges");

            migrationBuilder.DropPrimaryKey(
                name: "pk_variant_cartridges",
                table: "variant_cartridges");

            migrationBuilder.DropPrimaryKey(
                name: "pk_firearm_tags",
                table: "firearm_tags");

            migrationBuilder.DropPrimaryKey(
                name: "pk_firearm_manufacturers",
                table: "firearm_manufacturers");

            migrationBuilder.DropPrimaryKey(
                name: "pk_firearm_designers",
                table: "firearm_designers");

            migrationBuilder.DropPrimaryKey(
                name: "pk_adoptions",
                table: "adoptions");

            migrationBuilder.RenameTable(
                name: "variant_cartridges",
                newName: "variant_cartridge");

            migrationBuilder.RenameTable(
                name: "firearm_tags",
                newName: "firearm_tag");

            migrationBuilder.RenameTable(
                name: "firearm_manufacturers",
                newName: "firearm_manufacturer");

            migrationBuilder.RenameTable(
                name: "firearm_designers",
                newName: "firearm_designer");

            migrationBuilder.RenameTable(
                name: "adoptions",
                newName: "adoption");

            migrationBuilder.RenameIndex(
                name: "ix_variant_cartridges_cartridge_id",
                table: "variant_cartridge",
                newName: "ix_variant_cartridge_cartridge_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_tags_tag_id",
                table: "firearm_tag",
                newName: "ix_firearm_tag_tag_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_manufacturers_organization_id",
                table: "firearm_manufacturer",
                newName: "ix_firearm_manufacturer_organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_manufacturers_firearm_id",
                table: "firearm_manufacturer",
                newName: "ix_firearm_manufacturer_firearm_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_designers_person_id",
                table: "firearm_designer",
                newName: "ix_firearm_designer_person_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_designers_organization_id",
                table: "firearm_designer",
                newName: "ix_firearm_designer_organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_firearm_designers_firearm_id_person_id_organization_id_role",
                table: "firearm_designer",
                newName: "ix_firearm_designer_firearm_id_person_id_organization_id_role");

            migrationBuilder.RenameIndex(
                name: "ix_adoptions_firearm_id",
                table: "adoption",
                newName: "ix_adoption_firearm_id");

            migrationBuilder.RenameIndex(
                name: "ix_adoptions_country_id",
                table: "adoption",
                newName: "ix_adoption_country_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_variant_cartridge",
                table: "variant_cartridge",
                columns: new[] { "variant_id", "cartridge_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_firearm_tag",
                table: "firearm_tag",
                columns: new[] { "firearm_id", "tag_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_firearm_manufacturer",
                table: "firearm_manufacturer",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_firearm_designer",
                table: "firearm_designer",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_adoption",
                table: "adoption",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_adoption_countries_country_id",
                table: "adoption",
                column: "country_id",
                principalTable: "countries",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_adoption_firearms_firearm_id",
                table: "adoption",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_designer_firearms_firearm_id",
                table: "firearm_designer",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_designer_organizations_organization_id",
                table: "firearm_designer",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_designer_people_person_id",
                table: "firearm_designer",
                column: "person_id",
                principalTable: "people",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_manufacturer_firearms_firearm_id",
                table: "firearm_manufacturer",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_manufacturer_organizations_organization_id",
                table: "firearm_manufacturer",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_tag_firearms_firearm_id",
                table: "firearm_tag",
                column: "firearm_id",
                principalTable: "firearms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_firearm_tag_tags_tag_id",
                table: "firearm_tag",
                column: "tag_id",
                principalTable: "tags",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_variant_cartridge_cartridges_cartridge_id",
                table: "variant_cartridge",
                column: "cartridge_id",
                principalTable: "cartridges",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_variant_cartridge_variants_variant_id",
                table: "variant_cartridge",
                column: "variant_id",
                principalTable: "variants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
