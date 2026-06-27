using System.Text.Json;
using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

public class HydrusApiService : IHydrusApiService
{
    private readonly HttpClient _httpClient;
    private readonly IHydrusSettingsService _settingsService;
    private readonly ILogger<HydrusApiService> _logger;

    public HydrusApiService(
        HttpClient httpClient,
        IHydrusSettingsService settingsService,
        ILogger<HydrusApiService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Discovers all title tags in Hydrus using file search and metadata extraction.
    /// </summary>
    public Task<List<string>> DiscoverSeriesAsync(CancellationToken cancellationToken = default)
        => DiscoverTitlesAsync(cancellationToken);

    /// <summary>
    /// Discovers all title tags in Hydrus using file search and metadata extraction.
    /// </summary>
    public async Task<List<string>> DiscoverTitlesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        return await DiscoverTitlesAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Discovers all title tags in Hydrus using file search and metadata extraction with an explicit
    /// settings instance, so callers can read from another tag service and namespace mapping.
    /// </summary>
    public async Task<List<string>> DiscoverTitlesAsync(HydrusSettings settings, CancellationToken cancellationToken = default)
    {
        var titleNames = new List<string>();

        try
        {
            var titleNamespace = NormalizeNamespace(settings.TitleNamespace, "title:");
            var pageNamespace = NormalizeNamespace(settings.PageNamespace, "page:");

            var coverPageTag = string.IsNullOrWhiteSpace(settings.CoverPageTag)
                ? "meta:cover page"
                : settings.CoverPageTag.Trim();

            var discoveryTags = new List<object>
            {
                $"{titleNamespace}*",
                new List<string>
                {
                    $"{pageNamespace}1",
                    coverPageTag
                }
            };

            var fileIds = await SearchFilesInternalAsync(settings, discoveryTags, settings.TargetFileDomain, skipTagService: false, cancellationToken);
            if (fileIds.Count == 0)
            {
                _logger.LogInformation("Discovered 0 titles from Hydrus (no files matched title discovery query).");
                return titleNames;
            }

            var fileMetadata = await GetFileMetadataAsync(fileIds, cancellationToken: cancellationToken);

            foreach (var metadata in fileMetadata)
            {
                var tagsToInspect = !string.IsNullOrWhiteSpace(settings.TagServiceKey)
                    ? metadata.GetStorageTagsForService(settings.TagServiceKey)
                    : metadata.GetStorageTagsExcludingService(null);

                foreach (var tag in tagsToInspect)
                {
                    var titleName = ExtractNamespaceValue(tag, titleNamespace);
                    if (!string.IsNullOrWhiteSpace(titleName))
                    {
                        titleNames.Add(titleName);
                    }
                }
            }

            titleNames = titleNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Discovered {Count} titles from Hydrus", titleNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering titles from Hydrus");
            throw;
        }

        return titleNames;
    }

    /// <summary>
    /// Gets the page count for a specific title by searching for files with the title tag and page namespace.
    /// </summary>
    public async Task<int> GetTitlePageCountAsync(
        string titleName,
        string titleNamespace,
        string pageNamespace,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        return await GetTitlePageCountAsync(settings, titleName, titleNamespace, pageNamespace, cancellationToken);
    }

    /// <summary>
    /// Gets the page count for a specific title using an explicit settings instance.
    /// </summary>
    public async Task<int> GetTitlePageCountAsync(
        HydrusSettings settings,
        string titleName,
        string titleNamespace,
        string pageNamespace,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPageNamespace = NormalizeNamespace(pageNamespace, "page:");

            // Build the title tag using the settings (or provided titleNamespace if non-empty)
            var titleNamespacePrefix = string.IsNullOrWhiteSpace(titleNamespace)
                ? settings.TitleNamespace.Trim().TrimEnd(':')
                : titleNamespace.Trim().TrimEnd(':');

            var titleTag = string.IsNullOrWhiteSpace(titleNamespacePrefix)
                ? titleName
                : $"{titleNamespacePrefix}:{titleName}";

            var pageWildcard = $"{normalizedPageNamespace}*";

            var searchTags = new List<string> { titleTag, pageWildcard };

            var fileIds = await SearchFilesInternalAsync(settings, searchTags, settings.TargetFileDomain, skipTagService: false, cancellationToken);
            return fileIds.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting page count for title: {TitleName}", titleName);
            return 0;
        }
    }

    /// <summary>
    /// Searches for files matching the given tags.
    /// </summary>
    public async Task<List<long>> SearchFilesAsync(
        List<string> tags,
        string? fileDomain = null,
        bool skipTagService = false,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        return await SearchFilesAsync(settings, tags, fileDomain, skipTagService, cancellationToken);
    }

    /// <summary>
    /// Searches for files matching the given tags using an explicit settings instance.
    /// </summary>
    public async Task<List<long>> SearchFilesAsync(
        HydrusSettings settings,
        List<string> tags,
        string? fileDomain = null,
        bool skipTagService = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileIds = await SearchFilesInternalAsync(settings, tags, fileDomain, skipTagService, cancellationToken);

            _logger.LogInformation("Found {Count} files matching tags: {Tags}", fileIds.Count, string.Join(", ", tags));
            return fileIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files in Hydrus with tags: {Tags}", string.Join(", ", tags));
            throw;
        }
    }

    /// <summary>
    /// Gets detailed metadata for files, including their tags
    /// </summary>
    public async Task<List<FileMetadata>> GetFileMetadataAsync(List<long> fileIds, bool includeNotes = false, CancellationToken cancellationToken = default)
    {
        var metadata = new List<FileMetadata>();

        if (fileIds.Count == 0)
        {
            return metadata;
        }

        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/get_files/file_metadata";

            foreach (var batch in fileIds.Chunk(256))
            {
                var queryParams = new Dictionary<string, string>
                {
                    ["file_ids"] = JsonSerializer.Serialize(batch),
                    ["create_new_file_ids"] = "false",
                    ["only_return_identifiers"] = "false",
                    ["only_return_basic_information"] = "false",
                    ["detailed_url_information"] = "false",
                    ["include_blurhash"] = "false",
                    ["include_milliseconds"] = "true",
                    ["include_notes"] = includeNotes ? "true" : "false",
                    ["include_services_object"] = "false"
                };

                var queryString = string.Join("&", queryParams.Select(kvp =>
                    $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                var fullUrl = $"{url}?{queryString}";

                var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                AddApiKeyHeader(request, settings);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Hydrus file metadata request failed. Status: {StatusCode}. URL: {RequestUrl}. Response: {ResponseBody}",
                        (int)response.StatusCode,
                        fullUrl,
                        errorContent);
                    response.EnsureSuccessStatusCode();
                }

                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var metadataResponse = JsonSerializer.Deserialize<FileMetadataResponse>(jsonContent);

                if (metadataResponse?.Metadata != null)
                {
                    metadata.AddRange(metadataResponse.Metadata);
                }
            }

            _logger.LogInformation("Retrieved metadata for {Count} files", metadata.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file metadata from Hydrus for {Count} file IDs", fileIds.Count);
            throw;
        }

        return metadata;
    }

    /// <summary>
    /// Gets the raw file bytes from Hydrus
    /// </summary>
    public async Task<Stream> GetFileAsync(string hash, CancellationToken cancellationToken = default)
    {
        var mediaResult = await GetOriginalFileAsync(hash, cancellationToken: cancellationToken);
        return mediaResult.Content;
    }

    public Task<HydrusMediaResult> GetThumbnailAsync(string hash, CancellationToken cancellationToken = default)
    {
        var query = $"hash={Uri.EscapeDataString(hash)}";
        return GetMediaAsync("/get_files/thumbnail", query, "thumbnail", hash, cancellationToken);
    }

    public async Task<HydrusMediaResult> GetRenderedImageAsync(
        string hash,
        int? width = null,
        int? height = null,
        int? renderFormat = null,
        int? renderQuality = null,
        bool download = false,
        CancellationToken cancellationToken = default)
    {
        if ((width.HasValue && !height.HasValue) || (!width.HasValue && height.HasValue))
        {
            throw new ArgumentException("Width and height must both be provided when one is specified.");
        }

        if ((width.HasValue && width.Value <= 0) || (height.HasValue && height.Value <= 0))
        {
            throw new ArgumentException("Width and height must be greater than zero when provided.");
        }

        var queryParams = new List<string>
        {
            $"hash={Uri.EscapeDataString(hash)}"
        };

        if (download)
        {
            queryParams.Add("download=true");
        }

        if (renderFormat.HasValue)
        {
            queryParams.Add($"render_format={renderFormat.Value}");
        }

        if (renderQuality.HasValue)
        {
            queryParams.Add($"render_quality={renderQuality.Value}");
        }

        if (width.HasValue && height.HasValue)
        {
            var resolvedDimensions = await ResolveRenderDimensionsAsync(hash, width.Value, height.Value, cancellationToken);
            queryParams.Add($"width={resolvedDimensions.Width}");
            queryParams.Add($"height={resolvedDimensions.Height}");
        }

        var query = string.Join("&", queryParams);
        return await GetMediaAsync("/get_files/render", query, "rendered image", hash, cancellationToken);
    }

    public Task<HydrusMediaResult> GetOriginalFileAsync(string hash, bool download = false, CancellationToken cancellationToken = default)
    {
        var query = download
            ? $"hash={Uri.EscapeDataString(hash)}&download=true"
            : $"hash={Uri.EscapeDataString(hash)}";

        return GetMediaAsync("/get_files/file", query, "file", hash, cancellationToken);
    }

    /// <summary>
    /// Discovers all available services (file domains, tag services)
    /// </summary>
    public async Task<ServicesResponse> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/get_services";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var services = JsonSerializer.Deserialize<ServicesResponse>(jsonContent) ?? new ServicesResponse();

            _logger.LogInformation("Retrieved services: {FileServices} file services, {TagServices} tag services",
                services.LocalFileServices.Count + services.FileRepositories.Count,
                services.LocalTagServices.Count + services.TagRepositories.Count);

            return services;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving services from Hydrus");
            throw;
        }
    }

    /// <summary>
    /// Adds tags to a file in Hydrus
    /// </summary>
    public async Task AddTagsAsync(string hash, string serviceKey, List<string> tags, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/add_tags/add_tags";

            var request = new AddTagsRequest
            {
                Hash = hash,
                ServiceKeysToTags = new Dictionary<string, List<string>>
                {
                    { serviceKey, tags }
                }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = httpContent
            };
            AddApiKeyHeader(httpRequest, settings);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Added {Count} tags to file {Hash} in service {Service}",
                tags.Count, hash, serviceKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tags to file {Hash} in Hydrus", hash);
            throw;
        }
    }

    // TODO add a way to add + remove tags to update the metadata when import needs to change it (e.g. removing old page:1 and setting it to page:2)

    public async Task<Dictionary<string, string>> SetNotesAsync(
        string hash,
        Dictionary<string, string> notes,
        bool mergeCleverly = false,
        bool extendExistingNoteIfPossible = true,
        int conflictResolution = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Hash is required.", nameof(hash));
        }

        if (notes.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/add_notes/set_notes";

            var request = new SetNotesRequest
            {
                Hash = hash,
                Notes = notes,
                MergeCleverly = mergeCleverly,
                ExtendExistingNoteIfPossible = extendExistingNoteIfPossible,
                ConflictResolution = conflictResolution
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = httpContent
            };

            AddApiKeyHeader(httpRequest, settings);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Hydrus set notes request failed. Status: {StatusCode}. URL: {RequestUrl}. Response: {ResponseBody}",
                    (int)response.StatusCode,
                    url,
                    errorContent);
                response.EnsureSuccessStatusCode();
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var setNotesResponse = JsonSerializer.Deserialize<SetNotesResponse>(responseJson);
            var writtenNotes = setNotesResponse?.Notes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Updated {Count} notes for file {Hash}", writtenNotes.Count, hash);
            return writtenNotes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting notes for file {Hash} in Hydrus", hash);
            throw;
        }
    }

    /// <summary>
    /// Tests the Hydrus API connection
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/get_services";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully connected to Hydrus API");
                return true;
            }

            _logger.LogWarning("Hydrus API connection failed with status code: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Hydrus API connection");
            return false;
        }
    }

    /// <summary>
    /// Uploads raw file bytes to Hydrus via POST /add_files/add_file.
    /// </summary>
    public async Task<HydrusAddFileResult> AddFileAsync(byte[] content, string mimeType, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/add_files/add_file";

            using var httpContent = new ByteArrayContent(content);
            httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = httpContent };
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<HydrusAddFileResult>(jsonContent) ?? new HydrusAddFileResult();

            _logger.LogInformation("File add result: status={Status}, hash={Hash}", result.Status, result.Hash);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding file to Hydrus");
            throw;
        }
    }

    /// <summary>
    /// Extracts a value from a namespaced tag
    /// </summary>
    private static string ExtractNamespaceValue(string tag, string namespaceName)
    {
        if (tag.StartsWith(namespaceName, StringComparison.OrdinalIgnoreCase))
        {
            return tag[namespaceName.Length..];
        }

        return string.Empty;
    }

    private async Task<(int Width, int Height)> ResolveRenderDimensionsAsync(
        string hash,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        var originalDimensions = await TryGetImageDimensionsAsync(hash, cancellationToken);
        if (!originalDimensions.HasValue)
        {
            return (maxWidth, maxHeight);
        }

        var (originalWidth, originalHeight) = originalDimensions.Value;

        var scale = Math.Min((double)maxWidth / originalWidth, (double)maxHeight / originalHeight);
        if (scale >= 1d)
        {
            return (originalWidth, originalHeight);
        }

        var targetWidth = Math.Clamp((int)Math.Floor(originalWidth * scale), 1, maxWidth);
        var targetHeight = Math.Clamp((int)Math.Floor(originalHeight * scale), 1, maxHeight);

        return (targetWidth, targetHeight);
    }

    private async Task<(int Width, int Height)?> TryGetImageDimensionsAsync(string hash, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/get_files/file_metadata";

            var queryParams = new Dictionary<string, string>
            {
                ["hashes"] = JsonSerializer.Serialize(new[] { hash }),
                ["create_new_file_ids"] = "false",
                ["only_return_identifiers"] = "false",
                ["only_return_basic_information"] = "true",
                ["detailed_url_information"] = "false",
                ["include_blurhash"] = "false",
                ["include_milliseconds"] = "false",
                ["include_notes"] = "false",
                ["include_services_object"] = "false"
            };

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            var fullUrl = $"{url}?{queryString}";

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var metadataResponse = JsonSerializer.Deserialize<FileMetadataResponse>(jsonContent);
            var metadata = metadataResponse?.Metadata?.FirstOrDefault();

            if (metadata is null || metadata.Width <= 0 || metadata.Height <= 0)
            {
                return null;
            }

            return (metadata.Width, metadata.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve original image dimensions for hash {Hash} before rendering.", hash);
            return null;
        }
    }

    private async Task<HydrusMediaResult> GetMediaAsync(
        string path,
        string query,
        string mediaType,
        string hash,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}{path}?{query}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

            return new HydrusMediaResult
            {
                Content = stream,
                ContentType = contentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving {MediaType} from Hydrus for hash {Hash}", mediaType, hash);
            throw;
        }
    }

    private async Task<List<long>> SearchFilesInternalAsync(
        HydrusSettings settings,
        object tags,
        string? fileDomain,
        bool skipTagService,
        CancellationToken cancellationToken)
    {
        var url = $"{settings.ApiUrl}/get_files/search_files";

        var selectedFileDomain = string.IsNullOrWhiteSpace(fileDomain)
            ? settings.TargetFileDomain
            : fileDomain;
        var fileDomainKey = await ResolveFileDomainKeyAsync(settings, selectedFileDomain, cancellationToken);
        
        var queryParams = new Dictionary<string, string> { ["tags"] = JsonSerializer.Serialize(tags) };
        
        if (!string.IsNullOrWhiteSpace(fileDomainKey)) queryParams["file_domain"] = fileDomainKey;

        // Try searching with the configured tag service first (unless explicitly skipped)
        if (!skipTagService && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
        {
            queryParams["tag_service_key"] = settings.TagServiceKey;

            var queryString = string.Join("&", queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var fullUrl = $"{url}?{queryString}";
            _logger.LogDebug("Hydrus file search request URL: {RequestUrl}", fullUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            AddApiKeyHeader(request, settings);

            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var searchResponse = JsonSerializer.Deserialize<FileSearchResponse>(jsonContent);
                    return searchResponse?.FileIds?.ToList() ?? new List<long>();
                }

                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Hydrus file search with configured tag service failed (Status: {StatusCode}). URL: {RequestUrl}. Response: {ResponseBody}. Falling back to default tag service.",
                    (int)response.StatusCode,
                    fullUrl,
                    errorContent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hydrus file search with configured tag service failed. Falling back to default tag service.");
            }
        }

        // Fallback/direct search: search without tag service key (uses Hydrus default "my tags")
        {
            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var fullUrl = $"{url}?{queryString}";
            var debugMessage = skipTagService
                ? "Hydrus file search request URL (skipping configured tag service): {RequestUrl}"
                : "Hydrus file search request URL (fallback to default tag service): {RequestUrl}";
            _logger.LogDebug(debugMessage, fullUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Hydrus file search failed. Status: {StatusCode}. URL: {RequestUrl}. Response: {ResponseBody}",
                    (int)response.StatusCode,
                    fullUrl,
                    errorContent);
                response.EnsureSuccessStatusCode();
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResponse = JsonSerializer.Deserialize<FileSearchResponse>(jsonContent);

            if (skipTagService && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                _logger.LogInformation("File search succeeded with skipTagService option, bypassing configured tag service.");
            }
            else if (!skipTagService && !string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                _logger.LogInformation("File search succeeded using default tag service as fallback from configured tag service.");
            }

            return searchResponse?.FileIds?.ToList() ?? new List<long>();
        }
    }

    private static string NormalizeNamespace(string namespaceName, string fallback)
    {
        var resolved = string.IsNullOrWhiteSpace(namespaceName)
            ? fallback
            : namespaceName.Trim();

        return resolved.EndsWith(':') ? resolved : $"{resolved}:";
    }

    private async Task<string> ResolveFileDomainKeyAsync(HydrusSettings settings, string? selectedFileDomain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedFileDomain))
        {
            return string.Empty;
        }

        var trimmedDomain = selectedFileDomain.Trim();

        if (IsLikelyServiceKey(trimmedDomain))
        {
            return trimmedDomain;
        }

        try
        {
            var services = await _settingsService.GetServicesAsync(settings, cancellationToken);
            var matchingService = services.FileServices.FirstOrDefault(service =>
                service.Name.Equals(trimmedDomain, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchingService?.Key))
            {
                return matchingService.Key;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve file domain '{FileDomain}' to a service key.", trimmedDomain);
        }

        return trimmedDomain;
    }

    private static bool IsLikelyServiceKey(string value)
    {
        return value.Length == 64 && value.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// Adds the API key header to the request
    /// </summary>
    private static void AddApiKeyHeader(HttpRequestMessage request, HydrusSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiAccessKey))
        {
            request.Headers.Add("Hydrus-Client-API-Access-Key", settings.ApiAccessKey);
        }
    }
}
