using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEntry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PackageId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEntry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntry_TimestampUnixMs",
                table: "ActivityEntry",
                column: "TimestampUnixMs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEntry");
        }
    }
}
