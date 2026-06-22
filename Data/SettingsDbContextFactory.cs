using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HydrusComicCompanion.Data;

public sealed class SettingsDbContextFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext(string[] args)
    {
        var contentRootPath = Directory.GetCurrentDirectory();
        var appDataDirectory = Path.Combine(contentRootPath, "App_Data");
        Directory.CreateDirectory(appDataDirectory);

        var settingsDatabasePath = Path.Combine(appDataDirectory, "settings.db");
        var settingsConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = settingsDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseSqlite(settingsConnectionString);

        return new SettingsDbContext(optionsBuilder.Options);
    }
}
