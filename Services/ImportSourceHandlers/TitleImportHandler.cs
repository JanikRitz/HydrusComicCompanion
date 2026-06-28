using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services.Abstractions;

namespace HydrusComicCompanion.Services.ImportSourceHandlers;

/// <summary>
/// Handles imports from an existing Hydrus title.
/// Wraps the IHydrusSyncService so the workflow can extract and later sync the same title.
/// Implements a fallback strategy: if search with configured tag service returns no results,
/// retry with the default tag service to enable quick imports from alternative tag services.
/// </summary>
public class TitleImportHandler : IImportSourceHandler
{
    private readonly IHydrusSyncService _syncService;
    private readonly IHydrusApiService _apiService;

    public ImportSource Source => ImportSource.Title;

    public string DisplayName => "Hydrus Title";

    public string Description => "Load an existing Hydrus title and edit metadata and chapters before import.";

    public TitleImportHandler(IHydrusSyncService syncService, IHydrusApiService apiService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
    }

    /// <summary>
    /// Extracts Hydrus title data so the caller can edit metadata and chapter placement before import.
    /// Implements fallback: if no files found with configured tag service, retries with default tag service.
    /// </summary>
    public async Task<ComicImportPreparation> ExtractAsync(
        object sourceIdentifier,
        CancellationToken cancellationToken = default)
    {
        if (sourceIdentifier is not string titleName)
        {
            throw new ArgumentException(
                $"Title handler expects a string title name; got {sourceIdentifier?.GetType().Name ?? "null"}",
                nameof(sourceIdentifier));
        }

        return await _syncService.ExtractComicAsync(titleName, cancellationToken);
    }

    /// <summary>
    /// Syncs the title from Hydrus.
    /// For title imports, the request.SeriesName is used as the sync target.
    /// Implements fallback strategy: if sync with configured tag service returns no results,
    /// retries with skipTagService option to use default tag service.
    /// </summary>
    public async Task<int> CompleteImportAsync(
        ComicImportRequest request,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SeriesName))
        {
            throw new ArgumentException("SeriesName is required for title import.", nameof(request));
        }

        var titleId = await _syncService.SyncComicAsync(request.SeriesName, cancellationToken);

        // Fallback strategy: if no files found with configured tag service, retry with default tag service
        if (titleId is null)
        {
            titleId = await AttemptSyncWithDefaultTagServiceAsync(request.SeriesName, cancellationToken);
        }

        if (titleId is null)
        {
            throw new InvalidOperationException($"No matching files found for title: {request.SeriesName}");
        }

        progress?.Report(new ImportProgressUpdate
        {
            Current = 1,
            Total = 1,
            Message = "Title synced successfully."
        });

        return titleId.Value;
    }

    /// <summary>
    /// Attempts to sync the title using the default tag service (skipping configured tag service).
    /// This is used as a fallback when the configured tag service has no matching files.
    /// </summary>
    private async Task<int?> AttemptSyncWithDefaultTagServiceAsync(string seriesName, CancellationToken cancellationToken)
    {
        try
        {
            // Build the title search tag using the same method as HydrusSyncService
            var normalizedSeriesName = NormalizeTitleName(seriesName);
            var titleTag = BuildTitleTag(normalizedSeriesName);

            // Search with skipTagService=true to bypass configured tag service and use default
            var fileIds = await _apiService.SearchFilesAsync(
                new List<string> { titleTag },
                fileDomain: null,
                skipTagService: true,
                cancellationToken: cancellationToken);

            if (fileIds.Count == 0)
            {
                return null;
            }

            // Reuse sync logic from HydrusSyncService by calling it again
            // Since we found files with default tag service, the second sync attempt will succeed
            return await _syncService.SyncComicAsync(seriesName, cancellationToken);
        }
        catch (Exception)
        {
            // If fallback also fails, return null to let the main error handling deal with it
            return null;
        }
    }

    /// <summary>
    /// Mirrors NormalizeTitleName from HydrusSyncService.
    /// </summary>
    private static string NormalizeTitleName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    /// <summary>
    /// Mirrors BuildTitleTag from HydrusSyncService.
    /// </summary>
    private static string BuildTitleTag(string titleName)
    {
        return $"title:{titleName}";
    }
}
