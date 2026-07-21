using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePackageLinkAndInstalledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InstalledAt",
                table: "PackageListItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProfilePackageLink",
                columns: table => new
                {
                    ProfileId = table.Column<long>(type: "INTEGER", nullable: false),
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false),
                    InstalledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfilePackageLink", x => new { x.ProfileId, x.PackageId });
                    table.ForeignKey(
                        name: "FK_ProfilePackageLink_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfilePackageLink_Profile_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_AddedAt_VarName_PackageId",
                table: "PackageListItem",
                columns: new[] { "AddedAt", "VarName", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_InstalledAt_VarName_PackageId",
                table: "PackageListItem",
                columns: new[] { "InstalledAt", "VarName", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfilePackageLink_PackageId",
                table: "ProfilePackageLink",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfilePackageLink_ProfileId_InstalledAt",
                table: "ProfilePackageLink",
                columns: new[] { "ProfileId", "InstalledAt" });

            // Backfill profile membership from existing activation links (historical InstalledAt = migration time).
            migrationBuilder.Sql("""
                INSERT INTO ProfilePackageLink (ProfileId, PackageId, InstalledAt, Reason, UpdatedAt)
                SELECT DISTINCT al.ProfileId, vf.PackageId, datetime('now'), MIN(al.Reason), datetime('now')
                FROM ActivationLink al
                INNER JOIN VarFile vf ON vf.Id = al.VarFileId
                WHERE vf.PackageId IS NOT NULL
                  AND al.LinkKind IN (0, 1)
                GROUP BY al.ProfileId, vf.PackageId;
                """);

            migrationBuilder.Sql("""
                UPDATE PackageListItem
                SET IsActive = 1,
                    InstalledAt = (
                        SELECT ppl.InstalledAt FROM ProfilePackageLink ppl
                        INNER JOIN Profile p ON p.Id = ppl.ProfileId
                        WHERE ppl.PackageId = PackageListItem.PackageId AND p.IsActive = 1
                        LIMIT 1)
                WHERE PackageId IN (
                    SELECT ppl.PackageId FROM ProfilePackageLink ppl
                    INNER JOIN Profile p ON p.Id = ppl.ProfileId
                    WHERE p.IsActive = 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfilePackageLink");

            migrationBuilder.DropIndex(
                name: "IX_PackageListItem_AddedAt_VarName_PackageId",
                table: "PackageListItem");

            migrationBuilder.DropIndex(
                name: "IX_PackageListItem_InstalledAt_VarName_PackageId",
                table: "PackageListItem");

            migrationBuilder.DropColumn(
                name: "InstalledAt",
                table: "PackageListItem");
        }
    }
}
