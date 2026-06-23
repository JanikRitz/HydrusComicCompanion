using HydrusComicCompanion.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Service for importing CBZ/CBR comic archives into Hydrus and the local cache.
/// </summary>
public interface IComicImportService
{
    /// <summary>
    /// Extracts all image pages (and optional ComicInfo.xml metadata) from a CBZ or CBR archive.
    /// </summary>
    /// <param name="fileStream">Stream of the archive file.</param>
    /// <param name="fileName">Original file name (used to detect CBZ vs CBR).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered list of pages and any metadata found in ComicInfo.xml (may be null).</returns>
    Task<(List<ImportPage> Pages, ComicMetadata? Metadata)> ExtractArchiveAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads any pages not already in Hydrus, tags all pages, and writes the series to the local cache.
    /// </summary>
    /// <param name="request">Import configuration including pages, chapter boundaries, and metadata.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local database ID of the imported/updated series.</returns>
    Task<int> ImportComicAsync(
        ComicImportRequest request,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
