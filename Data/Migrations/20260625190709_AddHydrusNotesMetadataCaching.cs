using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHydrusNotesMetadataCaching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayTitle",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OcrText",
                table: "Pages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComicCommentNoteName",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FullTitleNoteName",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OcrTextNoteName",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "DisplayTitle",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "OcrText",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ComicCommentNoteName",
                table: "HydrusSettings");

            migrationBuilder.DropColumn(
                name: "FullTitleNoteName",
                table: "HydrusSettings");

            migrationBuilder.DropColumn(
                name: "OcrTextNoteName",
                table: "HydrusSettings");
        }
    }
}
