using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportOutcomes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    Lane = table.Column<string>(type: "TEXT", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportOutcomes_ImportRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportOutcomes_RunId",
                table: "ImportOutcomes",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportOutcomes");
        }
    }
}
