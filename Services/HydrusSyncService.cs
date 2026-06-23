using System.Text.RegularExpressions;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HydrusComicCompanion.Services;

public class HydrusSyncService : IHydrusSyncService
{
    private readonly IHydrusApiService _apiService;
    private readonly IDbContextFactory<SettingsDbContext> _dbContextFactory;
    private readonly HydrusSettings _settings;
    private readonly ILogger<HydrusSyncService> _logger;

    public HydrusSyncService(
        IHydrusApiService apiService,
        IDbContextFactory<SettingsDbContext> dbContextFactory,
        IOptions<HydrusSettings> settings,
        ILogger<HydrusSyncService> logger)
    {
        _apiService = apiService;
        _dbContextFactory = dbContextFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Performs a full library sync: discovers series and syncs their chapter/page structure
    /// </summary>
    public async Task<int> SyncLibraryAsync(CancellationToken cancellationToken = default)
    {
        var syncedCount = 0;

        try
        {
            _logger.LogInformation("Starting library sync");

            // Step 1: Discover all series
            var seriesNames = await _apiService.DiscoverSeriesAsync(cancellationToken);
            _logger.LogInformation("Discovered {Count} series", seriesNames.Count);

            // Step 2: Sync each series
            foreach (var seriesName in seriesNames)
            {
                try
                {
                    var seriesId = await SyncSeriesAsync(seriesName, cancellationToken);
                    if (seriesId.HasValue)
                    {
                        syncedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing series: {SeriesName}", seriesName);
                }
            }

            _logger.LogInformation("Completed library sync: {SyncedCount} series synchronized", syncedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library sync");
            throw;
        }

        return syncedCount;
    }

    /// <summary>
    /// Syncs a specific series: fetches all files tagged with the series and structures them
    /// </summary>
    public async Task<int?> SyncSeriesAsync(string seriesName, CancellationToken cancellationToken = default)
    {
        var normalizedSeriesName = NormalizeSeriesName(seriesName);

        if (string.IsNullOrWhiteSpace(normalizedSeriesName))
        {
            throw new ArgumentException("Series name cannot be empty.", nameof(seriesName));
        }

        try
        {
            _logger.LogInformation("Syncing series: {SeriesName}", normalizedSeriesName);

            // Build the series search tag
            var seriesTag = BuildSeriesTag(normalizedSeriesName);

            // Step 1: Search for files with this series tag
            var fileIds = await _apiService.SearchFilesAsync(
                new List<string> { seriesTag },
                cancellationToken: cancellationToken);

            if (fileIds.Count == 0)
            {
                _logger.LogWarning("No files found for series: {SeriesName}", normalizedSeriesName);
                return null;
            }

            _logger.LogInformation("Found {Count} files for series {SeriesName}", fileIds.Count, normalizedSeriesName);

            // Step 2: Get metadata for all files
            var fileMetadata = await _apiService.GetFileMetadataAsync(fileIds, cancellationToken);

            // Step 3: Parse metadata and structure into volume/chapter/page hierarchy
            var chapters = ParseFilesIntoChapters(fileMetadata);

            // Step 4: Store in database
            var seriesId = await StoreSeriesInDatabaseAsync(normalizedSeriesName, chapters, fileMetadata, cancellationToken);

            _logger.LogInformation("Successfully synced series {SeriesName} with ID {SeriesId}", normalizedSeriesName, seriesId);
            return seriesId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing series: {SeriesName}", normalizedSeriesName);
            throw;
        }
    }

    /// <summary>
    /// Gets the count of unsynced series
    /// </summary>
    public async Task<int> GetUnsyncedSeriesCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discoveredSeries = await _apiService.DiscoverSeriesAsync(cancellationToken);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var syncedSeries = await dbContext.Series
                .Where(s => s.LastSyncedAt != null)
                .Select(s => s.Title)
                .ToListAsync(cancellationToken);

            var unsyncedCount = discoveredSeries.Count(s => !syncedSeries.Contains(s));
            return unsyncedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsynced series count");
            return 0;
        }
    }

    /// <summary>
    /// Parses files into a chapter/volume/page structure based on tags
    /// </summary>
    private Dictionary<(int Volume, decimal Chapter), List<(int PageNumber, FileMetadata Metadata)>> ParseFilesIntoChapters(
        List<FileMetadata> fileMetadata)
    {
        var chapters = new Dictionary<(int Volume, decimal Chapter), List<(int PageNumber, FileMetadata Metadata)>>();

        foreach (var file in fileMetadata)
        {
            var structuralTags = GetStructuralTags(file);
            if (structuralTags.Count == 0)
            {
                continue;
            }

            var seriesTag = ExtractNamespaceValue(structuralTags, _settings.SeriesNamespace);
            if (string.IsNullOrWhiteSpace(seriesTag))
            {
                _logger.LogWarning("File {Hash} is missing a series tag in the structural tag service and will be skipped.", file.Hash);
                continue;
            }

            var volumeNumber = ExtractNumberFromTag(structuralTags, _settings.VolumeNamespace) ?? 1;
            var chapterNumber = ExtractDecimalFromTag(structuralTags, _settings.ChapterNamespace) ?? 1m;
            var pageNumber = ExtractNumberFromTag(structuralTags, _settings.PageNamespace) ?? 1;

            var key = (volumeNumber, chapterNumber);

            if (!chapters.ContainsKey(key))
            {
                chapters[key] = new List<(int, FileMetadata)>();
            }

            chapters[key].Add((pageNumber, file));
        }

        return chapters;
    }

    /// <summary>
    /// Stores series and its structure in the database
    /// </summary>
    private async Task<int> StoreSeriesInDatabaseAsync(
        string seriesName,
        Dictionary<(int Volume, decimal Chapter), List<(int PageNumber, FileMetadata Metadata)>> chapters,
        List<FileMetadata> allFileMetadata,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Find or create series
        var series = await dbContext.Series
            .Include(s => s.Chapters)
            .ThenInclude(c => c.Pages)
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Title == seriesName, cancellationToken);

        if (series == null)
        {
            series = new SeriesRecord { Title = seriesName };
            dbContext.Series.Add(series);
        }

        // Clear existing chapters to rebuild them
        series.Chapters.Clear();

        // Add chapters from parsed data
        foreach (var ((volumeNumber, chapterNumber), pages) in chapters.OrderBy(c => c.Key.Volume).ThenBy(c => c.Key.Chapter))
        {
            var chapter = new ChapterRecord
            {
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                Title = $"Ch. {chapterNumber}"
            };

            // Add pages sorted by page number
            foreach (var (pageNumber, fileMetadata) in pages.OrderBy(p => p.PageNumber))
            {
                var page = new PageRecord
                {
                    FileHash = fileMetadata.Hash,
                    PageNumber = pageNumber,
                    MimeType = fileMetadata.MimeType
                };
                chapter.Pages.Add(page);
            }

            series.Chapters.Add(chapter);
        }

        // Update series metadata (creators, genres, etc.)
        UpdateSeriesMetadata(series, allFileMetadata);

        // Set cover image to the first page of the first chapter if not already set
        if (string.IsNullOrEmpty(series.CoverFileHash) && chapters.Count > 0)
        {
            var firstChapterPages = chapters.Values.First().OrderBy(p => p.PageNumber).First();
            series.CoverFileHash = firstChapterPages.Metadata.Hash;
        }

        // Update sync timestamp
        series.LastSyncedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return series.Id;
    }

    /// <summary>
    /// Updates series metadata from file tags
    /// </summary>
    private void UpdateSeriesMetadata(SeriesRecord series, List<FileMetadata> fileMetadata)
    {
        var metadataDict = new Dictionary<string, HashSet<string>>();
        var structuralTagServiceKey = _settings.TagServiceKey.Trim();

        // Collect metadata tags from non-structural tag services
        foreach (var file in fileMetadata)
        {
            var infoTags = file.GetStorageTagsExcludingService(structuralTagServiceKey);
            foreach (var tag in infoTags)
            {
                // Extract metadata namespace:value tags (e.g., creator:Neil Gaiman)
                var colonIndex = tag.IndexOf(':');
                if (colonIndex > 0)
                {
                    var ns = tag[..colonIndex];
                    var value = tag[(colonIndex + 1)..];

                    // Skip known structural namespaces
                    if (IsStructuralNamespace(ns))
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
        series.Metadata.Clear();
        foreach (var (key, values) in metadataDict)
        {
            foreach (var value in values)
            {
                series.Metadata.Add(new MetadataRecord { Key = key, Value = value });
            }
        }
    }

    /// <summary>
    /// Checks if a namespace is a structural namespace (volume, chapter, page, series)
    /// </summary>
    private bool IsStructuralNamespace(string ns)
    {
        var normalized = ns.ToLowerInvariant();
        return normalized == _settings.SeriesNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == _settings.VolumeNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == _settings.ChapterNamespace.TrimEnd(':').ToLowerInvariant() ||
               normalized == _settings.PageNamespace.TrimEnd(':').ToLowerInvariant();
    }

    private string BuildSeriesTag(string seriesName)
    {
        var namespacePrefix = _settings.SeriesNamespace.Trim().TrimEnd(':');
        return string.IsNullOrWhiteSpace(namespacePrefix)
            ? seriesName
            : $"{namespacePrefix}:{seriesName}";
    }

    private string NormalizeSeriesName(string userInput)
    {
        var trimmed = userInput.Trim();
        var namespacePrefix = _settings.SeriesNamespace.Trim().TrimEnd(':');

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

    private IReadOnlyList<string> GetStructuralTags(FileMetadata file)
    {
        var structuralTagServiceKey = _settings.TagServiceKey.Trim();
        if (!string.IsNullOrWhiteSpace(structuralTagServiceKey))
        {
            return file.GetStorageTagsForService(structuralTagServiceKey);
        }

        return file.Tags
            .SelectMany(kvp => kvp.Value.StorageTags.Values)
            .SelectMany(tags => tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
}
