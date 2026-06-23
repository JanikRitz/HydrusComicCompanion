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
    /// Discovers all series tags in Hydrus using the tag search endpoint
    /// </summary>
    public async Task<List<string>> DiscoverSeriesAsync(CancellationToken cancellationToken = default)
    {
        var seriesNames = new List<string>();

        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);

            // Search for all tags starting with the series namespace (strip trailing colon for the search query)
            var searchPrefix = settings.SeriesNamespace.TrimEnd(':');
            var queryString = $"search={Uri.EscapeDataString(searchPrefix)}";
            if (!string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                // Hydrus doesn't properly resolve the Tag service on tag_search and returns and empty result (?)
                // TODO fix this on Hydrus side (?)
                // queryString += $"&tag_service_key={Uri.EscapeDataString(settings.TagServiceKey)}";
            }
            queryString += $"&tag_display_type=display";
            var url = $"{settings.ApiUrl}/add_tags/search_tags?{queryString}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var tagResponse = JsonSerializer.Deserialize<TagSearchResponse>(jsonContent);

            if (tagResponse?.Tags != null)
            {
                foreach (var tag in tagResponse.Tags)
                {
                    // Extract series name from tag (e.g., "series:the sandman" -> "the sandman")
                    var seriesName = ExtractNamespaceValue(tag.Value, settings.SeriesNamespace);
                    if (!string.IsNullOrEmpty(seriesName))
                    {
                        seriesNames.Add(seriesName);
                    }
                }
            }

            _logger.LogInformation("Discovered {Count} series from Hydrus", seriesNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering series from Hydrus");
            throw;
        }

        return seriesNames;
    }

    /// <summary>
    /// Searches for files matching the given tags
    /// </summary>
    public async Task<List<long>> SearchFilesAsync(
        List<string> tags,
        string? fileDomain = null,
        CancellationToken cancellationToken = default)
    {
        var fileIds = new List<long>();

        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/get_files/search_files";

            // Build query parameters
            var selectedFileDomain = string.IsNullOrWhiteSpace(fileDomain)
                ? settings.TargetFileDomain
                : fileDomain;
            var fileDomainKey = await ResolveFileDomainKeyAsync(settings, selectedFileDomain, cancellationToken);

            var queryParams = new Dictionary<string, string>
            {
                ["tags"] = JsonSerializer.Serialize(tags)
            };

            if (!string.IsNullOrWhiteSpace(fileDomainKey))
            {
                queryParams["file_domain"] = fileDomainKey;
            }

            if (!string.IsNullOrWhiteSpace(settings.TagServiceKey))
            {
                queryParams["tag_service_key"] = settings.TagServiceKey;
            }

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var fullUrl = $"{url}?{queryString}";
            _logger.LogDebug("Hydrus file search request URL: {RequestUrl}", fullUrl);

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

            if (searchResponse?.FileIds != null)
            {
                fileIds.AddRange(searchResponse.FileIds);
            }

            _logger.LogInformation("Found {Count} files matching tags: {Tags}", fileIds.Count, string.Join(", ", tags));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files in Hydrus with tags: {Tags}", string.Join(", ", tags));
            throw;
        }

        return fileIds;
    }

    /// <summary>
    /// Gets detailed metadata for files, including their tags
    /// </summary>
    public async Task<List<FileMetadata>> GetFileMetadataAsync(List<long> fileIds, CancellationToken cancellationToken = default)
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
                    ["include_notes"] = "false",
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
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/get_files/file?hash={Uri.EscapeDataString(hash)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request, settings);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file from Hydrus: {Hash}", hash);
            throw;
        }
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
    public async Task AddTagsAsync(string hash, string serviceName, List<string> tags, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken);
            var url = $"{settings.ApiUrl}/add_tags/add_tags";

            var request = new AddTagsRequest
            {
                Hash = hash,
                ServiceNamesTags = new Dictionary<string, List<string>>
                {
                    { serviceName, tags }
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
                tags.Count, hash, serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tags to file {Hash} in Hydrus", hash);
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
