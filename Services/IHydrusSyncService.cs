using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Service for orchestrating the sync process between Hydrus and the local cache database
/// </summary>
public interface IHydrusSyncService
{
    /// <summary>
    /// Performs a full library sync: discovers titles and syncs their chapter/page structure
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of titles synchronized</returns>
    Task<int> SyncLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a title from Hydrus so the import workflow can edit metadata and chapters before import.
    /// </summary>
    /// <param name="seriesName">Name of the title to extract (without the namespace prefix)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Preparation data containing ordered pages, metadata, and chapter starts.</returns>
    Task<ComicImportPreparation> ExtractTitleAsync(string seriesName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs a specific title: fetches all files tagged with the title and structures them
    /// </summary>
    /// <param name="seriesName">Name of the title to sync (without the namespace prefix)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The synchronized series ID, or null if sync failed</returns>
    Task<int?> SyncTitleAsync(string seriesName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs a specific series: fetches all files tagged with the series and structures them.
    /// </summary>
    /// <param name="seriesName">Name of the series to sync (without the namespace prefix)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The synchronized series ID, or null if sync failed</returns>
    Task<int?> SyncSeriesAsync(string seriesName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs only series that already exist in the local library cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of existing titles synchronized</returns>
    Task<int> SyncExistingLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unsynced titles
    /// </summary>
    Task<int> GetUnsyncedTitlesCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unsynced series.
    /// </summary>
    Task<int> GetUnsyncedSeriesCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a cached series and all related cached data.
    /// </summary>
    /// <param name="seriesId">The local cached series identifier.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True when the series existed and was deleted; otherwise false.</returns>
    Task<bool> DeleteSeriesAsync(int seriesId, CancellationToken cancellationToken = default);
}
