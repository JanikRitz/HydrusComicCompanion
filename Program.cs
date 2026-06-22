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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
