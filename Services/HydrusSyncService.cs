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
            var titleNames = await _apiService.DiscoverTitlesAsync(cancellationToken);
            _logger.LogInformation("Discovered {Count} titles", titleNames.Count);

            progress?.Report(new SyncProgressUpdate { Current = 0, Total = titleNames.Count });

            // Step 2: Sync each title
            for (var index = 0; index < titleNames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var seriesName = titleNames[index];
                progress?.Report(new SyncProgressUpdate
                {
                    Current = index + 1,
                    Total = titleNames.Count,
                    CurrentTitle = seriesName
                });

                try
                {
                    var seriesId = await SyncTitleAsync(seriesName, cancellationToken);
                    if (seriesId.HasValue)
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
                    _logger.LogError(ex, "Error syncing title: {TitleName}", seriesName);
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
            var existingSeriesNames = await dbContext.Comic
                .AsNoTracking()
                .Select(s => s.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} existing titles in local cache", existingSeriesNames.Count);

            progress?.Report(new SyncProgressUpdate { Current = 0, Total = existingSeriesNames.Count });

            for (var index = 0; index < existingSeriesNames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var seriesName = existingSeriesNames[index];
                progress?.Report(new SyncProgressUpdate
                {
                    Current = index + 1,
                    Total = existingSeriesNames.Count,
                    CurrentTitle = seriesName
                });

                try
                {
                    var seriesId = await SyncTitleAsync(seriesName, cancellationToken);
                    if (seriesId.HasValue)
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
                    _logger.LogError(ex, "Error syncing existing title: {TitleName}", seriesName);
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
    /// Extracts a Hydrus title into pages plus initial metadata/chapter state for the import UI.
    /// Implements fallback: if no files found with configured tag service, retries with default tag service.
    /// </summary>
    public async Task<ComicImportPreparation> ExtractTitleAsync(string seriesName, CancellationToken cancellationToken = default)
        => await ExtractTitleAsync(seriesName, sourceMapping: null, cancellationToken);

    public async Task<ComicImportPreparation> ExtractTitleAsync(string seriesName, HydrusSourceMapping? sourceMapping, CancellationToken cancellationToken = default)
    {
        var settings = ApplySourceMapping(await _settingsService.GetSettingsAsync(cancellationToken), sourceMapping);
        var normalizedSeriesName = NormalizeTitleName(seriesName, settings);

        if (string.IsNullOrWhiteSpace(normalizedSeriesName))
        {
            throw new ArgumentException("Title name cannot be empty.", nameof(seriesName));
        }

        var seriesTag = BuildTitleTag(normalizedSeriesName, settings);
        var fileIds = await _apiService.SearchFilesAsync(
            settings,
            new List<string> { seriesTag },
            cancellationToken: cancellationToken);

        // Fallback: if no files found with configured tag service, retry with default tag service
        if (fileIds.Count == 0 && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
        {
            _logger.LogInformation("No files found for title {TitleName} with configured tag service. Retrying with default tag service.", normalizedSeriesName);
            fileIds = await _apiService.SearchFilesAsync(
                settings,
                new List<string> { seriesTag },
                fileDomain: null,
                skipTagService: true,
                cancellationToken: cancellationToken);
        }

        if (fileIds.Count == 0)
        {
            return new ComicImportPreparation
            {
                Metadata = new ComicMetadata { Series = normalizedSeriesName },
                ChapterStartPageIndices = [0]
            };
        }

        var fileMetadata = await _apiService.GetFileMetadataAsync(fileIds, cancellationToken: cancellationToken);
        return BuildImportPreparation(normalizedSeriesName, fileMetadata, settings);
    }

    /// <summary>
    /// Discovers titles in another tag service by applying a one-off mapping to the global settings and
    /// running the shared cover/first-page discovery query against the mapped tag service and namespaces.
    /// </summary>
    public async Task<List<string>> DiscoverMappedTitlesAsync(HydrusSourceMapping mapping, CancellationToken cancellationToken = default)
    {
        var settings = ApplySourceMapping(await _settingsService.GetSettingsAsync(cancellationToken), mapping);
        return await _apiService.DiscoverTitlesAsync(settings, cancellationToken);
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
    public async Task<int?> SyncTitleAsync(string seriesName, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        var normalizedSeriesName = NormalizeTitleName(seriesName, settings);

        if (string.IsNullOrWhiteSpace(normalizedSeriesName))
        {
            throw new ArgumentException("Title name cannot be empty.", nameof(seriesName));
        }

        try
        {
            _logger.LogInformation("Syncing title: {TitleName}", normalizedSeriesName);
            
            // Build the title search tag
            var seriesTag = BuildTitleTag(normalizedSeriesName, settings);

            // Step 1: Search for files with this series tag
            var fileIds = await _apiService.SearchFilesAsync(
                new List<string> { seriesTag },
                cancellationToken: cancellationToken);

            // Fallback: if no files found with configured tag service, retry with default tag service
            if (fileIds.Count == 0 && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                _logger.LogInformation("No files found for title {TitleName} with configured tag service. Retrying with default tag service.", normalizedSeriesName);
                fileIds = await _apiService.SearchFilesAsync(
                    new List<string> { seriesTag },
                    fileDomain: null,
                    skipTagService: true,
                    cancellationToken: cancellationToken);
            }

            if (fileIds.Count == 0)
            {
                _logger.LogWarning("No files found for title: {TitleName}", normalizedSeriesName);
                return null;
            }

            _logger.LogInformation("Found {Count} files for title {TitleName}", fileIds.Count, normalizedSeriesName);

            // Step 2: Get metadata for all files
            var fileMetadata = await _apiService.GetFileMetadataAsync(fileIds, includeNotes: true, cancellationToken: cancellationToken);

            // Step 3: Parse metadata and structure into volume/chapter/page hierarchy
            var chapters = ParseFilesIntoChapters(fileMetadata, settings);

            // Step 4: Store in database
            var seriesId = await StoreSeriesInDatabaseAsync(normalizedSeriesName, chapters, fileMetadata, cancellationToken);

            _logger.LogInformation("Successfully synced title {TitleName} with ID {SeriesId}", normalizedSeriesName, seriesId);
            return seriesId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing title: {TitleName}", normalizedSeriesName);
            throw;
        }
    }

    public Task<int?> SyncSeriesAsync(string seriesName, CancellationToken cancellationToken = default)
        => SyncTitleAsync(seriesName, cancellationToken);

    /// <summary>
    /// Gets the count of unsynced titles
    /// </summary>
    public async Task<int> GetUnsyncedTitlesCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discoveredSeries = await _apiService.DiscoverTitlesAsync(cancellationToken);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var syncedSeries = await dbContext.Comic
                .Where(s => s.LastSyncedAt != null)
                .Select(s => s.Title)
                .ToListAsync(cancellationToken);

            var unsyncedCount = discoveredSeries.Count(s => !syncedSeries.Contains(s));
            return unsyncedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsynced titles count");
            return 0;
        }
    }

    public Task<int> GetUnsyncedSeriesCountAsync(CancellationToken cancellationToken = default)
        => GetUnsyncedTitlesCountAsync(cancellationToken);

    /// <summary>
    /// Deletes a cached series and all of its related cached records.
    /// </summary>
    public async Task<bool> DeleteComicAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seriesId));
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var series = await dbContext.Comic
            .SingleOrDefaultAsync(x => x.Id == seriesId, cancellationToken);

        if (series is null)
        {
            return false;
        }

        dbContext.Comic.Remove(series);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted cached series {SeriesId} ({Title})", seriesId, series.Title);
        return true;
    }

    /// <summary>
    /// Builds the editable import payload for a Hydrus title.
    /// </summary>
    private ComicImportPreparation BuildImportPreparation(string seriesName, List<FileMetadata> fileMetadata, HydrusSettings settings)
    {
        var useConfiguredTagService = ShouldUseConfiguredTagServiceForStructuralTags(fileMetadata, settings);

        var orderedFiles = fileMetadata
            .Select(file => new HydrusImportFile(
                file,
                ExtractNumberFromTag(GetStructuralTags(file, settings, useConfiguredTagService), settings.VolumeNamespace),
                ExtractDecimalFromTag(GetStructuralTags(file, settings, useConfiguredTagService), settings.ChapterNamespace),
                ExtractNumberFromTag(GetStructuralTags(file, settings, useConfiguredTagService), settings.PageNamespace)))
            .OrderBy(file => file.Volume ?? int.MaxValue)
            .ThenBy(file => file.Chapter ?? decimal.MaxValue)
            .ThenBy(file => file.Page ?? int.MaxValue)
            .ThenBy(file => file.Metadata.FileId)
            .ToList();

        var chapterStarts = new List<int>();
        (int? Volume, decimal? Chapter)? lastChapterKey = null;

        for (var i = 0; i < orderedFiles.Count; i++)
        {
            var currentKey = (orderedFiles[i].Volume, orderedFiles[i].Chapter);
            if (lastChapterKey != currentKey)
            {
                chapterStarts.Add(i);
                lastChapterKey = currentKey;
            }
        }

        var metadata = new ComicMetadata
        {
            Series = seriesName,
            Creator = ExtractCreatorMetadata(orderedFiles, settings.TagServiceKey),
            VolumeNumber = orderedFiles.Select(file => file.Volume).FirstOrDefault(volume => volume.HasValue)
        };

        return new ComicImportPreparation
        {
            Pages = orderedFiles
                .Select((file, index) => new ImportPage
                {
                    Index = index,
                    ArchiveFileName = file.Metadata.Hash,
                    Data = [],
                    Sha256Hash = file.Metadata.Hash,
                    MimeType = file.Metadata.MimeType,
                    PageNumber = file.Page ?? index + 1
                })
                .ToList(),
            Metadata = metadata,
            ChapterStartPageIndices = chapterStarts.Count == 0 ? [0] : chapterStarts
        };
    }

    /// <summary>
    /// Parses files into a chapter/volume/page structure based on tags
    /// </summary>
    private Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, FileMetadata Metadata)>> ParseFilesIntoChapters(
        List<FileMetadata> fileMetadata, HydrusSettings settings)
    {
        var chapters = new Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, FileMetadata Metadata)>>();
        var useConfiguredTagService = ShouldUseConfiguredTagServiceForStructuralTags(fileMetadata, settings);

        foreach (var file in fileMetadata)
        {
            var structuralTags = GetStructuralTags(file, settings, useConfiguredTagService);
            if (structuralTags.Count == 0)
            {
                continue;
            }

            var titleTag = ExtractNamespaceValue(structuralTags, settings.TitleNamespace);
            if (string.IsNullOrWhiteSpace(titleTag))
            {
                _logger.LogWarning("File {Hash} is missing a title tag in the structural tag service and will be skipped.", file.Hash);
                continue;
            }

            var pageNumber = ExtractNumberFromTag(structuralTags, settings.PageNamespace);
            if (!pageNumber.HasValue)
            {
                _logger.LogWarning("File {Hash} is missing a page tag in the structural tag service and will be skipped.", file.Hash);
                continue;
            }

            var volumeNumber = ExtractNumberFromTag(structuralTags, settings.VolumeNamespace);
            var chapterNumber = ExtractDecimalFromTag(structuralTags, settings.ChapterNamespace);

            var key = (volumeNumber, chapterNumber);

            if (!chapters.ContainsKey(key))
            {
                chapters[key] = new List<(int, FileMetadata)>();
            }

            chapters[key].Add((pageNumber.Value, file));
        }

        return chapters;
    }

    private sealed record HydrusImportFile(
        FileMetadata Metadata,
        int? Volume,
        decimal? Chapter,
        int? Page);

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
    /// Stores series and its structure in the database
    /// </summary>
    private async Task<int> StoreSeriesInDatabaseAsync(
        string seriesName,
        Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, FileMetadata Metadata)>> chapters,
        List<FileMetadata> allFileMetadata,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Find or create series
        var series = await dbContext.Comic
            .Include(s => s.Chapters)
            .ThenInclude(c => c.Pages)
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Title == seriesName, cancellationToken);

        if (series == null)
        {
            series = new ComicsRecord { Title = seriesName };
            dbContext.Comic.Add(series);
        }

        // Clear existing chapters to rebuild them
        series.Chapters.Clear();

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

            // Add pages sorted by page number
            foreach (var (pageNumber, fileMetadata) in pages.OrderBy(p => p.PageNumber))
            {
                var page = new PageRecord
                {
                    FileHash = fileMetadata.Hash,
                    PageNumber = pageNumber,
                    MimeType = fileMetadata.MimeType,
                    OcrText = ExtractNoteValue(fileMetadata, settings.OcrTextNoteName)
                };
                chapter.Pages.Add(page);
            }

            series.Chapters.Add(chapter);
        }

        // Update series metadata (creators, genres, etc.)
        UpdateSeriesMetadata(series, allFileMetadata, settings);

        // Set cover to configured cover tag file, or fallback to the lowest page number.
        series.CoverFileHash = ResolveCoverFileHash(chapters, allFileMetadata, settings);

        var coverFile = allFileMetadata.FirstOrDefault(file =>
            !string.IsNullOrWhiteSpace(series.CoverFileHash) &&
            string.Equals(file.Hash, series.CoverFileHash, StringComparison.OrdinalIgnoreCase));

        series.DisplayTitle = coverFile is null
            ? null
            : ExtractNoteValue(coverFile, settings.FullTitleNoteName);
        series.Comment = coverFile is null
            ? null
            : ExtractNoteValue(coverFile, settings.ComicCommentNoteName);

        // Update sync timestamp
        series.LastSyncedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return series.Id;
    }

    /// <summary>
    /// Updates series metadata from file tags
    /// </summary>
    private void UpdateSeriesMetadata(ComicsRecord comics, List<FileMetadata> fileMetadata, HydrusSettings settings)
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
    /// Checks if a namespace is a structural namespace (title, volume, chapter, page)
    /// </summary>
    private bool IsStructuralNamespace(string ns, HydrusSettings settings)
    {
        var normalized = ns.ToLowerInvariant();
        return normalized == settings.TitleNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.VolumeNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.ChapterNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == settings.PageNamespace.TrimEnd(':').ToLowerInvariant();
    }

    private string BuildTitleTag(string seriesName, HydrusSettings settings)
    {
        var namespacePrefix = settings.TitleNamespace.Trim().TrimEnd(':');
        return string.IsNullOrWhiteSpace(namespacePrefix)
            ? seriesName
            : $"{namespacePrefix}:{seriesName}";
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
        Dictionary<(int? Volume, decimal? Chapter), List<(int PageNumber, FileMetadata Metadata)>> chapters,
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
            .SelectMany(chapter => chapter.Value.Select(page => new
            {
                chapter.Key.Volume,
                chapter.Key.Chapter,
                page.PageNumber,
                page.Metadata
            }))
            .OrderBy(page => page.Volume ?? 0)
            .ThenBy(page => page.Chapter ?? 0m)
            .ThenBy(page => page.PageNumber)
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
}
