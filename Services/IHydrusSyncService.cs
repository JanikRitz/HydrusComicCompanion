using HydrusComicCompanion.Data;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Service for orchestrating the sync process between Hydrus and the local cache database
/// </summary>
public interface IHydrusSyncService
{
    /// <summary>
    /// Performs a full library sync: discovers series and syncs their chapter/page structure
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of series synchronized</returns>
    Task<int> SyncLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs a specific series: fetches all files tagged with the series and structures them
    /// </summary>
    /// <param name="seriesName">Name of the series to sync (without the namespace prefix)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The synchronized series ID, or null if sync failed</returns>
    Task<int?> SyncSeriesAsync(string seriesName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs only series that already exist in the local library cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of existing series synchronized</returns>
    Task<int> SyncExistingLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unsynced series
    /// </summary>
    Task<int> GetUnsyncedSeriesCountAsync(CancellationToken cancellationToken = default);
}
