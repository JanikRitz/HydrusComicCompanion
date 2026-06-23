using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Service for interacting with the Hydrus API to discover and sync comic library data
/// </summary>
public interface IHydrusApiService
{
    /// <summary>
    /// Discovers all series tags in Hydrus
    /// </summary>
    /// <returns>List of series names extracted from series: tags</returns>
    Task<List<string>> DiscoverSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for files matching the given tags
    /// </summary>
    /// <param name="tags">List of tags to search for (e.g., ["series:the sandman"])</param>
    /// <param name="fileDomain">Optional file domain to scope the search (defaults to configured settings value)</param>
    /// <returns>List of file IDs matching the search criteria</returns>
    Task<List<long>> SearchFilesAsync(List<string> tags, string? fileDomain = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed metadata for files, including their tags
    /// </summary>
    /// <param name="fileIds">List of file IDs to get metadata for</param>
    /// <returns>List of file metadata objects</returns>
    Task<List<FileMetadata>> GetFileMetadataAsync(List<long> fileIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw file bytes from Hydrus.
    /// </summary>
    /// <param name="hash">The file hash to retrieve.</param>
    /// <returns>Stream of the file content.</returns>
    Task<Stream> GetFileAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a file thumbnail from Hydrus.
    /// </summary>
    /// <param name="hash">The file hash to retrieve.</param>
    /// <returns>Hydrus media stream result including Content-Type.</returns>
    Task<HydrusMediaResult> GetThumbnailAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a rendered image from Hydrus.
    /// </summary>
    /// <param name="hash">The file hash to retrieve.</param>
    /// <param name="width">Optional maximum render width. Must be paired with height when provided.</param>
    /// <param name="height">Optional maximum render height. Must be paired with width when provided.</param>
    /// <param name="renderFormat">Optional Hydrus render format enum value.</param>
    /// <param name="renderQuality">Optional render quality value for selected format.</param>
    /// <param name="download">Whether the response should use attachment content disposition.</param>
    /// <returns>Hydrus media stream result including Content-Type.</returns>
    Task<HydrusMediaResult> GetRenderedImageAsync(
        string hash,
        int? width = null,
        int? height = null,
        int? renderFormat = null,
        int? renderQuality = null,
        bool download = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the original file stream from Hydrus with optional download disposition.
    /// </summary>
    /// <param name="hash">The file hash to retrieve.</param>
    /// <param name="download">Whether the response should use attachment content disposition.</param>
    /// <returns>Hydrus media stream result including Content-Type.</returns>
    Task<HydrusMediaResult> GetOriginalFileAsync(string hash, bool download = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available services (file domains, tag services)
    /// </summary>
    /// <returns>Services response containing all configured services</returns>
    Task<ServicesResponse> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds tags to a file in Hydrus
    /// </summary>
    /// <param name="hash">File hash</param>
    /// <param name="serviceName">Service name to add tags to (e.g., "my tags")</param>
    /// <param name="tags">List of tags to add</param>
    Task AddTagsAsync(string hash, string serviceName, List<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the Hydrus API connection
    /// </summary>
    /// <returns>True if connection is successful</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
