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
builder.Services.AddScoped<IHydrusMediaService, HydrusMediaService>();
builder.Services.AddScoped<IComicImportService, ComicImportService>();
builder.Services.AddScoped<ICalibreService, CalibreService>();

// OCR service
builder.Services.AddScoped<IOcrReader, OcrReader>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SettingsDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    await dbContext.Database.MigrateAsync();

    var settingsService = scope.ServiceProvider.GetRequiredService<IHydrusSettingsService>();
    var settings = await settingsService.GetSettingsAsync();

    if (string.IsNullOrWhiteSpace(settings.TagServiceKey) && !string.IsNullOrWhiteSpace(settings.PrimaryTagService))
    {
        try
        {
            var catalog = await settingsService.GetServicesAsync(settings);
            var tagService = catalog.TagServices.FirstOrDefault(service =>
                string.Equals(service.Name, settings.PrimaryTagService, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(tagService?.Key))
            {
                settings.TagServiceKey = tagService.Key;
                await settingsService.SaveSettingsAsync(settings);
                app.Logger.LogInformation("Resolved Hydrus tag service key for {TagServiceName}.", settings.PrimaryTagService);
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to resolve Hydrus tag service key during startup.");
        }
    }
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
        return Results.Ok(new { success = true, titlesSynced = count });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("SyncLibrary")
.Produces(200)
.Produces(400);

app.MapPost("/api/sync/existing-libraries", async (IHydrusSyncService syncService) =>
{
    try
    {
        var count = await syncService.SyncExistingLibrariesAsync();
        return Results.Ok(new { success = true, titlesSynced = count });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("SyncExistingLibraries")
.Produces(200)
.Produces(400);

app.MapPost("/api/sync/comic/{comicName}", async (string comicName, IHydrusSyncService syncService) =>
{
    try
    {
        var seriesId = await syncService.SyncComicAsync(comicName);
        return seriesId.HasValue
            ? Results.Ok(new { success = true, seriesId = seriesId.Value })
            : Results.NotFound(new { success = false, error = "Comic not found" });
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
    var count = await syncService.GetUnsyncedComicsCountAsync();
    return Results.Ok(new { unsyncedCount = count });
})
.WithName("GetUnsyncedCount")
.Produces(200);

app.MapGet("/media/thumbnail/{hash}", async (string hash, IHydrusMediaService mediaService, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    try
    {
        var eTag = BuildMediaEtag("thumb", hash);
        if (IsNotModified(httpContext, eTag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        ApplyImmutableMediaCacheHeaders(httpContext, eTag);
        var media = await mediaService.GetThumbnailAsync(hash, cancellationToken);
        return Results.Stream(media.Content, contentType: media.ContentType, enableRangeProcessing: true);
    }
    catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
    {
        return Results.StatusCode((int)ex.StatusCode.Value);
    }
})
.WithName("GetMediaThumbnail")
.Produces(200)
.Produces(304);

app.MapGet("/media/render/{hash}", async (
    string hash,
    int? width,
    int? height,
    int? renderFormat,
    int? renderQuality,
    IHydrusMediaService mediaService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    try
    {
        var renderKey = $"{hash}|{width?.ToString() ?? "-"}|{height?.ToString() ?? "-"}|{renderFormat?.ToString() ?? "-"}|{renderQuality?.ToString() ?? "-"}";
        var eTag = BuildMediaEtag("render", renderKey);
        if (IsNotModified(httpContext, eTag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        ApplyImmutableMediaCacheHeaders(httpContext, eTag);
        var media = await mediaService.GetRenderAsync(hash, width, height, renderFormat, renderQuality, cancellationToken);
        return Results.Stream(media.Content, contentType: media.ContentType, enableRangeProcessing: true);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
    {
        return Results.StatusCode((int)ex.StatusCode.Value);
    }
})
.WithName("GetMediaRender")
.Produces(200)
.Produces(304)
.Produces(400);

app.MapGet("/media/file/{hash}", async (string hash, bool download, IHydrusMediaService mediaService, CancellationToken cancellationToken) =>
{
    try
    {
        var media = await mediaService.GetOriginalFileAsync(hash, download, cancellationToken);
        var fileName = download ? hash : null;
        return Results.Stream(media.Content, contentType: media.ContentType, fileDownloadName: fileName, enableRangeProcessing: true);
    }
    catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
    {
        return Results.StatusCode((int)ex.StatusCode.Value);
    }
})
.WithName("GetMediaFile")
.Produces(200);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string BuildMediaEtag(string prefix, string cacheKey)
{
    return $"\"{prefix}-{Uri.EscapeDataString(cacheKey)}\"";
}

static bool IsNotModified(HttpContext context, string eTag)
{
    var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
    if (string.IsNullOrWhiteSpace(ifNoneMatch))
    {
        return false;
    }

    return ifNoneMatch
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(tag => tag == "*" || string.Equals(tag, eTag, StringComparison.Ordinal));
}

static void ApplyImmutableMediaCacheHeaders(HttpContext context, string eTag)
{
    context.Response.Headers.ETag = eTag;
    context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    context.Response.Headers.Vary = "Accept-Encoding";
}
