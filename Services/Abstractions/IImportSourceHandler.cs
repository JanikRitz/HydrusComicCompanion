using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services.Abstractions;

/// <summary>
/// Represents different sources for importing comic content into the system.
/// </summary>
public enum ImportSource
{
    /// <summary>Import from a local CBZ/CBR archive file.</summary>
    Archive,

    /// <summary>Import by syncing an existing title from Hydrus.</summary>
    Title,

    /// <summary>Import from an open Hydrus page (future feature).</summary>
    OpenHydrusPage,

    /// <summary>Import from a Calibre book library (future feature).</summary>
    CalibreBook
}

/// <summary>
/// Interface for handling import operations from different sources.
/// Each source (Archive, Title, Calibre, etc.) implements this interface
/// to provide pages and metadata in a standardized format.
/// </summary>
public interface IImportSourceHandler
{
    /// <summary>
    /// Gets the source type this handler manages.
    /// </summary>
    ImportSource Source { get; }

    /// <summary>
    /// Gets a user-friendly name for this import source (e.g., "Comic Archive", "Hydrus Title").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets a description of what this import source does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Extracts pages, metadata, and initial chapter state from the given source identifier.
    /// For archives, this would be a file stream; for titles, a title name, etc.
    /// </summary>
    /// <param name="sourceIdentifier">Source-specific identifier (e.g., file stream, title name)</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Shared import preparation data.</returns>
    Task<ComicImportPreparation> ExtractAsync(
        object sourceIdentifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the import operation after the user has configured metadata and chapters.
    /// </summary>
    /// <param name="request">Import configuration including pages, chapter boundaries, and metadata.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the imported/synced title.</returns>
    Task<int> CompleteImportAsync(
        ComicImportRequest request,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
