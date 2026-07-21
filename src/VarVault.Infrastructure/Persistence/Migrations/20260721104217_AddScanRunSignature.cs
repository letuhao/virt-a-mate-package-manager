using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScanRunSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SigFileCount",
                table: "ScanRun",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SigNewestMtimeTicks",
                table: "ScanRun",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SigPathsHash",
                table: "ScanRun",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SigTotalBytes",
                table: "ScanRun",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SigFileCount",
                table: "ScanRun");

            migrationBuilder.DropColumn(
                name: "SigNewestMtimeTicks",
                table: "ScanRun");

            migrationBuilder.DropColumn(
                name: "SigPathsHash",
                table: "ScanRun");

            migrationBuilder.DropColumn(
                name: "SigTotalBytes",
                table: "ScanRun");
        }
    }
}
