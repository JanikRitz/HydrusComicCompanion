using MudBlazor.Services;
using HydrusComicCompanion.Components;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

var appDataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataDirectory);

var dataProtectionKeysDirectory = Path.Combine(appDataDirectory, "keys");
Directory.CreateDirectory(dataProtectionKeysDirectory);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));

var settingsDatabasePath = Path.Combine(appDataDirectory, "settings.db");
var settingsConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = settingsDatabasePath,
    Mode = SqliteOpenMode.ReadWriteCreate
}.ToString();

builder.Services.AddDbContextFactory<SettingsDbContext>(options =>
{
    options.UseSqlite(settingsConnectionString);
});

builder.Services.Configure<HydrusSettings>(builder.Configuration.GetSection("HydrusSettings"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IHydrusSettingsService, HydrusSettingsService>();

// Add Hydrus API and sync services
builder.Services.AddScoped<IHydrusApiService, HydrusApiService>();
builder.Services.AddScoped<IHydrusSyncService, HydrusSyncService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SettingsDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();

// Add API endpoints for Hydrus sync operations
app.MapPost("/api/sync/test-connection", async (IHydrusApiService apiService) =>
{
    var success = await apiService.TestConnectionAsync();
    return Results.Ok(new { success, message = success ? "Connected to Hydrus API" : "Failed to connect to Hydrus API" });
})
.WithName("TestHydrusConnection")
.Produces(200);

app.MapPost("/api/sync/library", async (IHydrusSyncService syncService) =>
{
    try
    {
        var count = await syncService.SyncLibraryAsync();
        return Results.Ok(new { success = true, seriesSynced = count });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("SyncLibrary")
.Produces(200)
.Produces(400);

app.MapPost("/api/sync/series/{seriesName}", async (string seriesName, IHydrusSyncService syncService) =>
{
    try
    {
        var seriesId = await syncService.SyncSeriesAsync(seriesName);
        return seriesId.HasValue
            ? Results.Ok(new { success = true, seriesId = seriesId.Value })
            : Results.NotFound(new { success = false, error = "Series not found" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("SyncSeries")
.Produces(200)
.Produces(400)
.Produces(404);

app.MapGet("/api/sync/unsynced-count", async (IHydrusSyncService syncService) =>
{
    var count = await syncService.GetUnsyncedSeriesCountAsync();
    return Results.Ok(new { unsyncedCount = count });
})
.WithName("GetUnsyncedCount")
.Produces(200);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
