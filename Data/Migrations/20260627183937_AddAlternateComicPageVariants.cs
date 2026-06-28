using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HydrusComicCompanion.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlternateComicPageVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlternatePageDefaultValue",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "AlternatePageNamespace",
                table: "HydrusSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "variant:");

            migrationBuilder.CreateTable(
                name: "PageVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: true),
                    OcrText = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageVariants_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO PageVariants (PageId, FileHash, MimeType, OcrText, IsDefault, Label)
                SELECT Id, FileHash, MimeType, OcrText, 1, NULL
                FROM Pages
                WHERE FileHash IS NOT NULL AND TRIM(FileHash) <> '';
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE _PageMerge AS
                SELECT
                    Id AS DuplicatePageId,
                    MIN(Id) OVER (PARTITION BY ChapterId, PageNumber) AS CanonicalPageId
                FROM Pages;
                """);

            migrationBuilder.Sql("""
                UPDATE PageVariants
                SET PageId = (
                    SELECT CanonicalPageId
                    FROM _PageMerge
                    WHERE DuplicatePageId = PageVariants.PageId
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM PageVariants
                WHERE Id NOT IN (
                    SELECT MIN(Id)
                    FROM PageVariants
                    GROUP BY PageId, FileHash
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM Pages
                WHERE Id IN (
                    SELECT DuplicatePageId
                    FROM _PageMerge
                    WHERE DuplicatePageId <> CanonicalPageId
                );
                """);

            migrationBuilder.Sql("DROP TABLE _PageMerge;");

            migrationBuilder.Sql("""
                UPDATE PageVariants
                SET IsDefault = CASE
                    WHEN Id IN (
                        SELECT MIN(Id)
                        FROM PageVariants
                        GROUP BY PageId
                    ) THEN 1
                    ELSE 0
                END;
                """);

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "OcrText",
                table: "Pages");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ChapterId_PageNumber",
                table: "Pages",
                columns: new[] { "ChapterId", "PageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageVariants_PageId",
                table: "PageVariants",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_PageVariants_PageId_FileHash",
                table: "PageVariants",
                columns: new[] { "PageId", "FileHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PageVariants_PageId_FileHash",
                table: "PageVariants");

            migrationBuilder.DropIndex(
                name: "IX_PageVariants_PageId",
                table: "PageVariants");

            migrationBuilder.DropIndex(
                name: "IX_Pages_ChapterId_PageNumber",
                table: "Pages");

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Pages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "Pages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OcrText",
                table: "Pages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE Pages
                SET
                    FileHash = COALESCE((
                        SELECT pv.FileHash
                        FROM PageVariants pv
                        WHERE pv.PageId = Pages.Id
                        ORDER BY pv.IsDefault DESC, pv.Id ASC
                        LIMIT 1
                    ), ''),
                    MimeType = (
                        SELECT pv.MimeType
                        FROM PageVariants pv
                        WHERE pv.PageId = Pages.Id
                        ORDER BY pv.IsDefault DESC, pv.Id ASC
                        LIMIT 1
                    ),
                    OcrText = (
                        SELECT pv.OcrText
                        FROM PageVariants pv
                        WHERE pv.PageId = Pages.Id
                        ORDER BY pv.IsDefault DESC, pv.Id ASC
                        LIMIT 1
                    );
                """);

            migrationBuilder.DropTable(
                name: "PageVariants");

            migrationBuilder.DropColumn(
                name: "AlternatePageDefaultValue",
                table: "HydrusSettings");

            migrationBuilder.DropColumn(
                name: "AlternatePageNamespace",
                table: "HydrusSettings");
        }
    }
}
