using System.Net;
using System.Text;
using System.Text.Json;
using HydrusComicCompanion.Components.ImportWizard.State;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CollectionChecks;

internal static class Program
{
    private static int _checks;

    private static async Task Main()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new CacheFactory(new DbContextOptionsBuilder<SettingsDbContext>().UseSqlite(connection).Options);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "fresh cache applies all schema migrations");
        }

        var settings = new TestSettings();
        using var handler = new HydrusHandler();
        using var http = new HttpClient(handler);
        var api = new HydrusApiService(http, settings, NullLogger<HydrusApiService>.Instance);
        var sync = new HydrusSyncService(api, factory, settings, NullLogger<HydrusSyncService>.Instance);
        handler.Files.AddRange([
            File(1, "b", "comic:shared", "set:shared", "volume:2", "chapter:1", "page:1", "index:2", "variant:default"),
            File(99, "a", "comic:shared", "set:shared", "volume:2", "chapter:1", "page:1", "index:2", "variant:no text", "variant:monochrome"),
            File(3, "c", "set:shared", "index:1", "volume:99", "chapter:99", "page:99"),
            File(4, "0", "set:shared", "page:3", "variant:default"),
            File(5, "z", "set:shared", $"index:{int.MaxValue}"),
            File(6, "y", "set:shared", "index:0"),
            File(7, "d", "comic:index-only", "index:1"),
            File(8, "e", "set:page-only", "page:1"),
            File(9, "f", "comic:single", "meta:single page comic"),
            File(10, "u", "comic:shared", "volume:2", "chapter:1"),
            File(11, "aa", "comic:shared", "volume:2", "chapter:1", "page:1")
        ]);

        var discovered = await api.DiscoverCollectionsAsync();
        Check(discovered.Count == 5, "discover both collection types");
        Check(discovered.Contains(new("index-only", CollectionKind.Comic)), "discover comics through index tags");
        Check(discovered.Contains(new("page-only", CollectionKind.Imageset)), "discover imagesets through page tags");
        Check(handler.Searches.All(query => query.Contains("page:*") && query.Contains("index:*")), "discovery uses page OR index namespaces");
        Check(await sync.GetUnsyncedComicsCountAsync() == 5, "unsynced count includes typed identities");
        Check(await sync.SyncLibraryAsync() == 5, "full sync includes comics and imagesets");
        var comic = await ReadCollection(factory, "shared", CollectionKind.Comic);
        var imageset = await ReadCollection(factory, "shared", CollectionKind.Imageset);
        Check(comic.Id != imageset.Id, "same-title collection types have independent identities");
        Check(comic.Chapters.Single().VolumeNumber == 2 && comic.Chapters.Single().ChapterNumber == 1, "comic hierarchy retained");
        var comicPages = comic.Chapters.Single().Pages.OrderBy(page => page.PageNumber).ToList();
        Check(comicPages.Count == 2, "comic alternates grouped and unnumbered image kept separately");
        Check(comicPages[0].Variants.Single(variant => variant.IsDefault).FileHash == "b", "explicit comic default beats untagged/hash fallback");
        Check(comicPages[1].Variants.Single().FileHash == "u", "unnumbered comic page sorts last");
        Check(imageset.Chapters.Single().VolumeNumber is null && imageset.Chapters.Single().ChapterNumber is null, "imagesets ignore comic hierarchy tags");
        var images = ImagesetImage.FromCollection(imageset);
        Check(images.Select(image => image.File.FileHash).SequenceEqual(["y", "c", "a", "b", "z", "0"]), "imageset index order, hash ties and missing-last ordering");
        Check(images[^2].Index == int.MaxValue && images[^1].Index is null, "maximum integer and missing index remain distinct");
        Check(images.Single(image => image.File.FileHash == "a").Labels.SequenceEqual(["monochrome", "no text"]), "all imageset variant labels retained");
        Check(images.Single(image => image.File.FileHash == "b").Labels.Contains("default"), "imageset default is a visible label");
        Check(images.Count == 6, "all imageset images remain visible");
        var comicPreparation = await sync.ExtractComicAsync("shared");
        Check(comicPreparation.Pages.Single(page => page.PageNumber == 1 && page.IsDefaultVariant).Sha256Hash == "b", "comic extraction retains explicit default");
        Check(await api.GetTitlePageCountAsync("page-only", "set:", "index:") == 1, "imageset page counts respect page namespace discovery");
        Check(await sync.SyncExistingLibrariesAsync() == 5, "existing-only refresh includes both kinds");
        Check(await sync.GetUnsyncedComicsCountAsync() == 0, "typed identities count as synchronized");
        Check((await ReadCollection(factory, "shared", CollectionKind.Imageset)).Id == imageset.Id, "refresh retains collection identity");

        var preparation = await sync.ExtractCollectionAsync("shared", CollectionKind.Imageset);
        var state = new ImportWizardState { Kind = CollectionKind.Imageset };
        state.ApplyPreparation(preparation);
        state.GoToChapters();
        var importRequest = state.BuildImportRequest();
        Check(importRequest.Kind == CollectionKind.Imageset && importRequest.ChapterStartPageIndices.Count == 0 && importRequest.VolumeStarts.Count == 0, "imageset import omits chapter and volume structure");
        Check(importRequest.Pages.Select(page => page.PageNumber).SequenceEqual(new int?[] { 0, 1, 2, 2, int.MaxValue, null }), "wizard preserves indices including duplicates and missing values");
        Check(importRequest.Pages.Single(page => page.Sha256Hash == "a").VariantLabel == "monochrome\nno text", "wizard preserves multiple variant labels");
        var dialog = new HydrusMetadataEditDialogModel
        {
            ComicId = imageset.Id,
            Kind = CollectionKind.Imageset,
            HydrusTitle = "shared",
            Pages = preparation.Pages,
            CoverFileHash = "b"
        };
        dialog.Pages.Single(page => page.Sha256Hash == "a").PageNumber = 7;
        dialog.Pages.Single(page => page.Sha256Hash == "a").VariantLabel = "no text\nalternate color";
        var edit = dialog.ToRequest();
        Check(edit.Pages.Single(page => page.Sha256Hash == "a").PageNumber == 7, "metadata mapper preserves explicit imageset indices");
        var writesBeforeSave = handler.Writes;
        Check(writesBeforeSave == 0, "preparation and editing make no Hydrus writes");
        await sync.ApplyMetadataEditAsync(edit);
        var editedTags = handler.Files.Single(file => file.Hash == "a").GetAllStorageTags();
        Check(editedTags.Contains("set:shared") && editedTags.Contains("index:7") && !editedTags.Contains("index:2"), "save replaces imageset index tags");
        Check(editedTags.Contains("comic:shared") && editedTags.Contains("page:1") && editedTags.Contains("volume:2"), "imageset save preserves comic membership and structure");
        Check(editedTags.Contains("variant:alternate color") && !editedTags.Contains("variant:monochrome"), "save replaces variant attributes");
        imageset = await ReadCollection(factory, "shared", CollectionKind.Imageset);
        Check(ImagesetImage.FromCollection(imageset).Single(image => image.File.FileHash == "a").Index == 7, "metadata save updates cached index");
        Check(ImagesetImage.FromCollection(imageset).Last().Index is null, "metadata save preserves missing indices");

        var importer = new ComicImportService(api, factory, settings, NullLogger<ComicImportService>.Instance, sync);
        var importedId = await importer.ImportComicAsync(new ComicImportRequest
        {
            Kind = CollectionKind.Imageset,
            SeriesName = "new set",
            Pages = [new ImportPage { Sha256Hash = "c", PageNumber = 4, VariantLabel = "detail\ncolor", MimeType = "image/png" }]
        });
        var imported = await ReadCollection(factory, "new set", CollectionKind.Imageset);
        Check(importedId == imported.Id && ImagesetImage.FromCollection(imported).Single().Index == 4, "imageset import writes tags and refreshes cache");
        var importedTags = handler.Files.Single(file => file.Hash == "c").GetAllStorageTags();
        Check(importedTags.Contains("set:new set") && !importedTags.Contains("comic:new set") && !importedTags.Contains("index:1"), "imageset import uses set/index and removes stale indices");
        Check(importedTags.Contains("variant:detail") && importedTags.Contains("variant:color"), "imageset import emits each variant attribute");

        var unnumbered = File(20, "unnumbered", "set:unnumbered");
        unnumbered.Tags["other"] = new ServiceTagBucket { StorageTags = new() { ["0"] = ["index:42"] } };
        handler.Files.Add(unnumbered);
        await sync.SyncCollectionAsync("unnumbered", CollectionKind.Imageset);
        Check(ImagesetImage.FromCollection(await ReadCollection(factory, "unnumbered", CollectionKind.Imageset)).Single().Index is null,
            "unnumbered imagesets do not borrow indices from other tag services");
        handler.Files.Add(File(21, "fallback-z", "comic:fallback", "page:1", "variant:red"));
        handler.Files.Add(File(22, "fallback-a", "comic:fallback", "page:1", "variant:blue"));
        await sync.SyncComicAsync("fallback");
        Check((await ReadCollection(factory, "fallback", CollectionKind.Comic)).Chapters.Single().Pages.Single().Variants.Single(variant => variant.IsDefault).FileHash == "fallback-a",
            "comic fallback uses hash rather than file ID when no default is tagged");

        var changed = settings.Value.Clone();
        changed.TitleNamespace = "book:";
        changed.SetNamespace = "album:";
        changed.PageNamespace = "leaf:";
        changed.IndexNamespace = "position:";
        changed.AlternatePageNamespace = "attribute:";
        settings.Value = changed;
        handler.Files.Clear();
        handler.Files.Add(File(12, "custom", "album:mapped", "leaf:9", "position:5", "attribute:detail"));
        var custom = await api.DiscoverCollectionsAsync();
        Check(custom.Single() == new CollectionIdentity("mapped", CollectionKind.Imageset), "discovery respects custom namespaces");
        await sync.SyncCollectionAsync("mapped", CollectionKind.Imageset);
        var customImage = ImagesetImage.FromCollection(await ReadCollection(factory, "mapped", CollectionKind.Imageset)).Single();
        Check(customImage.Index == 5 && customImage.Labels.Single() == "detail", "custom order and variant namespaces parsed");

        var persistedSettings = new HydrusSettingsService(Options.Create(new HydrusSettings()), new TestHttpFactory(handler), new EphemeralDataProtectionProvider(), factory);
        await persistedSettings.SaveSettingsAsync(changed);
        var loadedSettings = await persistedSettings.GetSettingsAsync();
        Check(loadedSettings.SetNamespace == "album:" && loadedSettings.IndexNamespace == "position:" && loadedSettings.TitleNamespace == "book:", "new namespace settings survive persistence");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await sync.SyncLibraryAsync(cancellationToken: cancellation.Token);
            throw new InvalidOperationException("Canceled sync unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
            Check(true, "sync honors cancellation");
        }
        Console.WriteLine($"PASS: {_checks} collection regression checks.");
    }

    private static FileMetadata File(long id, string hash, params string[] tags) => new()
    {
        FileId = id,
        Hash = hash,
        MimeType = "image/png",
        Tags = new() { ["primary"] = new ServiceTagBucket { StorageTags = new() { ["0"] = tags.ToList() } } }
    };

    private static async Task<ComicsRecord> ReadCollection(CacheFactory factory, string title, CollectionKind kind)
    {
        await using var db = factory.CreateDbContext();
        return await db.Comic.AsNoTracking().Include(collection => collection.Chapters)
            .ThenInclude(chapter => chapter.Pages).ThenInclude(page => page.Variants)
            .SingleAsync(collection => collection.Title == title && collection.Kind == kind);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {description}");
        _checks++;
        Console.WriteLine($"PASS: {description}");
    }
}

internal sealed class CacheFactory(DbContextOptions<SettingsDbContext> options) : IDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext() => new(options);
}

internal sealed class TestSettings : IHydrusSettingsService
{
    public HydrusSettings Value { get; set; } = new() { ApiUrl = "http://hydrus.test", TagServiceKey = "primary" };
    public Task<HydrusSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Value.Clone());
    }
    public Task SaveSettingsAsync(HydrusSettings settings, CancellationToken cancellationToken = default)
    {
        Value = settings.Clone();
        return Task.CompletedTask;
    }
    public Task<HydrusServiceCatalog> GetServicesAsync(HydrusSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(new HydrusServiceCatalog());
}

internal sealed class TestHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class HydrusHandler : HttpMessageHandler
{
    public List<FileMetadata> Files { get; } = [];
    public List<string> Searches { get; } = [];
    public int Writes { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
        switch (request.RequestUri.AbsolutePath)
        {
            case "/get_files/search_files":
                var raw = query["tags"].ToString();
                Searches.Add(raw);
                using (var predicates = JsonDocument.Parse(raw))
                {
                    var matching = Files.Where(file => predicates.RootElement.EnumerateArray().All(predicate => Matches(predicate, file.GetAllStorageTags())));
                    return Json(new { file_ids = matching.Select(file => file.FileId).ToArray() });
                }
            case "/get_files/file_metadata":
                IEnumerable<FileMetadata> selected;
                if (query.TryGetValue("file_ids", out var ids))
                {
                    var requested = JsonSerializer.Deserialize<long[]>(ids.ToString())!;
                    selected = Files.Where(file => requested.Contains(file.FileId));
                }
                else
                {
                    var requested = JsonSerializer.Deserialize<string[]>(query["hashes"].ToString())!;
                    selected = Files.Where(file => requested.Contains(file.Hash));
                }
                return Json(new { metadata = selected.ToArray() });
            case "/add_tags/add_tags":
                Writes++;
                using (var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)))
                {
                    var root = payload.RootElement;
                    var file = Files.Single(file => file.Hash == root.GetProperty("hash").GetString());
                    if (root.TryGetProperty("service_keys_to_actions_to_tags", out var services))
                    {
                        foreach (var service in services.EnumerateObject())
                        {
                            var tags = file.Tags[service.Name].StorageTags["0"];
                            foreach (var action in service.Value.EnumerateObject())
                            {
                                foreach (var tag in action.Value.EnumerateArray().Select(tag => tag.GetString()!))
                                {
                                    if (action.Name == "1") tags.RemoveAll(existing => existing.Equals(tag, StringComparison.OrdinalIgnoreCase));
                                    else if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var service in root.GetProperty("service_keys_to_tags").EnumerateObject())
                            file.Tags[service.Name].StorageTags["0"].AddRange(service.Value.EnumerateArray().Select(tag => tag.GetString()!));
                    }
                }
                return Json(new { });
            default:
                throw new InvalidOperationException($"Unexpected Hydrus request: {request.RequestUri}");
        }
    }

    private static bool Matches(JsonElement predicate, IReadOnlyList<string> tags)
    {
        if (predicate.ValueKind == JsonValueKind.Array)
            return predicate.EnumerateArray().Any(child => Matches(child, tags));
        var text = predicate.GetString()!;
        return text.EndsWith('*')
            ? tags.Any(tag => tag.StartsWith(text[..^1], StringComparison.OrdinalIgnoreCase))
            : tags.Contains(text, StringComparer.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };
}
