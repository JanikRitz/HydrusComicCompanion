using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HydrusComicCompanion.Components.ImportWizard.State;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        Check(editedTags.Contains("medium:imageset"), "imageset save applies the default imageset medium tag");
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

        handler.Files.Add(File(23, "migrate-a", "comic:migrate", "volume:1", "chapter:1", "page:1", "medium:comic"));
        handler.Files.Add(File(24, "migrate-b", "comic:migrate", "volume:1", "chapter:2", "page:1", "medium:comic"));
        await sync.SyncCollectionAsync("migrate", CollectionKind.Comic);
        var migratingComic = await ReadCollection(factory, "migrate", CollectionKind.Comic);
        var migrationPreparation = await sync.ExtractComicAsync("migrate");
        var migrationEdit = new HydrusMetadataEditRequest
        {
            ComicId = migratingComic.Id,
            Kind = CollectionKind.Imageset,
            HydrusTitle = "migrate",
            CoverFileHash = "migrate-a",
            Pages = migrationPreparation.Pages
                .Select((page, index) => new ImportPage
                {
                    Index = page.Index,
                    ArchiveFileName = page.ArchiveFileName,
                    Data = page.Data,
                    Sha256Hash = page.Sha256Hash,
                    MimeType = page.MimeType,
                    PageNumber = index + 1,
                    GapBefore = page.GapBefore,
                    LogicalPageGroupId = page.LogicalPageGroupId,
                    IsDefaultVariant = page.IsDefaultVariant,
                    VariantLabel = page.VariantLabel
                })
                .ToList(),
            ChapterStartPageIndices = migrationPreparation.ChapterStartPageIndices
        };
        await sync.ApplyMetadataEditAsync(migrationEdit);
        var migratedTagsA = handler.Files.Single(file => file.Hash == "migrate-a").GetAllStorageTags();
        var migratedTagsB = handler.Files.Single(file => file.Hash == "migrate-b").GetAllStorageTags();
        Check(migratedTagsA.Contains("set:migrate") && migratedTagsA.Contains("index:1") && !migratedTagsA.Contains("comic:migrate") && !migratedTagsA.Contains("chapter:1"), "comic-to-imageset migration swaps title and structural namespaces");
        Check(migratedTagsB.Contains("index:2") && !migratedTagsB.Contains("page:1") && !migratedTagsB.Contains("chapter:2"), "comic-to-imageset migration flattens chaptered page structure into imageset indices");
        Check(migratedTagsA.Contains("medium:imageset") && !migratedTagsA.Contains("medium:comic"), "comic-to-imageset migration swaps medium tags");
        var migratedImageset = await ReadCollection(factory, "migrate", CollectionKind.Imageset);
        Check(ImagesetImage.FromCollection(migratedImageset).Select(image => image.Index).SequenceEqual(new int?[] { 1, 2 }), "comic-to-imageset migration updates the cached collection kind and indices");

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
        changed.ComicMediumTag = "medium:graphic novel";
        changed.ImagesetMediumTag = "medium:album";
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
        Check(loadedSettings.SetNamespace == "album:" && loadedSettings.IndexNamespace == "position:" && loadedSettings.TitleNamespace == "book:" && loadedSettings.ComicMediumTag == "medium:graphic novel" && loadedSettings.ImagesetMediumTag == "medium:album", "new namespace and medium tag settings survive persistence");
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
        await CheckDuplicateRecoveryAsync(factory);
        await CheckNewCollectionImportAsync();
        await CheckManagerCatalogAsync();
        CheckReaderVariantPinning();
        var mediaUrls = new HydrusMediaService(api);
        Check(mediaUrls.BuildThumbnailUrl("a/b") == "media/thumbnail/a%2Fb"
            && mediaUrls.BuildRenderUrl("abc", 100, 200) == "media/render/abc?width=100&height=200"
            && mediaUrls.BuildOriginalFileUrl("abc", true) == "media/file/abc?download=true",
            "media URLs are escaped and relative to the application base");
        await CheckPathBaseAsync(null);
        await CheckPathBaseAsync(null, httpsRedirect: true);
        await CheckPathBaseAsync("/managed/comiccompanion/test");
        await CheckPathBaseAsync("/managed/comiccompanion/test", httpsRedirect: true);
        Console.WriteLine($"PASS: {_checks} collection regression checks.");
    }

    private static async Task CheckPathBaseAsync(string? pathBase, bool httpsRedirect = false)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "comic-pathbase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(HydrusMediaService).Assembly.Location);
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(contentRoot);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = httpsRedirect ? "Production" : "Development";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["PSM_COMIC_PATH_BASE"] = pathBase ?? "";
        start.Environment["PSM_COMIC_SYNC_MINUTES"] = "";
        start.Environment["ASPNETCORE_HTTPS_PORT"] = httpsRedirect ? "5443" : "";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start companion host.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var prefix = pathBase ?? "";
            HttpResponseMessage? response = null;
            while (response is null)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException("Companion host exited before accepting requests.");
                try { response = await client.GetAsync(prefix + "/", timeout.Token); }
                catch (HttpRequestException) { await Task.Delay(100, timeout.Token); }
            }
            using (response)
            {
                if (httpsRedirect && pathBase is null)
                {
                    Check(response.StatusCode == HttpStatusCode.TemporaryRedirect
                        && response.Headers.Location?.ToString() == $"https://127.0.0.1:5443{prefix}/",
                        "standalone HTTPS redirect remains enabled");
                    return;
                }
                if (httpsRedirect)
                    Check(response.IsSuccessStatusCode && !response.Headers.Contains("Strict-Transport-Security"),
                        "managed hosting does not redirect to the upstream HTTPS port or set HSTS");
                var html = await response.Content.ReadAsStringAsync(timeout.Token);
                Check(response.IsSuccessStatusCode && html.Contains($"<base href=\"{prefix}/\"", StringComparison.Ordinal),
                    $"{prefix}/ serves the correct document base");
                var assets = Regex.Matches(html, "(?:src|href)=\"([^\"]+)\"")
                    .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
                    .Where(value => value.Contains("blazor.web.") || value.Contains("MudBlazor.min.") || value.Contains("ReconnectModal."))
                    .ToArray();
                Check(assets.Length >= 4, $"{prefix}/ emits framework, MudBlazor, and reconnect assets");
                foreach (var asset in assets)
                {
                    using var assetResponse = await client.GetAsync(prefix + "/" + asset, timeout.Token);
                    Check(assetResponse.IsSuccessStatusCode, $"{prefix}/ serves {asset}");
                }
            }

            using var spoofed = new HttpRequestMessage(HttpMethod.Get, prefix + "/settings");
            spoofed.Headers.Add("X-Forwarded-Prefix", "/untrusted");
            using var spoofedResponse = await client.SendAsync(spoofed, timeout.Token);
            Check((await spoofedResponse.Content.ReadAsStringAsync(timeout.Token)).Contains($"<base href=\"{prefix}/\"", StringComparison.Ordinal),
                $"{prefix}/ ignores an untrusted forwarded prefix");
            using var negotiate = await client.PostAsync(prefix + "/_blazor/negotiate?negotiateVersion=1", null, timeout.Token);
            Check(negotiate.IsSuccessStatusCode, $"{prefix}/ routes interactive SignalR negotiation");
            using var catalog = await client.GetAsync(prefix + "/api/manager/comics", timeout.Token);
            Check(catalog.IsSuccessStatusCode, $"{prefix}/ routes the manager API");
            using var thumbnail = new HttpRequestMessage(HttpMethod.Get, prefix + "/media/thumbnail/abc");
            thumbnail.Headers.TryAddWithoutValidation("If-None-Match", "\"thumb-abc\"");
            using var thumbnailResponse = await client.SendAsync(thumbnail, timeout.Token);
            Check(thumbnailResponse.StatusCode == HttpStatusCode.NotModified, $"{prefix}/ routes media requests");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(contentRoot, "App_Data", "settings.db"),
                Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<SettingsDbContext>().UseSqlite(connectionString).Options;
            int imagesetId;
            await using (var db = new SettingsDbContext(options))
            {
                var imageset = new ComicsRecord
                {
                    Kind = CollectionKind.Imageset,
                    Title = "Path base imageset",
                    Chapters = [new ChapterRecord { Pages = [new PageRecord
                    {
                        PageNumber = 1,
                        Variants = [new PageVariantRecord { FileHash = "fixturehash", ImageIndex = 1, IsDefault = true }]
                    }] }]
                };
                db.Comic.Add(imageset);
                await db.SaveChangesAsync(timeout.Token);
                imagesetId = imageset.Id;
            }
            var gallery = await client.GetStringAsync(prefix + $"/collection/{imagesetId}", timeout.Token);
            Check(gallery.Contains($"href=\"collection/{imagesetId}/image/fixturehash\"", StringComparison.Ordinal)
                && gallery.Contains("media/render/fixturehash", StringComparison.Ordinal),
                $"{prefix}/ renders base-relative gallery and media links");
            var reader = await client.GetStringAsync(prefix + $"/collection/{imagesetId}/image/fixturehash", timeout.Token);
            Check(reader.Contains($"href=\"collection/{imagesetId}\"", StringComparison.Ordinal)
                && reader.Contains("media/render/fixturehash", StringComparison.Ordinal),
                $"{prefix}/ renders base-relative imageset reader links");
            using var notFound = await client.GetAsync(prefix + "/not-found", timeout.Token);
            Check((await notFound.Content.ReadAsStringAsync(timeout.Token)).Contains($"<base href=\"{prefix}/\"", StringComparison.Ordinal),
                $"{prefix}/ renders not-found navigation with the correct base");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static async Task CheckManagerCatalogAsync()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new CacheFactory(new DbContextOptionsBuilder<SettingsDbContext>().UseSqlite(connection).Options);
        await using (var db = factory.CreateDbContext())
            await db.Database.MigrateAsync();

        var settings = new TestSettings();
        using var handler = new HydrusHandler();
        var alternate = File(2, "alternate", "comic:original", "page:1", "variant:alternate", "creator:Bjork", "standalone");
        alternate.Notes[settings.Value.OcrTextNoteName] = "Grüße 100%_[literal]";
        var cover = File(1, "cover", "comic:original", "page:1", "variant:default");
        cover.Notes[settings.Value.FullTitleNoteName] = "Zulu %_ Edition";
        var setFile = File(4, "set", "set:original", "index:2");
        setFile.Tags["other"] = new ServiceTagBucket { StorageTags = new() { ["0"] = ["theme:set-exclusive"] } };
        setFile.Notes[settings.Value.OcrTextNoteName] = "Collection OCR 100%";
        handler.Files.AddRange([cover, alternate, File(3, "alpha", "comic:alpha", "page:1"), setFile,
            File(5, "first-image", "set:original", "index:2"), File(6, "a-unnumbered", "set:original", "page:1")]);
        using var hydrusHttp = new HttpClient(handler);
        var api = new HydrusApiService(hydrusHttp, settings, NullLogger<HydrusApiService>.Instance);
        var sync = new HydrusSyncService(api, factory, settings, NullLogger<HydrusSyncService>.Instance);

        Check(new string?[] { null, "", "0", "-1", "1.5", "2147483648" }.All(value => ManagerComicSync.ParseInterval(value) is null)
            && ManagerComicSync.ParseInterval("2") == TimeSpan.FromMinutes(2), "only a positive integer enables scheduled sync");

        await using var services = new ServiceCollection()
            .AddScoped<IHydrusSyncService>(_ => new HydrusSyncService(api, factory, settings, NullLogger<HydrusSyncService>.Instance))
            .BuildServiceProvider();
        using var worker = new ManagerComicSync(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ManagerComicSync>.Instance, TimeSpan.FromMilliseconds(25));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ranAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveries = 0;
        handler.BeforeSearchAsync = async (raw, token) =>
        {
            if (!raw.Contains("comic:*", StringComparison.Ordinal) || !raw.Contains("page:*", StringComparison.Ordinal))
                return;

            if (Interlocked.Increment(ref discoveries) == 1)
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
            }
            else
            {
                ranAgain.TrySetResult();
            }
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(timeout.Token);
        try
        {
            await started.Task.WaitAsync(timeout.Token);
            using var queuedCancellation = new CancellationTokenSource();
            var manual = sync.SyncComicAsync("original", queuedCancellation.Token);
            Check(!manual.IsCompleted && handler.Searches.Count == 1, "startup sync holds the gate across scopes before manual sync contacts Hydrus");
            await queuedCancellation.CancelAsync();
            try
            {
                await manual;
                Check(false, "queued manual sync can be canceled");
            }
            catch (OperationCanceledException)
            {
                Check(true, "queued manual sync can be canceled");
            }
            release.SetResult();
            await ranAgain.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            await worker.StopAsync(timeout.Token);
        }
        Check(discoveries >= 2 && handler.Posts == 0 && handler.NotesMetadataReads > 0,
            "scheduled sync runs at startup and on interval, requests notes, and sends no Hydrus writes");

        var catalog = await ReadCollection(factory, "original", CollectionKind.Comic);
        var imagesetCatalog = await ReadCollection(factory, "original", CollectionKind.Imageset);
        var chapterId = catalog.Chapters.Single().Id;
        Check(imagesetCatalog.CoverFileHash == "first-image", "sync falls back to the first ordered imageset image for the cover");
        await using (var db = factory.CreateDbContext())
        {
            (await db.Comic.SingleAsync(comic => comic.Title == "alpha")).LastSyncedAt = DateTimeOffset.UnixEpoch;
            var cachedImageset = await db.Comic.SingleAsync(collection => collection.Id == imagesetCatalog.Id);
            cachedImageset.LastSyncedAt = DateTimeOffset.UnixEpoch.AddSeconds(1);
            cachedImageset.CoverFileHash = null;
            var cachedPage = await db.Set<PageRecord>().SingleAsync(page => page.ChapterId == chapterId);
            cachedPage.PageNumber = 8;
            db.Set<PageRecord>().Add(new PageRecord { ChapterId = chapterId, PageNumber = 4 });
            db.Comic.Add(new ComicsRecord { Title = "middle", Kind = CollectionKind.Comic });
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Error);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IDbContextFactory<SettingsDbContext>>(factory);
        builder.Services.AddScoped<ManagerComicCatalog>();
        await using var app = builder.Build();
        ManagerComicCatalog.MapEndpoint(app);
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        const string route = "/api/manager/comics";

        async Task<string[]> TitlesAsync(string parameters)
        {
            using var json = JsonDocument.Parse(await client.GetStringAsync(route + parameters, timeout.Token));
            return json.RootElement.EnumerateArray().Select(item => item.GetProperty("title").GetString()!).ToArray();
        }

        using var payload = JsonDocument.Parse(await client.GetStringAsync(route + "?q=oRiGiN", timeout.Token));
        var row = payload.RootElement.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "Comic");
        Check(row.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(["coverHash", "id", "imageCount", "kind", "pageCount", "readerPath", "title", "updatedAt"])
            && row.GetProperty("id").GetInt32() == catalog.Id
            && row.GetProperty("title").GetString() == "Zulu %_ Edition"
            && row.GetProperty("pageCount").GetInt32() == 2
            && row.GetProperty("imageCount").ValueKind == JsonValueKind.Null
            && row.GetProperty("coverHash").GetString() == "cover"
            && row.GetProperty("updatedAt").GetDateTimeOffset() == catalog.LastSyncedAt
            && row.GetProperty("readerPath").GetString() == $"read/{catalog.Id}/{chapterId}/2",
            "manager comic payload preserves fields and uses the first readable page's position, not its cached number");
        var setRow = payload.RootElement.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "Imageset");
        Check(setRow.GetProperty("id").GetInt32() == imagesetCatalog.Id
            && setRow.GetProperty("title").GetString() == "original"
            && setRow.GetProperty("pageCount").ValueKind == JsonValueKind.Null
            && setRow.GetProperty("imageCount").GetInt32() == 3
            && setRow.GetProperty("coverHash").GetString() == "first-image"
            && setRow.GetProperty("readerPath").GetString() == $"collection/{imagesetCatalog.Id}/image/first-image",
            "manager imageset payload uses gallery order and falls back to the first image when no cover is cached");

        Check((await TitlesAsync("?q=%25_")).SequenceEqual(["Zulu %_ Edition"]), "display title search treats wildcard characters literally");
        Check((await TitlesAsync("?q=bJORK")).Length == 1 && (await TitlesAsync("?q=CREATOR%3Abjork")).Length == 1
            && (await TitlesAsync("?q=standalone")).Length == 1, "exact unqualified, qualified and bare tags match on an alternate page");
        Check((await TitlesAsync("?q=bjor")).Length == 0 && (await TitlesAsync("?q=other%3Abjork")).Length == 0
            && (await TitlesAsync("?q=set-exclus")).Length == 0, "partial tags and wrong namespaces do not match");
        Check((await TitlesAsync("?q=set-exclusive")).SequenceEqual(["original"])
            && (await TitlesAsync("?q=THEME%3Aset-exclusive")).SequenceEqual(["original"]), "imageset tags match across services");
        Check((await TitlesAsync("?q=" + Uri.EscapeDataString("GRÜßE 100%_[LITERAL]"))).Length == 1,
            "case-insensitive literal OCR search includes alternate variants");
        Check((await TitlesAsync("?q=collection%20ocr%20100%25")).SequenceEqual(["original"]), "imageset OCR matches on images other than the reader entry");
        Check((await TitlesAsync("?q=")).SequenceEqual(["alpha", "middle", "original", "Zulu %_ Edition"])
            && (await TitlesAsync("?sort=TitleAscending&limit=1")).SequenceEqual(["alpha"])
            && (await TitlesAsync("?sort=TitleDescending&limit=1")).SequenceEqual(["Zulu %_ Edition"])
            && (await TitlesAsync("?sort=DateNewest")).SequenceEqual(["Zulu %_ Edition", "original", "alpha", "middle"])
            && (await TitlesAsync("?sort=DateOldest")).SequenceEqual(["alpha", "original", "Zulu %_ Edition", "middle"])
            && (await TitlesAsync("?sort=random&limit=2")).Distinct().Count() == 2, "browse, all sort modes and result limits work across both kinds");
        Check((await TitlesAsync("?q=origin&sort=TitleAscending&limit=1")).SequenceEqual(["original"])
            && (await TitlesAsync("?q=origin&sort=TitleAscending&limit=1&kind=cOmIc")).SequenceEqual(["Zulu %_ Edition"])
            && (await TitlesAsync("?q=origin&sort=TitleDescending&limit=1&kind=Imageset")).SequenceEqual(["original"])
            && (await TitlesAsync("?q=middle&kind=Imageset")).Length == 0,
            "kind filters matching collections before applying the limit, while omission keeps the combined results");
        using var nullable = JsonDocument.Parse(await client.GetStringAsync(route + "?q=middle", timeout.Token));
        Check(nullable.RootElement[0].GetProperty("coverHash").ValueKind == JsonValueKind.Null
            && nullable.RootElement[0].GetProperty("updatedAt").ValueKind == JsonValueKind.Null
            && nullable.RootElement[0].GetProperty("readerPath").ValueKind == JsonValueKind.Null, "missing cover, timestamp and readable files serialize as null");
        foreach (var invalid in new[] { "?limit=0", "?limit=bad", "?sort=1", "?sort=unknown", "?kind=", "?kind=1", "?kind=unknown", "?kind=Comic,Imageset" })
        {
            using var response = await client.GetAsync(route + invalid, timeout.Token);
            Check(response.StatusCode == HttpStatusCode.BadRequest, $"manager rejects {invalid}");
        }
    }

    private static void CheckReaderVariantPinning()
    {
        var selection = new ComicReaderVariantSelection();
        selection.SetComic(1);
        var firstDefault = new PageVariantRecord { Id = 1, FileHash = "first-default", IsDefault = true };
        var firstAlternate = new PageVariantRecord { Id = 2, FileHash = "first-alternate", Label = "no text" };
        var firstPage = new PageRecord { Id = 1, ChapterId = 1, Variants = [firstAlternate, firstDefault] };
        var secondDefault = new PageVariantRecord { Id = 3, FileHash = "second-default", IsDefault = true, Label = "default" };
        var secondAlternate = new PageVariantRecord { Id = 4, FileHash = "second-alternate", Label = "NO TEXT" };
        var secondPage = new PageRecord { Id = 2, ChapterId = 2, Variants = [secondAlternate, secondDefault] };

        Check(selection.Select(firstPage) == firstDefault, "reader initially prefers the default variant");
        Check(selection.Select(secondPage) == secondDefault, "unpinned forward navigation shows the default");
        Check(selection.Select(firstPage) == firstDefault, "unpinned backward navigation shows the default on a revisited page");
        selection.Pin(firstAlternate);
        Check(selection.IsPinned && selection.PinnedDisplayName == "no text", "reader pins the alternate label");
        Check(selection.Select(secondPage) == secondAlternate, "pin matches labels case-insensitively across chapters");
        selection.SetComic(1);
        Check(selection.IsPinned && selection.Select(firstPage) == firstAlternate, "navigation within the same comic preserves the pin");
        Check(selection.Select(secondPage) == secondAlternate, "pinned forward navigation continues showing the alternate");

        var missingPage = new PageRecord { Id = 3, Variants = [new PageVariantRecord { Id = 5, FileHash = "missing", IsDefault = true }] };
        Check(selection.Select(missingPage) == missingPage.Variants.Single() && selection.IsPinned,
            "missing pinned variant falls back to the default without clearing the pin");
        Check(!missingPage.Variants.Any(selection.MatchesPin) && selection.Select(secondPage) == secondAlternate,
            "missing-label notice can be detected and the pin resumes on subsequent pages");
        var hashFirst = new PageVariantRecord { Id = 99, FileHash = "a", Label = "other" };
        var hashLast = new PageVariantRecord { Id = 6, FileHash = "z", Label = "other" };
        var fallbackPage = new PageRecord { Id = 4, Variants = [hashLast, hashFirst] };
        Check(selection.Select(fallbackPage) == hashFirst, "pin fallback without a default uses deterministic hash ordering rather than IDs");
        hashFirst.Label = "no text";
        hashLast.Label = "no text";
        Check(selection.Select(fallbackPage) == hashFirst, "duplicate pinned labels resolve deterministically by hash");
        hashFirst.IsDefault = true;
        Check(selection.Select(fallbackPage) == hashFirst, "a matching alternate label remains selectable when marked as the page default");
        Check(selection.Select(new PageRecord { Id = 5 }) is null && selection.IsPinned, "empty pages do not discard a pin");

        selection.Unpin();
        Check(!selection.IsPinned && selection.Select(secondPage) == secondDefault,
            "unpinning shows the default on forward navigation to a previously pinned page");
        Check(selection.Select(firstPage) == firstDefault,
            "unpinning shows the default on backward navigation to a previously pinned page");
        selection.Pin(firstDefault);
        Check(selection.PinnedDisplayName == "Default" && selection.Select(secondPage) == secondDefault,
            "pinning default follows the default flag regardless of label");
        Check(selection.Select(fallbackPage) == hashFirst, "default pin follows a default carrying an alternate label");

        var unlabeled = new PageVariantRecord { Id = 7, FileHash = "unlabeled" };
        Check(!ComicReaderVariantSelection.CanPin(unlabeled) && ComicReaderVariantSelection.CanPin(firstDefault),
            "only defaults or labeled alternates can be pinned");
        try
        {
            selection.Pin(unlabeled);
            throw new InvalidOperationException("Unlabeled alternate unexpectedly pinned.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("needs a label", StringComparison.Ordinal))
        {
            Check(selection.IsPinned && selection.PinnedDisplayName == "Default", "invalid pin leaves the existing pin intact");
        }

        selection.SetComic(2);
        Check(!selection.IsPinned && selection.Select(firstPage) == firstDefault,
            "changing comics clears the pin and shows the default");
    }

    private static async Task CheckNewCollectionImportAsync()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new CacheFactory(new DbContextOptionsBuilder<SettingsDbContext>().UseSqlite(connection).Options);
        await using (var db = factory.CreateDbContext())
            await db.Database.MigrateAsync();

        var settings = new TestSettings();
        settings.Value.TitleNamespace = "book:";
        settings.Value.SetNamespace = "album:";
        settings.Value.PageNamespace = "leaf:";
        settings.Value.IndexNamespace = "position:";
        using var handler = new HydrusHandler();
        using var http = new HttpClient(handler);
        var api = new HydrusApiService(http, settings, NullLogger<HydrusApiService>.Instance);
        var sync = new HydrusSyncService(api, factory, settings, NullLogger<HydrusSyncService>.Instance);
        Check(await sync.ImportNewCollectionsAsync() == 0, "new collection import handles an empty Hydrus library");

        await using (var db = factory.CreateDbContext())
        {
            db.Comic.Add(new ComicsRecord { Title = "shared", Kind = CollectionKind.Comic, DisplayTitle = "Keep local title" });
            await db.SaveChangesAsync();
        }
        var cover = File(201, "new-cover", "book:new", "volume:2", "chapter:3", "leaf:1", "variant:default", "meta:cover page");
        cover.Notes[settings.Value.FullTitleNoteName] = "Full new title";
        cover.Notes[settings.Value.ComicCommentNoteName] = "Collection comment";
        cover.Notes[settings.Value.OcrTextNoteName] = "Cover OCR";
        cover.Tags["other"] = new ServiceTagBucket { StorageTags = new() { ["0"] = ["creator:someone", "genre:adventure"] } };
        handler.Files.AddRange([
            File(200, "existing", "book:shared", "leaf:1"),
            cover,
            File(202, "new-page", "book:new", "volume:2", "chapter:3", "leaf:2"),
            File(203, "new-alternate", "book:new", "volume:2", "chapter:3", "leaf:1", "variant:no text"),
            File(204, "new-set", "album:shared", "position:4", "variant:detail")
        ]);
        handler.Searches.Clear();
        var updates = new List<SyncProgressUpdate>();
        Check(await sync.ImportNewCollectionsAsync(new InlineProgress<SyncProgressUpdate>(updates.Add)) == 2,
            "missing-only import loads new comics and same-title imagesets with configured namespaces");
        Check(updates.Select(update => update.Current).SequenceEqual([0, 1, 2]) && updates.All(update => update.Total == 2),
            "import progress counts only missing collections");
        var existing = await ReadCollection(factory, "shared", CollectionKind.Comic);
        Check(existing.DisplayTitle == "Keep local title" && existing.LastSyncedAt is null && existing.Chapters.Count == 0,
            "import leaves existing records untouched even without a sync timestamp");
        Check(!handler.Searches.Contains("[\"book:shared\"]"), "import never performs a full fetch for known comics");
        var comic = await ReadCollection(factory, "new", CollectionKind.Comic);
        Check(comic.DisplayTitle == "Full new title" && comic.Comment == "Collection comment" && comic.CoverFileHash == "new-cover" && comic.LastSyncedAt.HasValue,
            "new comic imports cover, display title, comment and sync timestamp");
        var chapter = comic.Chapters.Single();
        Check(chapter.VolumeNumber == 2 && chapter.ChapterNumber == 3 && chapter.Pages.Count == 2,
            "new comic imports the full volume/chapter/page hierarchy");
        var variants = chapter.Pages.Single(page => page.PageNumber == 1).Variants;
        Check(variants.Single(variant => variant.IsDefault).FileHash == "new-cover"
            && variants.Single(variant => variant.FileHash == "new-alternate").Label == "no text"
            && variants.Single(variant => variant.FileHash == "new-cover").OcrText == "Cover OCR",
            "new comic imports alternate labels, default variant and existing OCR notes");
        await using (var db = factory.CreateDbContext())
            Check(await db.Metadata.AnyAsync(metadata => metadata.SeriesId == comic.Id && metadata.Key == "genre" && metadata.Value == "adventure"),
                "new comic imports non-structural metadata tags");
        var image = ImagesetImage.FromCollection(await ReadCollection(factory, "shared", CollectionKind.Imageset)).Single();
        Check(image.Index == 4 && image.Labels.Single() == "detail", "new imageset imports index and variant attributes");
        handler.Searches.Clear();
        Check(await sync.ImportNewCollectionsAsync() == 0 && handler.Searches.Count == 2,
            "repeating import performs discovery only and imports no duplicates");
        Check(handler.Writes == 0, "discovery and missing-only import never write to Hydrus");

        handler.Files.AddRange([
            File(205, "resume-a", "book:resume-a", "leaf:1"),
            File(206, "resume-b", "book:resume-b", "leaf:1")
        ]);
        using var cancellation = new CancellationTokenSource();
        var stop = new InlineProgress<SyncProgressUpdate>(update =>
        {
            if (update.Current == 2) cancellation.Cancel();
        });
        try
        {
            await sync.ImportNewCollectionsAsync(stop, cancellation.Token);
            throw new InvalidOperationException("Canceled import unexpectedly completed.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await using var db = factory.CreateDbContext();
            Check(await db.Comic.AnyAsync(collection => collection.Title == "resume-a")
                && !await db.Comic.AnyAsync(collection => collection.Title == "resume-b"),
                "stopping import preserves completed collections and skips remaining work");
        }
        handler.FailingSearchTag = "book:resume-b";
        try
        {
            await sync.ImportNewCollectionsAsync();
            throw new InvalidOperationException("Failed Hydrus import unexpectedly completed.");
        }
        catch (HttpRequestException)
        {
            await using var db = factory.CreateDbContext();
            Check(!await db.Comic.AnyAsync(collection => collection.Title == "resume-b"), "Hydrus failures propagate without caching an incomplete collection");
        }
        handler.FailingSearchTag = null;
        Check(await sync.ImportNewCollectionsAsync() == 1
            && (await ReadCollection(factory, "resume-b", CollectionKind.Comic)).Chapters.Single().Pages.Single().Variants.Single().FileHash == "resume-b",
            "retry imports only the remaining collection after cancellation or failure");
    }

    private static async Task CheckDuplicateRecoveryAsync(CacheFactory factory)
    {
        var settings = new TestSettings();
        settings.Value.ApiAccessKey = "test-key";
        settings.Value.FullTitleNoteName = "full title";
        using var handler = new HydrusHandler();
        using var http = new HttpClient(handler);
        var api = new HydrusApiService(http, settings, NullLogger<HydrusApiService>.Instance);
        var sync = new HydrusSyncService(api, factory, settings, NullLogger<HydrusSyncService>.Instance);
        var importer = new ComicImportService(api, factory, settings, NullLogger<ComicImportService>.Instance, sync);
        handler.Files.AddRange([File(100, "incoming"), File(101, "king", "keep:this"), File(102, "alternate")]);

        ComicImportRequest Request(string title) => new()
        {
            SeriesName = title,
            DisplayTitle = "Full title",
            Pages = [new ImportPage { Data = [1, 2, 3], Sha256Hash = "incoming", MimeType = "image/jpeg", PageNumber = 1 }]
        };
        HydrusAddFileResult Deleted() => new() { Status = 3, Hash = "incoming", Note = "Previously deleted" };
        Dictionary<string, object?> Relationship(string? king, bool local, bool onDomain) => new()
        {
            ["is_king"] = king == "incoming",
            ["king"] = king,
            ["king_is_local"] = local,
            ["king_is_on_file_domain"] = onDomain,
            ["0"] = new[] { "alternate" },
            ["1"] = new[] { "alternate" },
            ["3"] = new[] { "alternate" },
            ["8"] = new[] { "king" }
        };

        handler.Relationships["INCOMING"] = Relationship("king", true, true);
        handler.AddResults.Enqueue(Deleted());
        await importer.ImportComicAsync(Request("duplicate king"));
        var cached = await ReadCollection(factory, "duplicate king", CollectionKind.Comic);
        var variant = cached.Chapters.Single().Pages.Single().Variants.Single();
        Check(variant.FileHash == "king" && variant.MimeType == "image/png" && cached.CoverFileHash == "king", "recovery caches the king hash, MIME and cover rather than the uploaded image");
        Check(handler.Files.Single(file => file.Hash == "king").GetAllStorageTags().Contains("comic:duplicate king")
            && handler.Files.Single(file => file.Hash == "king").GetAllStorageTags().Contains("keep:this"), "import tags the king and preserves existing tags");
        Check(handler.Files.Single(file => file.Hash == "king").Notes["full title"] == "Full title", "cover notes target the king");
        Check(handler.Files.Single(file => file.Hash == "alternate").GetAllStorageTags().Count == 0
            && handler.Files.Single(file => file.Hash == "incoming").GetAllStorageTags().Count == 0, "alternates, potentials, false positives and the deleted input are not tagged");
        Check(handler.ClearedDeletionRecords.Count == 0 && handler.Searches.Count == 0, "king recovery does not clear deletion records or search structural tags");
        Check(handler.RelationshipRequests.Single().Query == "?hash=incoming" && handler.RelationshipApiKey == "test-key", "relationship lookup uses the hash, API key and default domain");

        foreach (var (name, king, local, domain) in new (string, string?, bool, bool)[]
        {
            ("nonlocal king", "king", false, true),
            ("out of domain king", "king", true, false),
            ("null king", null, false, false),
            ("singleton", "incoming", false, false)
        })
        {
            handler.Relationships["INCOMING"] = Relationship(king, local, domain);
            handler.AddResults.Enqueue(Deleted());
            handler.AddResults.Enqueue(new HydrusAddFileResult { Status = 1, Hash = "incoming" });
            var cleared = handler.ClearedDeletionRecords.Count;
            await importer.ImportComicAsync(Request(name));
            var restored = await ReadCollection(factory, name, CollectionKind.Comic);
            Check(handler.ClearedDeletionRecords.Count == cleared + 1 && handler.ClearedDeletionRecords[^1] == "incoming"
                && restored.CoverFileHash == "incoming", $"{name} falls back to verified original-file recovery");
        }

        handler.Relationships["INCOMING"] = Relationship("incoming", true, true);
        handler.AddResults.Enqueue(Deleted());
        var clearsBefore = handler.ClearedDeletionRecords.Count;
        await importer.ImportComicAsync(Request("local singleton"));
        Check(handler.ClearedDeletionRecords.Count == clearsBefore, "available self-king is reused without clearing the deletion record");

        async Task ExpectFailure(string title, string message)
        {
            var writes = handler.Writes;
            var cleared = handler.ClearedDeletionRecords.Count;
            handler.AddResults.Enqueue(Deleted());
            try
            {
                await importer.ImportComicAsync(Request(title));
                throw new InvalidOperationException("Import unexpectedly completed.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase))
            {
                Check(handler.Writes == writes && handler.ClearedDeletionRecords.Count == cleared, $"{title} stops before restoring or tagging");
            }
        }

        handler.RelationshipStatus = HttpStatusCode.Forbidden;
        await ExpectFailure("permission denied", "Manage File Relationships");
        handler.RelationshipStatus = HttpStatusCode.OK;
        handler.Relationships.Clear();
        await ExpectFailure("missing relationship", "no file relationships");
        handler.Relationships["INCOMING"] = Relationship("missing", true, true);
        handler.AddResults.Enqueue(Deleted());
        handler.AddResults.Enqueue(new HydrusAddFileResult { Status = 1, Hash = "incoming" });
        var clearsBeforeMissingKing = handler.ClearedDeletionRecords.Count;
        await importer.ImportComicAsync(Request("missing king metadata"));
        Check(handler.ClearedDeletionRecords.Count == clearsBeforeMissingKing + 1
            && handler.ClearedDeletionRecords[^1] == "incoming", "missing king metadata falls back to clearing the deletion record");

        handler.Relationships["INCOMING"] = Relationship(null, false, false);
        handler.AddResults.Enqueue(Deleted());
        handler.AddResults.Enqueue(Deleted());
        var writesBefore = handler.Writes;
        try
        {
            await importer.ImportComicAsync(Request("failed restore"));
            throw new InvalidOperationException("Import unexpectedly completed.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("could not restore"))
        {
            Check(handler.Writes == writesBefore, "failed deletion-record recovery does not tag an unavailable file");
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await api.GetFileRelationshipsAsync("incoming", canceled.Token);
            throw new InvalidOperationException("Canceled relationship lookup unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
            Check(true, "relationship lookup honors cancellation");
        }
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

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
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
    public string? FailingSearchTag { get; set; }
    public Func<string, CancellationToken, Task>? BeforeSearchAsync { get; set; }
    public int Writes { get; private set; }
    public int Posts { get; private set; }
    public int NotesMetadataReads { get; private set; }
    public Dictionary<string, object> Relationships { get; } = [];
    public HttpStatusCode RelationshipStatus { get; set; } = HttpStatusCode.OK;
    public List<Uri> RelationshipRequests { get; } = [];
    public string? RelationshipApiKey { get; private set; }
    public Queue<HydrusAddFileResult> AddResults { get; } = [];
    public List<string> Undeleted { get; } = [];
    public List<string> ClearedDeletionRecords { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method == HttpMethod.Post) Posts++;
        var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
        switch (request.RequestUri.AbsolutePath)
        {
            case "/manage_file_relationships/get_file_relationships":
                RelationshipRequests.Add(request.RequestUri);
                RelationshipApiKey = request.Headers.TryGetValues("Hydrus-Client-API-Access-Key", out var keys) ? keys.Single() : null;
                return RelationshipStatus == HttpStatusCode.OK
                    ? Json(new { file_relationships = Relationships })
                    : new HttpResponseMessage(RelationshipStatus);
            case "/add_files/add_file":
                return Json(AddResults.Dequeue());
            case "/add_files/clear_file_deletion_record":
                using (var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)))
                    ClearedDeletionRecords.Add(payload.RootElement.GetProperty("hash").GetString()!);
                return Json(new { });
            case "/add_files/undelete_files":
                using (var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)))
                    Undeleted.AddRange(payload.RootElement.GetProperty("hashes").EnumerateArray().Select(hash => hash.GetString()!));
                return Json(new { });
            case "/add_notes/set_notes":
                using (var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)))
                {
                    var file = Files.Single(file => file.Hash == payload.RootElement.GetProperty("hash").GetString());
                    foreach (var note in payload.RootElement.GetProperty("notes").EnumerateObject())
                        file.Notes[note.Name] = note.Value.GetString()!;
                    Writes++;
                    return Json(new { notes = file.Notes });
                }
            case "/get_files/search_files":
                var raw = query["tags"].ToString();
                Searches.Add(raw);
                if (BeforeSearchAsync is not null)
                    await BeforeSearchAsync(raw, cancellationToken);
                if (FailingSearchTag is not null && raw == JsonSerializer.Serialize(new[] { FailingSearchTag }))
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                using (var predicates = JsonDocument.Parse(raw))
                {
                    var matching = Files.Where(file => predicates.RootElement.EnumerateArray().All(predicate => Matches(predicate, file.GetAllStorageTags())));
                    return Json(new { file_ids = matching.Select(file => file.FileId).ToArray() });
                }
            case "/get_files/file_metadata":
                if (query["include_notes"] == "true") NotesMetadataReads++;
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
