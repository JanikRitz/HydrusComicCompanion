using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services.Abstractions;

namespace HydrusComicCompanion.Services.ImportSourceHandlers;

/// <summary>
/// Handles imports by syncing an existing title from Hydrus.
/// Wraps the IHydrusSyncService to fetch files and structure them by chapter.
/// </summary>
public class TitleImportHandler : IImportSourceHandler
{
    private readonly IHydrusSyncService _syncService;

    public ImportSource Source => ImportSource.Title;

    public string DisplayName => "Hydrus Title";

    public string Description => "Sync a single comic by entering its title (with or without namespace).";

    public TitleImportHandler(IHydrusSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    /// <summary>
    /// For title imports, extraction is deferred to CompleteImportAsync.
    /// This method always throws as titles are synced directly.
    /// </summary>
    public Task<(List<ImportPage> Pages, ComicMetadata? Metadata)> ExtractAsync(
        object sourceIdentifier,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Title import handler does not support the extract phase. " +
            "Use CompleteImportAsync with a ComicImportRequest containing the title name instead.");
    }

    /// <summary>
    /// Syncs the title from Hydrus.
    /// For title imports, the request.SeriesName is used as the sync target.
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

        var titleId = await _syncService.SyncTitleAsync(request.SeriesName, cancellationToken);

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
}
