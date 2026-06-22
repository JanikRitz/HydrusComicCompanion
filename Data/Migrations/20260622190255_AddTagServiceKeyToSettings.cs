using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagServiceKeyToSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TagServiceKey",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TagServiceKey",
                table: "HydrusSettings");
        }
    }
}
