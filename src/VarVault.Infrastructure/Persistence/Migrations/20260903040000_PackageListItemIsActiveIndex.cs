using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PackageListItemIsActiveIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PackageListItem_IsActive",
            table: "PackageListItem",
            column: "PackageId",
            filter: "IsActive = 1");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PackageListItem_IsActive",
            table: "PackageListItem");
    }
}
