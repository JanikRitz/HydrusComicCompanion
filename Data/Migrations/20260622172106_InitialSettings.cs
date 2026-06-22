using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HydrusSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedApiAccessKey = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryTagService = table.Column<string>(type: "TEXT", nullable: false),
                    TargetFileDomain = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesNamespace = table.Column<string>(type: "TEXT", nullable: false),
                    VolumeNamespace = table.Column<string>(type: "TEXT", nullable: false),
                    ChapterNamespace = table.Column<string>(type: "TEXT", nullable: false),
                    PageNamespace = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundSyncIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HydrusSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HydrusSettings");
        }
    }
}
