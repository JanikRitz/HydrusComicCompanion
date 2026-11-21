using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HydrusComicCompanion.Data.Migrations;

[DbContext(typeof(SettingsDbContext))]
[Migration("20261121000000_AddCachedPageTags")]
public sealed class AddCachedPageTags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>("CachedPageTagsJson", "Series", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE Series DROP COLUMN CachedPageTagsJson");
}