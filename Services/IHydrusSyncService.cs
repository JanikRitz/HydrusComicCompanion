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
    /// <param name="progress">Optional progress callback for current title and counts.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of titles synchronized</returns>
    Task<int> SyncLibraryAsync(IProgress<SyncProgressUpdate>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a title from Hydrus so the import workflow can edit metadata and chapters before import.
    /// </summary>
    /// <param name="seriesName">Name of the title to extract (without the namespace prefix)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Preparation data containing ordered pages, metadata, and chapter starts.</returns>
    Task<ComicImportPreparation> ExtractTitleAsync(string seriesName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a title from Hydrus using a one-off tag service and namespace mapping override so
    /// titles tagged by another tool can be loaded into the import workflow for editing.
    /// </summary>
    /// <param name="seriesName">Name of the title to extract (without the namespace prefix).</param>
    /// <param name="sourceMapping">Optional override for the tag service and structural namespaces. Blank fields fall back to global settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preparation data containing ordered pages, metadata, and chapter starts.</returns>
    Task<ComicImportPreparation> ExtractTitleAsync(string seriesName, HydrusSourceMapping? sourceMapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers existing titles in another Hydrus tag service using a one-off namespace mapping so the
    /// mapped import workflow can queue every matching comic for review. Reads cover/first pages to find titles.
    /// </summary>
    /// <param name="mapping">Tag service and structural namespace override. Blank fields fall back to global settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Distinct, ordered title names found in the mapped tag service.</returns>
    Task<List<string>> DiscoverMappedTitlesAsync(HydrusSourceMapping mapping, CancellationToken cancellationToken = default);

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
    /// <param name="progress">Optional progress callback for current title and counts.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of existing titles synchronized</returns>
    Task<int> SyncExistingLibrariesAsync(IProgress<SyncProgressUpdate>? progress = null, CancellationToken cancellationToken = default);

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
    Task<bool> DeleteComicAsync(int seriesId, CancellationToken cancellationToken = default);
}
