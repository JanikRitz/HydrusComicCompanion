using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediumTagsToSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComicMediumTag",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "medium:comic");

            migrationBuilder.AddColumn<string>(
                name: "ImagesetMediumTag",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "medium:imageset");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComicMediumTag",
                table: "HydrusSettings");

            migrationBuilder.DropColumn(
                name: "ImagesetMediumTag",
                table: "HydrusSettings");
        }
    }
}
