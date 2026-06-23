using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services.Abstractions;

namespace HydrusComicCompanion.Services.ImportSourceHandlers;

/// <summary>
/// Handles imports from an existing Hydrus title.
/// Wraps the IHydrusSyncService so the workflow can extract and later sync the same title.
/// </summary>
public class TitleImportHandler : IImportSourceHandler
{
    private readonly IHydrusSyncService _syncService;

    public ImportSource Source => ImportSource.Title;

    public string DisplayName => "Hydrus Title";

    public string Description => "Load an existing Hydrus title and edit metadata and chapters before import.";

    public TitleImportHandler(IHydrusSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    /// <summary>
    /// Extracts Hydrus title data so the caller can edit metadata and chapter placement before import.
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

        return await _syncService.ExtractTitleAsync(titleName, cancellationToken);
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
