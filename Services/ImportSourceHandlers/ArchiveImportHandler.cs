using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services.Abstractions;
using Microsoft.AspNetCore.Components.Forms;

namespace HydrusComicCompanion.Services.ImportSourceHandlers;

/// <summary>
/// Handles imports from CBZ/CBR archive files.
/// Wraps the existing IComicImportService to extract pages and metadata from archives.
/// </summary>
public class ArchiveImportHandler : IImportSourceHandler
{
    private readonly IComicImportService _importService;

    public ImportSource Source => ImportSource.Archive;

    public string DisplayName => "Comic Archive";

    public string Description => "Import a CBZ or CBR archive. Pages are uploaded to Hydrus and tagged automatically.";

    public ArchiveImportHandler(IComicImportService importService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
    }

    /// <summary>
    /// Extracts pages from a CBZ/CBR archive file.
    /// </summary>
    /// <param name="sourceIdentifier">
    /// Expected to be an IBrowserFile from an InputFile component.
    /// The file stream will be opened and passed to the import service.
    /// </param>
    public async Task<ComicImportPreparation> ExtractAsync(
        object sourceIdentifier,
        CancellationToken cancellationToken = default)
    {
        if (sourceIdentifier is not IBrowserFile file)
        {
            throw new ArgumentException(
                $"Archive handler expects IBrowserFile; got {sourceIdentifier?.GetType().Name ?? "null"}",
                nameof(sourceIdentifier));
        }

        // 2 GB max file size for archive imports
        const long maxArchiveSizeBytes = 2L * 1024 * 1024 * 1024;

        await using var stream = file.OpenReadStream(maxAllowedSize: maxArchiveSizeBytes);
        return await _importService.ExtractArchiveAsync(stream, file.Name, cancellationToken);
    }

    /// <summary>
    /// Completes the import by uploading pages to Hydrus and writing to the local cache.
    /// </summary>
    public Task<int> CompleteImportAsync(
        ComicImportRequest request,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _importService.ImportComicAsync(request, progress, cancellationToken);
    }
}
