using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HydrusComicCompanion.Services;

public class HydrusSyncService : IHydrusSyncService
{
    private readonly IHydrusApiService _apiService;
    private readonly IDbContextFactory<SettingsDbContext> _dbContextFactory;
    private readonly IHydrusSettingsService _settingsService;
    private readonly ILogger<HydrusSyncService> _logger;

    public HydrusSyncService(
        IHydrusApiService apiService,
        IDbContextFactory<SettingsDbContext> dbContextFactory,
        IHydrusSettingsService settingsService,
        ILogger<HydrusSyncService> logger)
    {
        _apiService = apiService;
        _dbContextFactory = dbContextFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Performs a full library sync: discovers titles and syncs their chapter/page structure
    /// </summary>
    public async Task<int> SyncLibraryAsync(IProgress<SyncProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var syncedCount = 0;

        try
        {
            _logger.LogInformation("Starting library sync");

            // Step 1: Discover all titles
            var comicTitles = await _apiService.DiscoverComicsAsync(cancellationToken);
            _logger.LogInformation("Discovered {Count} titles", comicTitles.Count);

            progress?.Report(new SyncProgressUpdate { Current = 0, Total = comicTitles.Count });

            // Step 2: Sync each title
            for (var index = 0; index < comicTitles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var comicTitle = comicTitles[index];
                progress?.Report(new SyncProgressUpdate
                {
                    Current = index + 1,
                    Total = comicTitles.Count,
                    CurrentTitle = comicTitle
                });

                try
                {
                    var comicId = await SyncComicAsync(comicTitle, cancellationToken);
                    if (comicId.HasValue)
                    {
                        syncedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing title: {ComicTitle}", comicTitle);
                }
            }

            _logger.LogInformation("Completed library sync: {SyncedCount} titles synchronized", syncedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Library sync was canceled after syncing {SyncedCount} titles", syncedCount);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library sync");
            throw;
        }

        return syncedCount;
    }

    /// <summary>
    /// Syncs only titles that already exist in the local cache database.
    /// </summary>
    public async Task<int> SyncExistingLibrariesAsync(IProgress<SyncProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var syncedCount = 0;

        try
        {
            _logger.LogInformation("Starting existing libraries sync");

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existingComicTitles = await dbContext.Comic
                .AsNoTracking()
                .Select(s => s.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} existing titles in local cache", existingComicTitles.Count);

            progress?.Report(new SyncProgressUpdate { Current = 0, Total = existingComicTitles.Count });

            for (var index = 0; index < existingComicTitles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var comicTitle = existingComicTitles[index];
                progress?.Report(new SyncProgressUpdate
                {
                    Current = index + 1,
                    Total = existingComicTitles.Count,
                    CurrentTitle = comicTitle
                });

                try
                {
                    var comicId = await SyncComicAsync(comicTitle, cancellationToken);
                    if (comicId.HasValue)
                    {
                        syncedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing existing title: {ComicTitle}", comicTitle);
                }
            }

            _logger.LogInformation("Completed existing libraries sync: {SyncedCount} titles synchronized", syncedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Existing libraries sync was canceled after syncing {SyncedCount} titles", syncedCount);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during existing libraries sync");
            throw;
        }

        return syncedCount;
    }

    /// <summary>
    /// Syncs only titles that already exist in the local cache database.
    /// </summary>
    public async Task<int> SyncOcrOnExistingLibrariesAsync(IOcrReader ocrReader, IProgress<SyncProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var syncedCount = 0;

        try
        {
            _logger.LogInformation("Starting ocr sync on known comics");

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existingComicTitles = await dbContext.Comic
                .AsNoTracking()
                .Select(s => s.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} existing titles in local cache", existingComicTitles.Count);

            progress?.Report(new SyncProgressUpdate { Current = 0, Total = existingComicTitles.Count });

            for (var index = 0; index < existingComicTitles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var comicTitle = existingComicTitles[index];
                progress?.Report(new SyncProgressUpdate
                {
                    Current = index + 1,
                    Total = existingComicTitles.Count,
                    CurrentTitle = comicTitle
                });

                try
                {
                    var comicId = await OcrSyncComic(ocrReader, comicTitle, cancellationToken);
                    
                    if (comicId.HasValue)
                    {
                        syncedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing existing title: {ComicTitle}", comicTitle);
                }
            }

            _logger.LogInformation("Completed existing libraries sync: {SyncedCount} titles synchronized", syncedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Existing libraries sync was canceled after syncing {SyncedCount} titles", syncedCount);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during existing libraries sync");
            throw;
        }

        return syncedCount;
    }

    /// <summary>
    /// Syncs a specific title: fetches all files tagged with the title and structures them.
    /// Implements fallback: if no files found with configured tag service, retries with default tag service.
    /// </summary>
    private async Task<int?> OcrSyncComic(IOcrReader reader, string comicTitle, CancellationToken cancellationToken)

    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        var normalizedComicTitle = NormalizeTitleName(comicTitle, settings);

        if (string.IsNullOrWhiteSpace(normalizedComicTitle))
        {
            throw new ArgumentException("Title name cannot be empty.", nameof(comicTitle));
        }

        try
        {
            _logger.LogInformation("Syncing OCR for title: {ComicTitle}", normalizedComicTitle);

            // Build the title search tag
            var titleTag = BuildTitleTag(normalizedComicTitle, settings);

            // Step 1: Search for files with this title tag
            var fileIds = await _apiService.SearchFilesAsync(new List<string> { titleTag }, cancellationToken: cancellationToken);

            // Fallback: if no files found with configured tag service, retry with default tag service
            if (fileIds.Count == 0 && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                _logger.LogInformation("No files found for title {ComicTitle} with configured tag service. Retrying with default tag service.", normalizedComicTitle);
                fileIds = await _apiService.SearchFilesAsync(new List<string> { titleTag }, fileDomain: null, skipTagService: true, cancellationToken: cancellationToken);
            }

            if (fileIds.Count == 0)
            {
                _logger.LogWarning("No files found for title: {ComicTitle}", normalizedComicTitle);
                return null;
            }

            _logger.LogInformation("Found {Count} files for title {ComicTitle}", fileIds.Count, normalizedComicTitle);

            var fileHashes = await _apiService.GetHashesAsync(fileIds, cancellationToken);

            foreach (var fileHash in fileHashes)
            {
                var path = await _apiService.GetFilePathAsync(fileHash, cancellationToken);
                var text = await reader.ReadOcrPlaintextForFileAsync(path, cancellationToken);

                if(string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) continue;

                // Set OCR text as note on Hydrus
                await _apiService.SetNotesAsync(fileHash, new Dictionary<string, string>() {{settings.OcrTextNoteName, text}});

                // Cache the OCR text in local database
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                
                var comic = await dbContext.Comic
                    .Include(s => s.Chapters)
                    .ThenInclude(c => c.Pages)
                    .ThenInclude(p => p.Variants)
                    .FirstOrDefaultAsync(s => s.Title == normalizedComicTitle, cancellationToken);

                if (comic != null)
                {
                    // Find the page variant matching this file hash and update OCR text
                    var variant = comic.Chapters
                        .SelectMany(c => c.Pages)
                        .SelectMany(p => p.Variants)
                        .FirstOrDefault(v => v.FileHash == fileHash);

                    if (variant != null)
                    {
                        variant.OcrText = text;
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            return fileIds.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing title: {ComicTitle}", normalizedComicTitle);
            throw;
        }
    }

    /// <summary>
    /// Extracts a Hydrus title into pages plus initial metadata/chapter state for the import UI.
    /// Implements fallback: if no files found with configured tag service, retries with default tag service.
    /// </summary>
    public async Task<ComicImportPreparation> ExtractComicAsync(string comicTitle, CancellationToken cancellationToken = default)
        => await ExtractComicAsync(comicTitle, sourceMapping: null, cancellationToken);

    public async Task<ComicImportPreparation> ExtractComicAsync(string comicTitle, HydrusSourceMapping? sourceMapping, CancellationToken cancellationToken = default)
    {
        var settings = ApplySourceMapping(await _settingsService.GetSettingsAsync(cancellationToken), sourceMapping);
        var normalizedComicTitle = NormalizeTitleName(comicTitle, settings);

        if (string.IsNullOrWhiteSpace(normalizedComicTitle))
        {
            throw new ArgumentException("Title name cannot be empty.", nameof(comicTitle));
        }

        var titleTag = BuildTitleTag(normalizedComicTitle, settings);
        var fileIds = await _apiService.SearchFilesAsync(
            settings,
            new List<string> { titleTag },
            cancellationToken: cancellationToken);

        // Fallback: if no files found with configured tag service, retry with default tag service
        if (fileIds.Count == 0 && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
        {
            _logger.LogInformation("No files found for title {ComicTitle} with configured tag service. Retrying with default tag service.", normalizedComicTitle);
            fileIds = await _apiService.SearchFilesAsync(
                settings,
                new List<string> { titleTag },
                fileDomain: null,
                skipTagService: true,
                cancellationToken: cancellationToken);
        }

        if (fileIds.Count == 0)
        {
            return new ComicImportPreparation
            {
                Metadata = new ComicMetadata { Series = normalizedComicTitle },
                ChapterStartPageIndices = [0]
            };
        }

        var fileMetadata = await _apiService.GetFileMetadataAsync(fileIds, cancellationToken: cancellationToken);
        return BuildImportPreparation(normalizedComicTitle, fileMetadata, settings);
    }

    /// <summary>
    /// Discovers titles in another tag service by applying a one-off mapping to the global settings and
    /// running the shared cover/first-page discovery query against the mapped tag service and namespaces.
    /// </summary>
    public async Task<List<TitleWithPageCount>> DiscoverMappedTitlesAsync(HydrusSourceMapping mapping, CancellationToken cancellationToken = default)
    {
        var settings = ApplySourceMapping(await _settingsService.GetSettingsAsync(cancellationToken), mapping);
        var titleNames = await _apiService.DiscoverComicsAsync(settings, cancellationToken);

        // If no minimum pages filter, return all titles with page count 0 (for UI display)
        if (mapping?.MinimumPages is null || mapping.MinimumPages <= 0)
        {
            return titleNames
                .Select(name => new TitleWithPageCount { Title = name, PageCount = 0 })
                .ToList();
        }

        // Fetch page counts for each title and filter
        var minimumPages = mapping.MinimumPages.Value;
        var titlesWithPageCounts = new List<TitleWithPageCount>();

        var titleNamespace = NormalizeNamespace(settings.TitleNamespace, "title:");
        var pageNamespace = NormalizeNamespace(settings.PageNamespace, "page:");

        foreach (var titleName in titleNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageCount = await _apiService.GetTitlePageCountAsync(settings, titleName, titleNamespace, pageNamespace, cancellationToken);

            if (pageCount >= minimumPages)
            {
                titlesWithPageCounts.Add(new TitleWithPageCount { Title = titleName, PageCount = pageCount });
            }
            else
            {
                _logger.LogDebug("Filtered out title '{ComicTitle}' with {PageCount} pages (minimum: {MinimumPages})", titleName, pageCount, minimumPages);
            }
        }

        return titlesWithPageCounts;
    }

    /// Only non-empty mapping fields override the global settings so blank inputs fall back safely.
    /// </summary>
    private static HydrusSettings ApplySourceMapping(HydrusSettings settings, HydrusSourceMapping? mapping)
    {
        if (mapping is null)
        {
            return settings;
        }

        var effective = settings.Clone();

        if (!string.IsNullOrWhiteSpace(mapping.TagServiceName))
        {
            effective.PrimaryTagService = mapping.TagServiceName.Trim();
            effective.TagServiceKey = mapping.TagServiceKey?.Trim() ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(mapping.TitleNamespace))
        {
            effective.TitleNamespace = mapping.TitleNamespace.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapping.VolumeNamespace))
        {
            effective.VolumeNamespace = mapping.VolumeNamespace.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapping.ChapterNamespace))
        {
            effective.ChapterNamespace = mapping.ChapterNamespace.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapping.PageNamespace))
        {
            effective.PageNamespace = mapping.PageNamespace.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapping.AlternatePageNamespace))
        {
            effective.AlternatePageNamespace = mapping.AlternatePageNamespace.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapping.AlternatePageDefaultValue))
        {
            effective.AlternatePageDefaultValue = mapping.AlternatePageDefaultValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mapping.CoverPageTag))
        {
            effective.CoverPageTag = mapping.CoverPageTag.Trim();
        }

        return effective;
    }

    /// <summary>
    /// Syncs a specific title: fetches all files tagged with the title and structures them.
    /// Implements fallback: if no files found with configured tag service, retries with default tag service.
    /// </summary>
    public async Task<int?> SyncComicAsync(string comicTitle, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        var normalizedComicTitle = NormalizeTitleName(comicTitle, settings);

        if (string.IsNullOrWhiteSpace(normalizedComicTitle))
        {
            throw new ArgumentException("Title name cannot be empty.", nameof(comicTitle));
        }

        try
        {
            _logger.LogInformation("Syncing title: {ComicTitle}", normalizedComicTitle);
            
            // Build the title search tag
            var titleTag = BuildTitleTag(normalizedComicTitle, settings);

            // Step 1: Search for files with this title tag
            var fileIds = await _apiService.SearchFilesAsync( new List<string> { titleTag }, cancellationToken: cancellationToken);

            // Fallback: if no files found with configured tag service, retry with default tag service
            if (fileIds.Count == 0 && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                _logger.LogInformation("No files found for title {ComicTitle} with configured tag service. Retrying with default tag service.", normalizedComicTitle);
                fileIds = await _apiService.SearchFilesAsync(new List<string> { titleTag }, fileDomain: null, skipTagService: true, cancellationToken: cancellationToken);
            }

            if (fileIds.Count == 0)
            {
                _logger.LogWarning("No files found for title: {ComicTitle}", normalizedComicTitle);
                return null;
            }

            _logger.LogInformation("Found {Count} files for title {ComicTitle}", fileIds.Count, normalizedComicTitle);

            // Step 2: Get metadata for all files
            var fileMetadata = await _apiService.GetFileMetadataAsync(fileIds, includeNotes: true, cancellationToken: cancellationToken);

            // Step 3: Parse metadata and structure into volume/chapter/page hierarchy
            var chapters = ParseFilesIntoChapters(fileMetadata, settings);

            // Step 4: Store in database
            var comicId = await StoreComicsInDatabaseAsync(normalizedComicTitle, chapters, fileMetadata, cancellationToken);

            _logger.LogInformation("Successfully synced title {ComicTitle} with ID {ComicId}", normalizedComicTitle, comicId);
            return comicId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing title: {ComicTitle}", normalizedComicTitle);
            throw;
        }
    }
    
    /// <summary>
    /// Gets the count of unsynced titles
    /// </summary>
    public async Task<int> GetUnsyncedComicsCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discoveredComics = await _apiService.DiscoverComicsAsync(cancellationToken);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var syncedComics = await dbContext.Comic
                .Where(s => s.LastSyncedAt != null)
                .Select(s => s.Title)
                .ToListAsync(cancellationToken);

            var unsyncedCount = discoveredComics.Count(s => !syncedComics.Contains(s));
            return unsyncedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsynced titles count");
            return 0;
        }
    }

    /// <summary>
    /// Deletes a cached comics and all of its related cached records.
    /// </summary>
    public async Task<bool> DeleteComicAsync(int comicId, CancellationToken cancellationToken = default)
    {
        if (comicId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(comicId));
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var comic = await dbContext.Comic
            .SingleOrDefaultAsync(x => x.Id == comicId, cancellationToken);

        if (comic is null)
        {
            return false;
        }

        dbContext.Comic.Remove(comic);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted cached comid {ComicId} ({Title})", comicId, comic.Title);
        return true;
    }

    public async Task ApplyMetadataEditAsync(HydrusMetadataEditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ComicId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ComicId));
        }

        var normalizedTitle = request.HydrusTitle.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            throw new ArgumentException("Hydrus title is required.", nameof(request));
        }

        if (request.Pages.Count == 0)
        {
            throw new ArgumentException("At least one page is required.", nameof(request));
        }

        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        var tagServiceKey = settings.TagServiceKey.Trim();
        if (string.IsNullOrWhiteSpace(tagServiceKey))
        {
            throw new InvalidOperationException("Configured tag service key is required to update managed tags.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var comic = await dbContext.Comic
            .Include(s => s.Chapters)
            .ThenInclude(c => c.Pages)
            .ThenInclude(p => p.Variants)
            .SingleOrDefaultAsync(s => s.Id == request.ComicId, cancellationToken);

        if (comic is null)
        {
            throw new InvalidOperationException($"Comic with ID {request.ComicId} was not found.");
        }

        var hashes = request.Pages
            .Select(page => page.Sha256Hash?.Trim())
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        if (hashes.Count == 0)
        {
            throw new InvalidOperationException("No page hashes were provided for metadata update.");
        }

        var existingMetadata = await _apiService.GetFileMetadataByHashesAsync(hashes, includeNotes: false, cancellationToken);
        var existingByHash = existingMetadata
            .Where(file => !string.IsNullOrWhiteSpace(file.Hash))
            .ToDictionary(file => file.Hash, StringComparer.OrdinalIgnoreCase);

        var newManagedTagsByHash = BuildManagedTagsByHash(request, settings);

        foreach (var hash in hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!existingByHash.TryGetValue(hash, out var metadata))
            {
                _logger.LogWarning("File hash {Hash} was not returned by Hydrus metadata lookup and will be skipped.", hash);
                continue;
            }

            var oldManagedTags = ExtractManagedTags(metadata, settings, tagServiceKey);
            var newManagedTags = newManagedTagsByHash.TryGetValue(hash, out var tags)
                ? tags
                : [];

            await _apiService.UpdateTagsAsync(hash, tagServiceKey, oldManagedTags, newManagedTags, cancellationToken);
        }

        comic.Title = normalizedTitle;
        comic.CoverFileHash = ResolveEditedCoverHash(request, settings, newManagedTagsByHash);
        comic.LastSyncedAt = DateTimeOffset.UtcNow;

        RebuildComicStructureFromEdit(comic, request);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Dictionary<string, List<string>> BuildManagedTagsByHash(HydrusMetadataEditRequest request, HydrusSettings settings)
    {
        var titleTag = BuildTag(settings.TitleNamespace, request.HydrusTitle.Trim());
        var coverTag = settings.CoverPageTag.Trim();

        var chapterStarts = request.ChapterStartPageIndices
            .Where(i => i >= 0 && i < request.Pages.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (chapterStarts.Count == 0 && request.Pages.Count > 0)
        {
            chapterStarts.Add(0);
        }
        else if (chapterStarts.Count > 0 && chapterStarts[0] != 0)
        {
            chapterStarts.Insert(0, 0);
        }

        var volumeStarts = request.VolumeStarts
            .Where(entry => entry.PageIndex >= 0 && entry.PageIndex < request.Pages.Count)
            .OrderBy(entry => entry.PageIndex)
            .ToDictionary(entry => entry.PageIndex, entry => entry.VolumeNumber);

        var logicalGroupSizes = request.Pages
            .GroupBy(page => page.LogicalPageGroupId)
            .ToDictionary(group => group.Key, group => group.Count());

        var logicalGroupVariantCounters = new Dictionary<int, int>();
        var tagsByHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < request.Pages.Count; i++)
        {
            var page = request.Pages[i];
            if (string.IsNullOrWhiteSpace(page.Sha256Hash))
            {
                continue;
            }

            var tags = new List<string> { titleTag };

            var volumeNumber = GetVolumeForPage(i, volumeStarts);
            if (volumeNumber.HasValue)
            {
                tags.Add(BuildTag(settings.VolumeNamespace, volumeNumber.Value.ToString()));
            }

            if (chapterStarts.Count > 0)
            {
                var chapterNumber = GetChapterWithinVolume(i, chapterStarts, volumeStarts);
                var fallbackPageNumber = GetPageWithinChapter(i, chapterStarts);
                var pageNumber = ResolvePageNumber(page.PageNumber, fallbackPageNumber);
                tags.Add(BuildTag(settings.ChapterNamespace, chapterNumber.ToString()));
                tags.Add(BuildTag(settings.PageNamespace, pageNumber.ToString()));
            }
            else
            {
                var pageNumber = ResolvePageNumber(page.PageNumber, i + 1);
                tags.Add(BuildTag(settings.PageNamespace, pageNumber.ToString()));
            }

            if (logicalGroupSizes.TryGetValue(page.LogicalPageGroupId, out var variantCount) && variantCount > 1)
            {
                var nextVariantOrdinal = logicalGroupVariantCounters.TryGetValue(page.LogicalPageGroupId, out var existingOrdinal)
                    ? existingOrdinal + 1
                    : 1;

                logicalGroupVariantCounters[page.LogicalPageGroupId] = nextVariantOrdinal;

                var defaultValue = settings.AlternatePageDefaultValue.Trim();
                var alternateValue = ResolveAlternateTagValue(page, defaultValue, nextVariantOrdinal);
                tags.Add(BuildTag(settings.AlternatePageNamespace, alternateValue));
            }

            if (!string.IsNullOrWhiteSpace(coverTag)
                && !string.IsNullOrWhiteSpace(request.CoverFileHash)
                && string.Equals(page.Sha256Hash, request.CoverFileHash, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add(coverTag);
            }

            tagsByHash[page.Sha256Hash] = tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return tagsByHash;
    }

    private static List<string> ExtractManagedTags(FileMetadata metadata, HydrusSettings settings, string tagServiceKey)
    {
        var configuredTags = metadata.GetStorageTagsForService(tagServiceKey);
        var candidateTags = configuredTags.Count > 0
            ? configuredTags
            : metadata.GetAllStorageTags();

        return candidateTags
            .Where(tag => IsManagedStructuralTag(tag, settings))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsManagedStructuralTag(string tag, HydrusSettings settings)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var coverTag = settings.CoverPageTag.Trim();
        if (!string.IsNullOrWhiteSpace(coverTag)
            && string.Equals(tag.Trim(), coverTag, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var titleNamespace = NormalizeNamespace(settings.TitleNamespace, "title:");
        var volumeNamespace = NormalizeNamespace(settings.VolumeNamespace, "volume:");
        var chapterNamespace = NormalizeNamespace(settings.ChapterNamespace, "chapter:");
        var pageNamespace = NormalizeNamespace(settings.PageNamespace, "page:");
        var alternatePageNamespace = NormalizeNamespace(settings.AlternatePageNamespace, "variant:");

        return tag.StartsWith(titleNamespace, StringComparison.OrdinalIgnoreCase)
               || tag.StartsWith(volumeNamespace, StringComparison.OrdinalIgnoreCase)
               || tag.StartsWith(chapterNamespace, StringComparison.OrdinalIgnoreCase)
               || tag.StartsWith(pageNamespace, StringComparison.OrdinalIgnoreCase)
               || tag.StartsWith(alternatePageNamespace, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveEditedCoverHash(
        HydrusMetadataEditRequest request,
        HydrusSettings settings,
        Dictionary<string, List<string>> tagsByHash)
    {
        if (!string.IsNullOrWhiteSpace(request.CoverFileHash)
            && tagsByHash.ContainsKey(request.CoverFileHash.Trim()))
        {
            return request.CoverFileHash.Trim();
        }

        var coverTag = settings.CoverPageTag.Trim();
        if (!string.IsNullOrWhiteSpace(coverTag))
        {
            var taggedCover = tagsByHash
                .FirstOrDefault(entry => entry.Value.Contains(coverTag, StringComparer.OrdinalIgnoreCase))
                .Key;

            if (!string.IsNullOrWhiteSpace(taggedCover))
            {
                return taggedCover;
            }
        }

        return request.Pages
            .Select(page => page.Sha256Hash?.Trim())
            .FirstOrDefault(hash => !string.IsNullOrWhiteSpace(hash));
    }

    private static int ResolvePageNumber(int? pageNumber, int fallback)
        => pageNumber is > 0 ? pageNumber.Value : fallback;

    private static int? GetVolumeForPage(int pageIndex, IReadOnlyDictionary<int, int> volumeStarts)
    {
        if (volumeStarts.Count == 0)
        {
            return null;
        }

        return volumeStarts
            .Where(kv => kv.Key <= pageIndex)
            .OrderByDescending(kv => kv.Key)
            .Select(kv => (int?)kv.Value)
            .FirstOrDefault();
    }

    private static int GetChapterWithinVolume(int pageIndex, IReadOnlyList<int> chapterStarts, IReadOnlyDictionary<int, int> volumeStarts)
    {
        if (chapterStarts.Count == 0)
        {
            return 1;
        }

        var chapterStart = chapterStarts.Where(start => start <= pageIndex).DefaultIfEmpty(0).Max();
        var volumeStart = volumeStarts.Count > 0
            ? volumeStarts.Keys.Where(key => key <= chapterStart).DefaultIfEmpty(0).Max()
            : 0;

        return chapterStarts.Count(start => start >= volumeStart && start <= chapterStart);
    }

    private static int GetPageWithinChapter(int pageIndex, IReadOnlyList<int> chapterStarts)
    {
        if (chapterStarts.Count == 0)
        {
            return pageIndex + 1;
        }

        var chapterStart = chapterStarts.Where(start => start <= pageIndex).DefaultIfEmpty(0).Max();
        return pageIndex - chapterStart + 1;
    }

    private static string ResolveAlternateTagValue(ImportPage page, string defaultValue, int variantOrdinal)
    {
        if (page.IsDefaultVariant)
        {
            return string.IsNullOrWhiteSpace(defaultValue) ? "default" : defaultValue;
        }

        var label = page.VariantLabel?.Trim();
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return $"alt-{variantOrdinal}";
    }

    private static string BuildTag(string namespaceName, string value)
    {
        var trimmedValue = value.Trim();
        var prefix = namespaceName.Trim().TrimEnd(':');

        return string.IsNullOrWhiteSpace(prefix)
            ? trimmedValue
            : $"{prefix}:{trimmedValue}";
    }

    private static void RebuildComicStructureFromEdit(ComicsRecord comic, HydrusMetadataEditRequest request)
    {
        var existingOcrByHash = comic.Chapters
            .SelectMany(chapter => chapter.Pages)
            .SelectMany(page => page.Variants)
            .Where(variant => !string.IsNullOrWhiteSpace(variant.FileHash))
            .GroupBy(variant => variant.FileHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(variant => variant.OcrText).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)),
                StringComparer.OrdinalIgnoreCase);

        comic.Chapters.Clear();

        var chapterStarts = request.ChapterStartPageIndices
            .Where(i => i >= 0 && i < request.Pages.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (chapterStarts.Count == 0 && request.Pages.Count > 0)
        {
            chapterStarts.Add(0);
        }
        else if (chapterStarts.Count > 0 && chapterStarts[0] != 0)
        {
            chapterStarts.Insert(0, 0);
        }

        var volumeStarts = request.VolumeStarts
            .Where(entry => entry.PageIndex >= 0 && entry.PageIndex < request.Pages.Count)
            .OrderBy(entry => entry.PageIndex)
            .ToDictionary(entry => entry.PageIndex, entry => entry.VolumeNumber);

        var logicalGroups = request.Pages
            .Select((page, index) => new { Page = page, Index = index })
            .GroupBy(entry => entry.Page.LogicalPageGroupId)
            .Select(group => new
            {
                GroupId = group.Key,
                PrimaryIndex = group.Min(entry => entry.Index),
                Entries = group.OrderBy(entry => entry.Index).ToList()
            })
            .OrderBy(group => group.PrimaryIndex)
            .ToList();

        for (var chapterIndex = 0; chapterIndex < chapterStarts.Count; chapterIndex++)
        {
            var chapterStart = chapterStarts[chapterIndex];
            var chapterEnd = chapterIndex + 1 < chapterStarts.Count
                ? chapterStarts[chapterIndex + 1]
                : request.Pages.Count;

            var chapterGroups = logicalGroups
                .Where(group => group.PrimaryIndex >= chapterStart && group.PrimaryIndex < chapterEnd)
                .ToList();

            if (chapterGroups.Count == 0)
            {
                continue;
            }

            var volumeNumber = GetVolumeForPage(chapterStart, volumeStarts);
            var chapterNumber = GetChapterWithinVolume(chapterStart, chapterStarts, volumeStarts);

            var chapter = new ChapterRecord
            {
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                Title = $"Ch. {chapterNumber}"
            };

            foreach (var group in chapterGroups)
            {
                var primaryEntry = group.Entries.FirstOrDefault(entry => entry.Page.IsDefaultVariant) ?? group.Entries[0];
                var pageNumber = ResolvePageNumber(primaryEntry.Page.PageNumber, chapter.Pages.Count + 1);

                var pageRecord = new PageRecord
                {
                    PageNumber = pageNumber
                };

                foreach (var variantEntry in group.Entries)
                {
                    existingOcrByHash.TryGetValue(variantEntry.Page.Sha256Hash, out var ocrText);

                    pageRecord.Variants.Add(new PageVariantRecord
                    {
                        FileHash = variantEntry.Page.Sha256Hash,
                        MimeType = variantEntry.Page.MimeType,
                        OcrText = ocrText,
                        IsDefault = variantEntry.Page.IsDefaultVariant,
                        Label = string.IsNullOrWhiteSpace(variantEntry.Page.VariantLabel)
                            ? null
                            : variantEntry.Page.VariantLabel.Trim()
                    });
                }

                if (!pageRecord.Variants.Any(variant => variant.IsDefault) && pageRecord.Variants.Count > 0)
                {
                    pageRecord.Variants.First().IsDefault = true;
                }

                chapter.Pages.Add(pageRecord);
            }

            comic.Chapters.Add(chapter);
        }
    }

    /// <summary>
    /// Builds the editable import payload for a Hydrus title.
    /// </summary>
    private ComicImportPreparation BuildImportPreparation(string comicTitle, List<FileMetadata> fileMetadata, HydrusSettings settings)
    {
        var orderedFiles = ParseImportFiles(fileMetadata, settings);

        var chapterStarts = new List<int>();
        (int? Volume, decimal? Chapter)? lastChapterKey = null;
        var logicalGroupId = 1;
        var pages = new List<ImportPage>();

        foreach (var logicalGroup in orderedFiles
                     .GroupBy(file => (file.Volume, file.Chapter, Page: file.Page ?? int.MaxValue))
                     .OrderBy(group => group.Key.Volume ?? int.MaxValue)
                     .ThenBy(group => group.Key.Chapter ?? decimal.MaxValue)
                     .ThenBy(group => group.Key.Page)
                     .ThenBy(group => group.Min(f => f.Metadata.FileId)))
        {
            var chapterKey = (logicalGroup.Key.Volume, logicalGroup.Key.Chapter);
            if (lastChapterKey != chapterKey)
            {
                chapterStarts.Add(pages.Count);
                lastChapterKey = chapterKey;
            }

            var orderedVariants = logicalGroup
                .OrderByDescending(file => file.IsDefaultVariant)
                .ThenBy(file => file.Metadata.FileId)
                .ToList();

            var explicitDefaultFileId = orderedVariants
                .FirstOrDefault(file => file.IsDefaultVariant)
                ?.Metadata.FileId;

            for (var variantIndex = 0; variantIndex < orderedVariants.Count; variantIndex++)
            {
                var file = orderedVariants[variantIndex];

                pages.Add(new ImportPage
                {
                    Index = pages.Count,
                    ArchiveFileName = file.Metadata.Hash,
                    Data = [],
                    Sha256Hash = file.Metadata.Hash,
                    MimeType = file.Metadata.MimeType,
                    PageNumber = file.Page ?? pages.Count + 1,
                    LogicalPageGroupId = logicalGroupId,
                    IsDefaultVariant = explicitDefaultFileId.HasValue
                        ? file.Metadata.FileId == explicitDefaultFileId.Value
                        : variantIndex == 0,
                    VariantLabel = file.VariantLabel
                });
            }

            logicalGroupId++;
        }

        var metadata = new ComicMetadata
        {
            Series = comicTitle,
            Creator = ExtractCreatorMetadata(orderedFiles, settings.TagServiceKey),
            VolumeNumber = orderedFiles.Select(file => file.Volume).FirstOrDefault(volume => volume.HasValue)
        };

        return new ComicImportPreparation
        {
            Pages = pages,
            Metadata = metadata,
            ChapterStartPageIndices = chapterStarts.Count == 0 ? [0] : chapterStarts
        };
    }

    private List<HydrusImportFile> ParseImportFiles(List<FileMetadata> fileMetadata, HydrusSettings settings)
    {
        var useConfiguredTagService = ShouldUseConfiguredTagServiceForStructuralTags(fileMetadata, settings);

        return fileMetadata
            .Select(file =>
            {
                var structuralTags = GetStructuralTags(file, settings, useConfiguredTagService);
                if (structuralTags.Count == 0)
                {
                    return null;
                }

                var titleTag = ExtractNamespaceValue(structuralTags, settings.TitleNamespace);
                if (string.IsNullOrWhiteSpace(titleTag))
                {
                    _logger.LogWarning("File {Hash} is missing a title tag in the structural tag service and will be skipped.", file.Hash);
                    return null;
                }

                var pageNumber = ExtractNumberFromTag(structuralTags, settings.PageNamespace);
                if (!pageNumber.HasValue)
                {
                    _logger.LogWarning("File {Hash} is missing a page tag in the structural tag service and will be skipped.", file.Hash);
                    return null;
                }

                var alternateValue = ExtractNamespaceValue(structuralTags, settings.AlternatePageNamespace)?.Trim();
                var hasAlternateValue = !string.IsNullOrWhiteSpace(alternateValue);
                var defaultAlternateValue = settings.AlternatePageDefaultValue.Trim();
                var isDefaultVariant = !hasAlternateValue ||
                    string.Equals(alternateValue, defaultAlternateValue, StringComparison.OrdinalIgnoreCase);

                return new HydrusImportFile(
                    file,
                    ExtractNumberFromTag(structuralTags, settings.VolumeNamespace),
                    ExtractDecimalFromTag(structuralTags, settings.ChapterNamespace),
                    pageNumber,
                    isDefaultVariant,
                    hasAlternateValue && !isDefaultVariant ? alternateValue : null);
            })
            .Where(file => file is not null)
            .Select(file => file!)
            .OrderBy(file => file.Volume ?? int.MaxValue)
            .ThenBy(file => file.Chapter ?? decimal.MaxValue)
            .ThenBy(file => file.Page ?? int.MaxValue)
            .ThenByDescending(file => file.IsDefaultVariant)
            .ThenBy(file => file.Metadata.FileId)
            .ToList();
    }

    /// <summary>
    /// Parses files into a chapter/volume/logical-page structure based on tags.
    /// </summary>
    private Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, List<SyncedPageVariant> Variants)>> ParseFilesIntoChapters(
        List<FileMetadata> fileMetadata,
        HydrusSettings settings)
    {
        var chapters = new Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, List<SyncedPageVariant> Variants)>>();

        foreach (var chapterGroup in ParseImportFiles(fileMetadata, settings)
                     .GroupBy(file => (file.Volume, file.Chapter)))
        {
            var logicalPages = chapterGroup
                .GroupBy(file => file.Page ?? int.MaxValue)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    var variants = group
                        .OrderByDescending(file => file.IsDefaultVariant)
                        .ThenBy(file => file.Metadata.FileId)
                        .Select(file => new SyncedPageVariant(
                            file.Metadata,
                            file.IsDefaultVariant,
                            file.VariantLabel))
                        .ToList();

                    var defaultVariantIndex = variants.FindIndex(v => v.IsDefault);
                    if (defaultVariantIndex < 0 && variants.Count > 0)
                    {
                        defaultVariantIndex = 0;
                    }

                    for (var i = 0; i < variants.Count; i++)
                    {
                        variants[i] = variants[i] with { IsDefault = i == defaultVariantIndex };
                    }

                    return (PageNumber: group.Key, Variants: variants);
                })
                .ToList();

            chapters[chapterGroup.Key] = logicalPages;
        }

        return chapters;
    }

    private sealed record HydrusImportFile(
        FileMetadata Metadata,
        int? Volume,
        decimal? Chapter,
        int? Page,
        bool IsDefaultVariant,
        string? VariantLabel);

    private sealed record SyncedPageVariant(
        FileMetadata Metadata,
        bool IsDefault,
        string? Label);

    private string ExtractCreatorMetadata(IEnumerable<HydrusImportFile> orderedFiles, string structuralTagServiceKey)
    {
        foreach (var file in orderedFiles)
        {
            foreach (var tag in file.Metadata.GetStorageTagsExcludingService(structuralTagServiceKey))
            {
                if (tag.StartsWith("creator:", StringComparison.OrdinalIgnoreCase))
                {
                    var creator = tag["creator:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(creator))
                    {
                        return creator;
                    }
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Stores a comic and its structure in the database
    /// </summary>
    private async Task<int> StoreComicsInDatabaseAsync(
        string comitTitle,
        Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, List<SyncedPageVariant> Variants)>> chapters,
        List<FileMetadata> allFileMetadata,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Find or create comic
        var comic = await dbContext.Comic
            .Include(s => s.Chapters)
            .ThenInclude(c => c.Pages)
            .ThenInclude(p => p.Variants)
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Title == comitTitle, cancellationToken);

        if (comic == null)
        {
            comic = new ComicsRecord { Title = comitTitle };
            dbContext.Comic.Add(comic);
        }

        // Clear existing chapters to rebuild them
        comic.Chapters.Clear();

        var settings = await _settingsService.GetSettingsAsync(cancellationToken);

        // Add chapters from parsed data
        foreach (var ((volumeNumber, chapterNumber), pages) in chapters
                     .OrderBy(c => c.Key.Volume ?? 0)
                     .ThenBy(c => c.Key.Chapter ?? 0m))
        {
            var chapter = new ChapterRecord
            {
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                Title = chapterNumber.HasValue ? $"Ch. {chapterNumber.Value}" : null
            };

            // Add logical pages and their variants sorted by page number.
            foreach (var (pageNumber, variants) in pages.OrderBy(p => p.PageNumber))
            {
                var page = new PageRecord
                {
                    PageNumber = pageNumber
                };

                foreach (var variant in variants)
                {
                    page.Variants.Add(new PageVariantRecord
                    {
                        FileHash = variant.Metadata.Hash,
                        MimeType = variant.Metadata.MimeType,
                        OcrText = ExtractNoteValue(variant.Metadata, settings.OcrTextNoteName),
                        IsDefault = variant.IsDefault,
                        Label = variant.Label
                    });
                }

                if (!page.Variants.Any(v => v.IsDefault) && page.Variants.Count > 0)
                {
                    var firstVariant = page.Variants.First();
                    firstVariant.IsDefault = true;
                }

                chapter.Pages.Add(page);
            }

            comic.Chapters.Add(chapter);
        }

        // Update comic metadata (creators, genres, etc.)
        UpdateComicMetadata(comic, allFileMetadata, settings);

        // Set cover to configured cover tag file, or fallback to the lowest page number.
        comic.CoverFileHash = ResolveCoverFileHash(chapters, allFileMetadata, settings);

        var coverFile = allFileMetadata.FirstOrDefault(file =>
            !string.IsNullOrWhiteSpace(comic.CoverFileHash) &&
            string.Equals(file.Hash, comic.CoverFileHash, StringComparison.OrdinalIgnoreCase));

        comic.DisplayTitle = coverFile is null
            ? null
            : ExtractNoteValue(coverFile, settings.FullTitleNoteName);
        comic.Comment = coverFile is null
            ? null
            : ExtractNoteValue(coverFile, settings.ComicCommentNoteName);

        // Update sync timestamp
        comic.LastSyncedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return comic.Id;
    }

    /// <summary>
    /// Updates comic metadata from file tags
    /// </summary>
    private void UpdateComicMetadata(ComicsRecord comics, List<FileMetadata> fileMetadata, HydrusSettings settings)
    {
        var metadataDict = new Dictionary<string, HashSet<string>>();

        // Collect metadata tags from non-structural tag services
        foreach (var file in fileMetadata)
        {
            var infoTags = file.GetStorageTagsExcludingService(settings.TagServiceKey);
            foreach (var tag in infoTags)
            {
                // Extract metadata namespace:value tags (e.g., creator:Neil Gaiman)
                var colonIndex = tag.IndexOf(':');
                if (colonIndex > 0)
                {
                    var ns = tag[..colonIndex];
                    var value = tag[(colonIndex + 1)..];

                    // Skip known structural namespaces
                    if (IsStructuralNamespace(ns, settings))
                    {
                        continue;
                    }

                    if (!metadataDict.ContainsKey(ns))
                    {
                        metadataDict[ns] = new HashSet<string>();
                    }

                    metadataDict[ns].Add(value);
                }
            }
        }

        // Clear and rebuild metadata records
        comics.Metadata.Clear();
        foreach (var (key, values) in metadataDict)
        {
            foreach (var value in values)
            {
                comics.Metadata.Add(new MetadataRecord { Key = key, Value = value });
            }
        }
    }

    /// <summary>
    /// Checks if a namespace is a structural namespace (title, volume, chapter, page, alternate page)
    /// </summary>
    private bool IsStructuralNamespace(string ns, HydrusSettings settings)
    {
        var normalized = ns.ToLowerInvariant();
        return normalized == settings.TitleNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.VolumeNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.ChapterNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.PageNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.AlternatePageNamespace.TrimEnd(':').ToLowerInvariant();
    }

    private string BuildTitleTag(string comicTitle, HydrusSettings settings)
    {
        var namespacePrefix = settings.TitleNamespace.Trim().TrimEnd(':');
        return string.IsNullOrWhiteSpace(namespacePrefix)
            ? comicTitle
            : $"{namespacePrefix}:{comicTitle}";
    }

    private string NormalizeTitleName(string userInput, HydrusSettings settings)
    {
        var trimmed = userInput.Trim();
        var namespacePrefix = settings.TitleNamespace.Trim().TrimEnd(':');

        if (string.IsNullOrWhiteSpace(namespacePrefix))
        {
            return trimmed;
        }

        var namespacedPrefix = $"{namespacePrefix}:";
        if (trimmed.StartsWith(namespacedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[namespacedPrefix.Length..].Trim();
        }

        return trimmed;
    }

    private bool ShouldUseConfiguredTagServiceForStructuralTags(List<FileMetadata> fileMetadata, HydrusSettings settings)
    {
        var structuralTagServiceKey = settings.TagServiceKey.Trim();
        if (string.IsNullOrWhiteSpace(structuralTagServiceKey))
        {
            return false;
        }

        var hasConfiguredPageTags = fileMetadata.Any(file =>
            ExtractNumberFromTag(file.GetStorageTagsForService(structuralTagServiceKey), settings.PageNamespace).HasValue);

        if (!hasConfiguredPageTags)
        {
            _logger.LogInformation("Configured tag service {TagServiceKey} has no page tags for this title. Falling back to all tags for structural parsing.", structuralTagServiceKey);
        }

        return hasConfiguredPageTags;
    }

    private IReadOnlyList<string> GetStructuralTags(FileMetadata file, HydrusSettings settings, bool useConfiguredTagService)
    {
        var structuralTagServiceKey = settings.TagServiceKey.Trim();
        if (useConfiguredTagService && !string.IsNullOrWhiteSpace(structuralTagServiceKey))
        {
            return file.GetStorageTagsForService(structuralTagServiceKey);
        }

        return file.GetAllStorageTags();
    }

    private static string? ExtractNamespaceValue(IReadOnlyList<string> tags, string namespaceName)
    {
        foreach (var tag in tags)
        {
            if (tag.StartsWith(namespaceName, StringComparison.OrdinalIgnoreCase))
            {
                return tag[namespaceName.Length..];
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts an integer value from a tag with the given namespace
    /// </summary>
    private static int? ExtractNumberFromTag(IReadOnlyList<string> tags, string namespaceName)
    {
        foreach (var tag in tags)
        {
            if (tag.StartsWith(namespaceName, StringComparison.OrdinalIgnoreCase))
            {
                var value = tag[namespaceName.Length..];
                if (int.TryParse(value, out var number))
                {
                    return number;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts a decimal value from a tag with the given namespace
    /// </summary>
    private static decimal? ExtractDecimalFromTag(IReadOnlyList<string> tags, string namespaceName)
    {
        foreach (var tag in tags)
        {
            if (tag.StartsWith(namespaceName, StringComparison.OrdinalIgnoreCase))
            {
                var value = tag[namespaceName.Length..];
                if (decimal.TryParse(value, out var number))
                {
                    return number;
                }
            }
        }

        return null;
    }

    private static string? ExtractNoteValue(FileMetadata metadata, string noteName)
    {
        if (string.IsNullOrWhiteSpace(noteName) || metadata.Notes.Count == 0)
        {
            return null;
        }

        if (!metadata.Notes.TryGetValue(noteName.Trim(), out var noteValue))
        {
            return null;
        }

        var trimmed = noteValue.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? ResolveCoverFileHash(
        Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, List<SyncedPageVariant> Variants)>> chapters,
        List<FileMetadata> allFileMetadata,
        HydrusSettings settings)
    {
        var coverTag = settings.CoverPageTag.Trim();

        if (chapters.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(coverTag))
            {
                return null;
            }

            return allFileMetadata.FirstOrDefault(file =>
                file.GetAllStorageTags().Contains(coverTag, StringComparer.OrdinalIgnoreCase))?.Hash;
        }

        var orderedPages = chapters
            .SelectMany(chapter => chapter.Value.SelectMany(page =>
                page.Variants.Select(variant => new
                {
                    chapter.Key.Volume,
                    chapter.Key.Chapter,
                    page.PageNumber,
                    variant.Metadata,
                    variant.IsDefault
                })))
            .OrderBy(page => page.Volume ?? 0)
            .ThenBy(page => page.Chapter ?? 0m)
            .ThenBy(page => page.PageNumber)
            .ThenByDescending(page => page.IsDefault)
            .ThenBy(page => page.Metadata.FileId);

        if (!string.IsNullOrWhiteSpace(coverTag))
        {
            var taggedCover = orderedPages.FirstOrDefault(page =>
                page.Metadata.GetAllStorageTags().Contains(coverTag, StringComparer.OrdinalIgnoreCase));

            if (taggedCover is not null)
            {
                return taggedCover.Metadata.Hash;
            }
        }

        return orderedPages.FirstOrDefault()?.Metadata.Hash;
    }

    private static string NormalizeNamespace(string namespaceName, string fallback)
    {
        var resolved = string.IsNullOrWhiteSpace(namespaceName)
            ? fallback
            : namespaceName.Trim();

        return resolved.EndsWith(':') ? resolved : $"{resolved}:";
    }
}
