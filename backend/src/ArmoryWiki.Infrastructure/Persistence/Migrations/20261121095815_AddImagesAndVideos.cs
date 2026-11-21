using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArmoryWiki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImagesAndVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firearm_id = table.Column<int>(type: "integer", nullable: true),
                    person_id = table.Column<int>(type: "integer", nullable: true),
                    organization_id = table.Column<int>(type: "integer", nullable: true),
                    cartridge_id = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_extension = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    caption = table.Column<string>(type: "text", nullable: true),
                    author = table.Column<string>(type: "text", nullable: false),
                    license = table.Column<string>(type: "text", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    commons_file_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_images", x => x.id);
                    table.CheckConstraint("ck_images_exactly_one_subject", "num_nonnulls(firearm_id, person_id, organization_id, cartridge_id) = 1");
                    table.ForeignKey(
                        name: "fk_images_cartridges_cartridge_id",
                        column: x => x.cartridge_id,
                        principalTable: "cartridges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_images_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_images_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_images_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "videos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firearm_id = table.Column<int>(type: "integer", nullable: false),
                    you_tube_video_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_videos", x => x.id);
                    table.ForeignKey(
                        name: "fk_videos_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_images_cartridge_id",
                table: "images",
                column: "cartridge_id");

            migrationBuilder.CreateIndex(
                name: "ix_images_commons_file_name",
                table: "images",
                column: "commons_file_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_images_content_hash",
                table: "images",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_images_firearm_id",
                table: "images",
                column: "firearm_id");

            migrationBuilder.CreateIndex(
                name: "ix_images_organization_id",
                table: "images",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_images_person_id",
                table: "images",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_videos_firearm_id_you_tube_video_id",
                table: "videos",
                columns: new[] { "firearm_id", "you_tube_video_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "images");

            migrationBuilder.DropTable(
                name: "videos");
        }
    }
}
