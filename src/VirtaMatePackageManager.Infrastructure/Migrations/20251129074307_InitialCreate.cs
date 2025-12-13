using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VirtaMatePackageManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_targets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    profile_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installation_targets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repositories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repositories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "var_packages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    repository_id = table.Column<int>(type: "integer", nullable: false),
                    var_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    creator_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    package_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    file_path = table.Column<string>(type: "text", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    relative_path = table.Column<string>(type: "text", nullable: true),
                    license_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    credits = table.Column<string>(type: "text", nullable: true),
                    instructions = table.Column<string>(type: "text", nullable: true),
                    promotional_link = table.Column<string>(type: "text", nullable: true),
                    program_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    preview_image_path = table.Column<string>(type: "text", nullable: true),
                    file_created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    file_modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_scanned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_var_packages", x => x.id);
                    table.ForeignKey(
                        name: "FK_var_packages_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    var_package_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<int>(type: "integer", nullable: false),
                    relative_path = table.Column<string>(type: "text", nullable: false),
                    is_preset = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    preview_image_path = table.Column<string>(type: "text", nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_items_var_packages_var_package_id",
                        column: x => x.var_package_id,
                        principalTable: "var_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dependencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    var_package_id = table.Column<int>(type: "integer", nullable: false),
                    dependency_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    resolved_var_package_id = table.Column<int>(type: "integer", nullable: true),
                    version_constraint = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dependencies", x => x.id);
                    table.ForeignKey(
                        name: "FK_dependencies_var_packages_resolved_var_package_id",
                        column: x => x.resolved_var_package_id,
                        principalTable: "var_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dependencies_var_packages_var_package_id",
                        column: x => x.var_package_id,
                        principalTable: "var_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    var_package_id = table.Column<int>(type: "integer", nullable: false),
                    installation_target_id = table.Column<int>(type: "integer", nullable: false),
                    symlink_path = table.Column<string>(type: "text", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    installed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    installed_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installations", x => x.id);
                    table.ForeignKey(
                        name: "FK_installations_installation_targets_installation_target_id",
                        column: x => x.installation_target_id,
                        principalTable: "installation_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_installations_var_packages_var_package_id",
                        column: x => x.var_package_id,
                        principalTable: "var_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_content_items_type",
                table: "content_items",
                column: "content_type");

            migrationBuilder.CreateIndex(
                name: "idx_content_items_var_package",
                table: "content_items",
                column: "var_package_id");

            migrationBuilder.CreateIndex(
                name: "uk_content_items_var_package_path",
                table: "content_items",
                columns: new[] { "var_package_id", "relative_path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dependencies_name",
                table: "dependencies",
                column: "dependency_name");

            migrationBuilder.CreateIndex(
                name: "idx_dependencies_resolved",
                table: "dependencies",
                column: "resolved_var_package_id");

            migrationBuilder.CreateIndex(
                name: "idx_dependencies_resolved_flag",
                table: "dependencies",
                column: "is_resolved");

            migrationBuilder.CreateIndex(
                name: "idx_dependencies_var_package",
                table: "dependencies",
                column: "var_package_id");

            migrationBuilder.CreateIndex(
                name: "uk_dependencies_var_package_name",
                table: "dependencies",
                columns: new[] { "var_package_id", "dependency_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_installation_targets_active",
                table: "installation_targets",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "idx_installation_targets_profile",
                table: "installation_targets",
                column: "profile_name");

            migrationBuilder.CreateIndex(
                name: "uk_installation_targets_path",
                table: "installation_targets",
                column: "path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_installations_installed_at",
                table: "installations",
                column: "installed_at");

            migrationBuilder.CreateIndex(
                name: "idx_installations_target",
                table: "installations",
                column: "installation_target_id");

            migrationBuilder.CreateIndex(
                name: "idx_installations_var_package",
                table: "installations",
                column: "var_package_id");

            migrationBuilder.CreateIndex(
                name: "uk_installations_symlink_path",
                table: "installations",
                column: "symlink_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_installations_var_package_target",
                table: "installations",
                columns: new[] { "var_package_id", "installation_target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_repositories_enabled",
                table: "repositories",
                column: "enabled",
                filter: "\"enabled\" = true");

            migrationBuilder.CreateIndex(
                name: "idx_repositories_priority",
                table: "repositories",
                columns: new[] { "priority", "enabled" });

            migrationBuilder.CreateIndex(
                name: "uk_repositories_path",
                table: "repositories",
                column: "path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_var_packages_composite",
                table: "var_packages",
                columns: new[] { "creator_name", "package_name", "version" });

            migrationBuilder.CreateIndex(
                name: "idx_var_packages_creator",
                table: "var_packages",
                column: "creator_name");

            migrationBuilder.CreateIndex(
                name: "idx_var_packages_package",
                table: "var_packages",
                column: "package_name");

            migrationBuilder.CreateIndex(
                name: "idx_var_packages_repository",
                table: "var_packages",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "idx_var_packages_updated",
                table: "var_packages",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "idx_var_packages_version",
                table: "var_packages",
                column: "version");

            migrationBuilder.CreateIndex(
                name: "uk_var_name",
                table: "var_packages",
                column: "var_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_var_package_repo_path",
                table: "var_packages",
                columns: new[] { "repository_id", "file_path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_items");

            migrationBuilder.DropTable(
                name: "dependencies");

            migrationBuilder.DropTable(
                name: "installations");

            migrationBuilder.DropTable(
                name: "installation_targets");

            migrationBuilder.DropTable(
                name: "var_packages");

            migrationBuilder.DropTable(
                name: "repositories");
        }
    }
}
