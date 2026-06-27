using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Service for interacting with the Hydrus API to discover and sync comic library data
/// </summary>
public interface IHydrusApiService
{
    /// <summary>
    /// Discovers all title tags in Hydrus
    /// </summary>
    /// <returns>List of title names extracted from title: tags</returns>
    Task<List<string>> DiscoverTitlesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all title tags in Hydrus using an explicit settings instance, allowing callers to
    /// override the tag service and structural namespaces (e.g. mapped imports from another tag service).
    /// </summary>
    /// <param name="settings">Settings to use for discovery (tag service key, namespaces, file domain, API connection).</param>
    /// <returns>List of title names extracted from title: tags.</returns>
    Task<List<string>> DiscoverTitlesAsync(HydrusSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all series tags in Hydrus.
    /// </summary>
    /// <returns>List of title names extracted from structural tags.</returns>
    Task<List<string>> DiscoverSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for files matching the given tags
    /// </summary>
    /// <param name="tags">List of tags to search for (e.g., ["title:the sandman"])</param>
    /// <param name="fileDomain">Optional file domain to scope the search (defaults to configured settings value)</param>
    /// <param name="skipTagService">If true, ignores the configured tag service key and searches without it (uses Hydrus default "my tags")</param>
    /// <returns>List of file IDs matching the search criteria</returns>
    Task<List<long>> SearchFilesAsync(List<string> tags, string? fileDomain = null, bool skipTagService = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for files matching the given tags using an explicit settings instance, allowing
    /// callers to override the tag service and file domain (e.g. mapped imports from another tag service).
    /// </summary>
    /// <param name="settings">Settings to use for the search (tag service key, file domain, API connection).</param>
    /// <param name="tags">List of tags to search for (e.g. ["title:the sandman"]).</param>
    /// <param name="fileDomain">Optional file domain to scope the search (defaults to the provided settings value).</param>
    /// <param name="skipTagService">If true, ignores the settings tag service key and searches without it.</param>
    /// <returns>List of file IDs matching the search criteria.</returns>
    Task<List<long>> SearchFilesAsync(HydrusSettings settings, List<string> tags, string? fileDomain = null, bool skipTagService = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed metadata for files, including their tags.
    /// </summary>
    /// <param name="fileIds">List of file IDs to get metadata for</param>
    /// <param name="includeNotes">Whether to include Hydrus notes in the metadata payload.</param>
    /// <returns>List of file metadata objects</returns>
    Task<List<FileMetadata>> GetFileMetadataAsync(List<long> fileIds, bool includeNotes = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed metadata for files by hash, including their tags.
    /// </summary>
    /// <param name="hashes">List of file hashes to get metadata for.</param>
    /// <param name="includeNotes">Whether to include Hydrus notes in the metadata payload.</param>
    /// <returns>List of file metadata objects.</returns>
    Task<List<FileMetadata>> GetFileMetadataByHashesAsync(List<string> hashes, bool includeNotes = false, CancellationToken cancellationToken = default);

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
    /// Gets the page count for a specific title by searching for files with the title tag and page namespace.
    /// </summary>
    /// <param name="titleName">The title name to count pages for.</param>
    /// <param name="titleNamespace">The namespace prefix for title tags (e.g. "title:").</param>
    /// <param name="pageNamespace">The namespace prefix for page tags (e.g. "page:").</param>
    /// <returns>Number of distinct pages/files found for the title.</returns>
    Task<int> GetTitlePageCountAsync(string titleName, string titleNamespace, string pageNamespace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the page count for a specific title using an explicit settings instance, allowing callers to
    /// override the tag service and namespaces.
    /// </summary>
    /// <param name="settings">Settings to use for the search (tag service key, namespaces, file domain, API connection).</param>
    /// <param name="titleName">The title name to count pages for.</param>
    /// <param name="titleNamespace">The namespace prefix for title tags (e.g. "title:").</param>
    /// <param name="pageNamespace">The namespace prefix for page tags (e.g. "page:").</param>
    /// <returns>Number of distinct pages/files found for the title.</returns>
    Task<int> GetTitlePageCountAsync(HydrusSettings settings, string titleName, string titleNamespace, string pageNamespace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available services (file domains, tag services)
    /// </summary>
    /// <returns>Services response containing all configured services</returns>
    Task<ServicesResponse> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds tags to a file in Hydrus
    /// </summary>
    /// <param name="hash">File hash</param>
    /// <param name="serviceKey">Service name to add tags to (e.g., "my tags")</param>
    /// <param name="tags">List of tags to add</param>
    Task AddTagsAsync(string hash, string serviceKey, List<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads raw file bytes to Hydrus via POST /add_files/add_file.
    /// </summary>
    /// <param name="content">Raw bytes of the file to upload.</param>
    /// <param name="mimeType">MIME type of the file (e.g. "image/jpeg").</param>
    /// <returns>Result containing the Hydrus hash and import status.</returns>
    Task<HydrusAddFileResult> AddFileAsync(byte[] content, string mimeType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates notes associated with a file in Hydrus.
    /// </summary>
    /// <param name="hash">File hash</param>
    /// <param name="notes">Notes map of name to text.</param>
    /// <param name="mergeCleverly">Whether Hydrus should merge notes using duplicate resolution logic.</param>
    /// <param name="extendExistingNoteIfPossible">Hydrus merge setting for extending existing notes.</param>
    /// <param name="conflictResolution">Hydrus conflict mode: 0 replace, 1 ignore, 2 append, 3 rename.</param>
    /// <returns>The notes Hydrus reports as written in this operation.</returns>
    Task<Dictionary<string, string>> SetNotesAsync(
        string hash,
        Dictionary<string, string> notes,
        bool mergeCleverly = false,
        bool extendExistingNoteIfPossible = true,
        int conflictResolution = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the Hydrus API connection
    /// </summary>
    /// <returns>True if connection is successful</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
