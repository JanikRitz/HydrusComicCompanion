using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Service for interacting with a Calibre library via the calibredb command-line tool.
/// </summary>
public interface ICalibreService
{
    /// <summary>
    /// Discovers books in the given Calibre library that have CBZ or CBR formats.
    /// </summary>
    /// <param name="libraryPath">Path passed to calibredb --with-library.</param>
    /// <param name="searchQuery">Optional calibredb search filter. Pass null or empty to retrieve all books.</param>
    /// <param name="progress">Optional progress callback describing the current Calibre phase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Books that have at least one CBZ or CBR format, sorted by display name.</returns>
    Task<IReadOnlyList<CalibreBookEntry>> DiscoverBooksAsync(
        string libraryPath,
        string? searchQuery = null,
        IProgress<LongRunningProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a Calibre book's archive via calibredb and extracts it as import-ready pages.
    /// Also loads and returns the book's OPF metadata.
    /// </summary>
    /// <param name="bookId">Calibre book id.</param>
    /// <param name="libraryPath">Path passed to calibredb --with-library.</param>
    /// <param name="progress">Optional progress callback describing the current Calibre phase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of the extracted <see cref="ComicImportPreparation"/> and the
    /// <see cref="CalibreMetadataSnapshot"/> loaded from the book's OPF metadata.
    /// </returns>
    Task<(ComicImportPreparation Preparation, CalibreMetadataSnapshot Metadata)> ExtractBookAsync(
        int bookId,
        string libraryPath,
        IProgress<LongRunningProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
