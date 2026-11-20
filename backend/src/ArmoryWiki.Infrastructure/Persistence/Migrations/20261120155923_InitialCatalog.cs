using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace ArmoryWiki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            // Hand-added: generated columns on firearms need an IMMUTABLE array_to_string
            // (the built-in one is only STABLE). Must exist before the firearms table.
            // The IMMUTABLE label is only honest because the signature is fixed to text[]:
            // array_to_string is STABLE solely for element types whose text output can vary.
            migrationBuilder.Sql("""
                CREATE FUNCTION armorywiki_immutable_array_to_string(text[]) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$ SELECT coalesce(array_to_string($1, ' '), '') $$;
                """);

            migrationBuilder.CreateTable(
                name: "countries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    iso_code = table.Column<string>(type: "text", nullable: true),
                    is_historical = table.Column<bool>(type: "boolean", nullable: false),
                    wikidata_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operating_mechanisms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    path = table.Column<string>(type: "text", nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operating_mechanisms", x => x.id);
                    table.ForeignKey(
                        name: "fk_operating_mechanisms_operating_mechanisms_parent_id",
                        column: x => x.parent_id,
                        principalTable: "operating_mechanisms",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "slug_redirects",
                columns: table => new
                {
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    old_slug = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slug_redirects", x => new { x.entity_type, x.old_slug });
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    group = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cartridges",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    alternative_names = table.Column<List<string>>(type: "text[]", nullable: false),
                    wikidata_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    locked_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    bullet_diameter_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    case_length_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    overall_length_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    parent_cartridge_id = table.Column<int>(type: "integer", nullable: true),
                    design_date_year = table.Column<int>(type: "integer", nullable: true),
                    design_date_month = table.Column<int>(type: "integer", nullable: true),
                    design_date_day = table.Column<int>(type: "integer", nullable: true),
                    design_date_year_to = table.Column<int>(type: "integer", nullable: true),
                    design_date_precision = table.Column<string>(type: "text", nullable: true),
                    design_date_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    country_of_origin_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cartridges", x => x.id);
                    table.ForeignKey(
                        name: "fk_cartridges_cartridges_parent_cartridge_id",
                        column: x => x.parent_cartridge_id,
                        principalTable: "cartridges",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_cartridges_countries_country_of_origin_id",
                        column: x => x.country_of_origin_id,
                        principalTable: "countries",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    country_id = table.Column<int>(type: "integer", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    founded_year = table.Column<int>(type: "integer", nullable: true),
                    founded_month = table.Column<int>(type: "integer", nullable: true),
                    founded_day = table.Column<int>(type: "integer", nullable: true),
                    founded_year_to = table.Column<int>(type: "integer", nullable: true),
                    founded_precision = table.Column<string>(type: "text", nullable: true),
                    founded_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    wikidata_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    locked_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organizations_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "countries",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    birth_date_year = table.Column<int>(type: "integer", nullable: true),
                    birth_date_month = table.Column<int>(type: "integer", nullable: true),
                    birth_date_day = table.Column<int>(type: "integer", nullable: true),
                    birth_date_year_to = table.Column<int>(type: "integer", nullable: true),
                    birth_date_precision = table.Column<string>(type: "text", nullable: true),
                    birth_date_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    death_date_year = table.Column<int>(type: "integer", nullable: true),
                    death_date_month = table.Column<int>(type: "integer", nullable: true),
                    death_date_day = table.Column<int>(type: "integer", nullable: true),
                    death_date_year_to = table.Column<int>(type: "integer", nullable: true),
                    death_date_precision = table.Column<string>(type: "text", nullable: true),
                    death_date_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    nationality_id = table.Column<int>(type: "integer", nullable: true),
                    biography = table.Column<string>(type: "text", nullable: true),
                    wikidata_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    locked_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                    table.ForeignKey(
                        name: "fk_people_countries_nationality_id",
                        column: x => x.nationality_id,
                        principalTable: "countries",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "firearms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    alternative_names = table.Column<List<string>>(type: "text[]", nullable: false),
                    wikidata_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    locked_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    country_of_origin_id = table.Column<int>(type: "integer", nullable: true),
                    design_date_year = table.Column<int>(type: "integer", nullable: true),
                    design_date_month = table.Column<int>(type: "integer", nullable: true),
                    design_date_day = table.Column<int>(type: "integer", nullable: true),
                    design_date_year_to = table.Column<int>(type: "integer", nullable: true),
                    design_date_precision = table.Column<string>(type: "text", nullable: true),
                    design_date_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    production_start_year = table.Column<int>(type: "integer", nullable: true),
                    production_start_month = table.Column<int>(type: "integer", nullable: true),
                    production_start_day = table.Column<int>(type: "integer", nullable: true),
                    production_start_year_to = table.Column<int>(type: "integer", nullable: true),
                    production_start_precision = table.Column<string>(type: "text", nullable: true),
                    production_start_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    production_end_year = table.Column<int>(type: "integer", nullable: true),
                    production_end_month = table.Column<int>(type: "integer", nullable: true),
                    production_end_day = table.Column<int>(type: "integer", nullable: true),
                    production_end_year_to = table.Column<int>(type: "integer", nullable: true),
                    production_end_precision = table.Column<string>(type: "text", nullable: true),
                    production_end_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    operating_mechanism_id = table.Column<int>(type: "integer", nullable: true),
                    feed_system = table.Column<string>(type: "text", nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    development_history = table.Column<string>(type: "text", nullable: true),
                    service_history = table.Column<string>(type: "text", nullable: true),
                    wikipedia_title = table.Column<string>(type: "text", nullable: true),
                    wikipedia_revision_id = table.Column<long>(type: "bigint", nullable: true),
                    search_names = table.Column<string>(type: "text", nullable: true, computedColumnSql: "lower(name || ' ' || armorywiki_immutable_array_to_string(alternative_names))", stored: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', name), 'A') || setweight(to_tsvector('simple', armorywiki_immutable_array_to_string(alternative_names)), 'B') || setweight(to_tsvector('simple', coalesce(summary, '')), 'C')", stored: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firearms", x => x.id);
                    table.ForeignKey(
                        name: "fk_firearms_countries_country_of_origin_id",
                        column: x => x.country_of_origin_id,
                        principalTable: "countries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_firearms_operating_mechanisms_operating_mechanism_id",
                        column: x => x.operating_mechanism_id,
                        principalTable: "operating_mechanisms",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "wikidata_class_tag_maps",
                columns: table => new
                {
                    qid = table.Column<string>(type: "text", nullable: false),
                    tag_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wikidata_class_tag_maps", x => new { x.qid, x.tag_id });
                    table.ForeignKey(
                        name: "fk_wikidata_class_tag_maps_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "adoption",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firearm_id = table.Column<int>(type: "integer", nullable: false),
                    country_id = table.Column<int>(type: "integer", nullable: false),
                    from_year = table.Column<int>(type: "integer", nullable: true),
                    from_month = table.Column<int>(type: "integer", nullable: true),
                    from_day = table.Column<int>(type: "integer", nullable: true),
                    from_year_to = table.Column<int>(type: "integer", nullable: true),
                    from_precision = table.Column<string>(type: "text", nullable: true),
                    from_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    to_year = table.Column<int>(type: "integer", nullable: true),
                    to_month = table.Column<int>(type: "integer", nullable: true),
                    to_day = table.Column<int>(type: "integer", nullable: true),
                    to_year_to = table.Column<int>(type: "integer", nullable: true),
                    to_precision = table.Column<string>(type: "text", nullable: true),
                    to_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adoption", x => x.id);
                    table.ForeignKey(
                        name: "fk_adoption_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_adoption_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firearm_designer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firearm_id = table.Column<int>(type: "integer", nullable: false),
                    person_id = table.Column<int>(type: "integer", nullable: true),
                    organization_id = table.Column<int>(type: "integer", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firearm_designer", x => x.id);
                    table.CheckConstraint("ck_firearm_designers_person_xor_organization", "num_nonnulls(person_id, organization_id) = 1");
                    table.ForeignKey(
                        name: "fk_firearm_designer_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firearm_designer_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_firearm_designer_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "firearm_manufacturer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firearm_id = table.Column<int>(type: "integer", nullable: false),
                    organization_id = table.Column<int>(type: "integer", nullable: false),
                    from_year = table.Column<int>(type: "integer", nullable: true),
                    from_month = table.Column<int>(type: "integer", nullable: true),
                    from_day = table.Column<int>(type: "integer", nullable: true),
                    from_year_to = table.Column<int>(type: "integer", nullable: true),
                    from_precision = table.Column<string>(type: "text", nullable: true),
                    from_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    to_year = table.Column<int>(type: "integer", nullable: true),
                    to_month = table.Column<int>(type: "integer", nullable: true),
                    to_day = table.Column<int>(type: "integer", nullable: true),
                    to_year_to = table.Column<int>(type: "integer", nullable: true),
                    to_precision = table.Column<string>(type: "text", nullable: true),
                    to_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    is_licensed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firearm_manufacturer", x => x.id);
                    table.ForeignKey(
                        name: "fk_firearm_manufacturer_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firearm_manufacturer_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firearm_relations",
                columns: table => new
                {
                    from_firearm_id = table.Column<int>(type: "integer", nullable: false),
                    to_firearm_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firearm_relations", x => new { x.from_firearm_id, x.to_firearm_id, x.type });
                    table.CheckConstraint("ck_firearm_relations_not_self", "from_firearm_id <> to_firearm_id");
                    table.ForeignKey(
                        name: "fk_firearm_relations_firearms_from_firearm_id",
                        column: x => x.from_firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firearm_relations_firearms_to_firearm_id",
                        column: x => x.to_firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firearm_tag",
                columns: table => new
                {
                    firearm_id = table.Column<int>(type: "integer", nullable: false),
                    tag_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firearm_tag", x => new { x.firearm_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_firearm_tag_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firearm_tag_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firearm_id = table.Column<int>(type: "integer", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_base_variant = table.Column<bool>(type: "boolean", nullable: false),
                    introduced_date_year = table.Column<int>(type: "integer", nullable: true),
                    introduced_date_month = table.Column<int>(type: "integer", nullable: true),
                    introduced_date_day = table.Column<int>(type: "integer", nullable: true),
                    introduced_date_year_to = table.Column<int>(type: "integer", nullable: true),
                    introduced_date_precision = table.Column<string>(type: "text", nullable: true),
                    introduced_date_is_approximate = table.Column<bool>(type: "boolean", nullable: true),
                    weight_empty_g = table.Column<int>(type: "integer", nullable: true),
                    overall_length_mm = table.Column<int>(type: "integer", nullable: true),
                    overall_length_folded_mm = table.Column<int>(type: "integer", nullable: true),
                    barrel_length_mm = table.Column<int>(type: "integer", nullable: true),
                    rate_of_fire_min_rpm = table.Column<int>(type: "integer", nullable: true),
                    rate_of_fire_max_rpm = table.Column<int>(type: "integer", nullable: true),
                    muzzle_velocity_ms = table.Column<int>(type: "integer", nullable: true),
                    effective_range_m = table.Column<int>(type: "integer", nullable: true),
                    magazine_capacity_standard = table.Column<int>(type: "integer", nullable: true),
                    firing_modes = table.Column<string>(type: "text", nullable: false),
                    extra_specs = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variants", x => x.id);
                    table.ForeignKey(
                        name: "fk_variants_firearms_firearm_id",
                        column: x => x.firearm_id,
                        principalTable: "firearms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant_cartridge",
                columns: table => new
                {
                    variant_id = table.Column<int>(type: "integer", nullable: false),
                    cartridge_id = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_cartridge", x => new { x.variant_id, x.cartridge_id });
                    table.ForeignKey(
                        name: "fk_variant_cartridge_cartridges_cartridge_id",
                        column: x => x.cartridge_id,
                        principalTable: "cartridges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_variant_cartridge_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adoption_country_id",
                table: "adoption",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_adoption_firearm_id",
                table: "adoption",
                column: "firearm_id");

            migrationBuilder.CreateIndex(
                name: "ix_cartridges_country_of_origin_id",
                table: "cartridges",
                column: "country_of_origin_id");

            migrationBuilder.CreateIndex(
                name: "ix_cartridges_parent_cartridge_id",
                table: "cartridges",
                column: "parent_cartridge_id");

            migrationBuilder.CreateIndex(
                name: "ix_cartridges_slug",
                table: "cartridges",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cartridges_wikidata_id",
                table: "cartridges",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_countries_slug",
                table: "countries",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_countries_wikidata_id",
                table: "countries",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_firearm_designer_firearm_id_person_id_organization_id_role",
                table: "firearm_designer",
                columns: new[] { "firearm_id", "person_id", "organization_id", "role" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_firearm_designer_organization_id",
                table: "firearm_designer",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearm_designer_person_id",
                table: "firearm_designer",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearm_manufacturer_firearm_id",
                table: "firearm_manufacturer",
                column: "firearm_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearm_manufacturer_organization_id",
                table: "firearm_manufacturer",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearm_relations_to_firearm_id",
                table: "firearm_relations",
                column: "to_firearm_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearm_tag_tag_id",
                table: "firearm_tag",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearms_country_of_origin_id",
                table: "firearms",
                column: "country_of_origin_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearms_design_date_year",
                table: "firearms",
                column: "design_date_year");

            migrationBuilder.CreateIndex(
                name: "ix_firearms_operating_mechanism_id",
                table: "firearms",
                column: "operating_mechanism_id");

            migrationBuilder.CreateIndex(
                name: "ix_firearms_search_names",
                table: "firearms",
                column: "search_names")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_firearms_search_vector",
                table: "firearms",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_firearms_slug",
                table: "firearms",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_firearms_status",
                table: "firearms",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_firearms_wikidata_id",
                table: "firearms",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operating_mechanisms_parent_id",
                table: "operating_mechanisms",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_operating_mechanisms_path",
                table: "operating_mechanisms",
                column: "path")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_operating_mechanisms_slug",
                table: "operating_mechanisms",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organizations_country_id",
                table: "organizations",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_slug",
                table: "organizations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organizations_wikidata_id",
                table: "organizations",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_nationality_id",
                table: "people",
                column: "nationality_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_slug",
                table: "people",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_wikidata_id",
                table: "people",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variant_cartridge_cartridge_id",
                table: "variant_cartridge",
                column: "cartridge_id");

            migrationBuilder.CreateIndex(
                name: "ux_variant_cartridges_one_primary_per_variant",
                table: "variant_cartridge",
                column: "variant_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_variants_firearm_id_slug",
                table: "variants",
                columns: new[] { "firearm_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_variants_one_base_per_firearm",
                table: "variants",
                column: "firearm_id",
                unique: true,
                filter: "is_base_variant");

            migrationBuilder.CreateIndex(
                name: "ix_wikidata_class_tag_maps_tag_id",
                table: "wikidata_class_tag_maps",
                column: "tag_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adoption");

            migrationBuilder.DropTable(
                name: "firearm_designer");

            migrationBuilder.DropTable(
                name: "firearm_manufacturer");

            migrationBuilder.DropTable(
                name: "firearm_relations");

            migrationBuilder.DropTable(
                name: "firearm_tag");

            migrationBuilder.DropTable(
                name: "slug_redirects");

            migrationBuilder.DropTable(
                name: "variant_cartridge");

            migrationBuilder.DropTable(
                name: "wikidata_class_tag_maps");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "cartridges");

            migrationBuilder.DropTable(
                name: "variants");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "firearms");

            migrationBuilder.DropTable(
                name: "countries");

            migrationBuilder.DropTable(
                name: "operating_mechanisms");

            migrationBuilder.Sql("DROP FUNCTION armorywiki_immutable_array_to_string(text[]);");
        }
    }
}
