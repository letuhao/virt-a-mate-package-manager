using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScanLedgerAndIngestState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IngestAttempts",
                table: "VarFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IngestError",
                table: "VarFile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IngestState",
                table: "VarFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "VarFile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseOwner",
                table: "VarFile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SeenGeneration",
                table: "VarFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "DirtyPackage",
                columns: table => new
                {
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MarkedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirtyPackage", x => x.PackageId);
                });

            migrationBuilder.CreateTable(
                name: "ScanRun",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Generation = table.Column<long>(type: "INTEGER", nullable: false),
                    Phase = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Discovered = table.Column<long>(type: "INTEGER", nullable: false),
                    Ingested = table.Column<long>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<long>(type: "INTEGER", nullable: false),
                    Failed = table.Column<long>(type: "INTEGER", nullable: false),
                    Pruned = table.Column<long>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRun", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_IngestState_LeaseExpiresAt",
                table: "VarFile",
                columns: new[] { "IngestState", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_RepositoryId_SeenGeneration",
                table: "VarFile",
                columns: new[] { "RepositoryId", "SeenGeneration" });

            migrationBuilder.CreateIndex(
                name: "IX_DirtyPackage_MarkedAt",
                table: "DirtyPackage",
                column: "MarkedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanRun_Phase",
                table: "ScanRun",
                column: "Phase");

            migrationBuilder.CreateIndex(
                name: "IX_ScanRun_PublicId",
                table: "ScanRun",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanRun_RepositoryId_Generation",
                table: "ScanRun",
                columns: new[] { "RepositoryId", "Generation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirtyPackage");

            migrationBuilder.DropTable(
                name: "ScanRun");

            migrationBuilder.DropIndex(
                name: "IX_VarFile_IngestState_LeaseExpiresAt",
                table: "VarFile");

            migrationBuilder.DropIndex(
                name: "IX_VarFile_RepositoryId_SeenGeneration",
                table: "VarFile");

            migrationBuilder.DropColumn(
                name: "IngestAttempts",
                table: "VarFile");

            migrationBuilder.DropColumn(
                name: "IngestError",
                table: "VarFile");

            migrationBuilder.DropColumn(
                name: "IngestState",
                table: "VarFile");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "VarFile");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "VarFile");

            migrationBuilder.DropColumn(
                name: "SeenGeneration",
                table: "VarFile");
        }
    }
}
