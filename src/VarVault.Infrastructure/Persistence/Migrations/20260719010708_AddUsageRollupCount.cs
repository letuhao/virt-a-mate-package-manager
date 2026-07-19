using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageRollupCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RolledUpUseCount",
                table: "UsageStat",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RolledUpUseCount",
                table: "UsageStat");
        }
    }
}
