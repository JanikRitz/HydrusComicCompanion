using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageIndex",
                table: "PageVariants",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexNamespace",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "index:");

            migrationBuilder.AddColumn<string>(
                name: "SetNamespace",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "set:");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "ImageIndex",
                table: "PageVariants");

            migrationBuilder.DropColumn(
                name: "IndexNamespace",
                table: "HydrusSettings");

            migrationBuilder.DropColumn(
                name: "SetNamespace",
                table: "HydrusSettings");
        }
    }
}
