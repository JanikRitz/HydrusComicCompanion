using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class TitleNamespaceRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SeriesNamespace",
                table: "HydrusSettings",
                newName: "TitleNamespace");

            migrationBuilder.AlterColumn<int>(
                name: "VolumeNumber",
                table: "Chapters",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "ChapterNumber",
                table: "Chapters",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TitleNamespace",
                table: "HydrusSettings",
                newName: "SeriesNamespace");

            migrationBuilder.AlterColumn<int>(
                name: "VolumeNumber",
                table: "Chapters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ChapterNumber",
                table: "Chapters",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
