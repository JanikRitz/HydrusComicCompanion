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
    /// <param name="fileServiceKey">Optional file service key to scope the search</param>
    /// <returns>List of file hashes matching the search criteria</returns>
    Task<List<string>> SearchFilesAsync(List<string> tags, string? fileServiceKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed metadata for files, including their tags
    /// </summary>
    /// <param name="hashes">List of file hashes to get metadata for</param>
    /// <returns>List of file metadata objects</returns>
    Task<List<FileMetadata>> GetFileMetadataAsync(List<string> hashes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw file bytes from Hydrus
    /// </summary>
    /// <param name="hash">The file hash to retrieve</param>
    /// <returns>Stream of the file content</returns>
    Task<Stream> GetFileAsync(string hash, CancellationToken cancellationToken = default);

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
