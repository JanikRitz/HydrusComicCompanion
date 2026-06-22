using System.Text.Json;
using HydrusComicCompanion.Models;
using Microsoft.Extensions.Options;

namespace HydrusComicCompanion.Services;

public class HydrusApiService : IHydrusApiService
{
    private readonly HttpClient _httpClient;
    private readonly HydrusSettings _settings;
    private readonly ILogger<HydrusApiService> _logger;

    public HydrusApiService(
        HttpClient httpClient,
        IOptions<HydrusSettings> settings,
        ILogger<HydrusApiService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
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
            // Search for all tags starting with the series namespace
            var searchPrefix = _settings.SeriesNamespace;
            var encodedSearch = Uri.EscapeDataString($"search={searchPrefix}*");
            var url = $"{_settings.ApiUrl}/add_tags/search_tags?{encodedSearch}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var tagResponse = JsonSerializer.Deserialize<TagSearchResponse>(jsonContent);

            if (tagResponse?.Tags != null)
            {
                foreach (var tag in tagResponse.Tags)
                {
                    // Extract series name from tag (e.g., "series:the sandman" -> "the sandman")
                    var seriesName = ExtractNamespaceValue(tag.DisplayName, _settings.SeriesNamespace);
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
    public async Task<List<string>> SearchFilesAsync(
        List<string> tags,
        string? fileServiceKey = null,
        CancellationToken cancellationToken = default)
    {
        var hashes = new List<string>();

        try
        {
            var url = $"{_settings.ApiUrl}/get_files/search_files";

            // Build query parameters
            var queryParams = new Dictionary<string, string>
            {
                ["tags"] = JsonSerializer.Serialize(tags)
            };

            if (!string.IsNullOrEmpty(fileServiceKey))
            {
                queryParams["file_service_key"] = fileServiceKey;
            }

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var fullUrl = $"{url}?{queryString}";

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            AddApiKeyHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResponse = JsonSerializer.Deserialize<FileSearchResponse>(jsonContent);

            if (searchResponse?.Hashes != null)
            {
                hashes.AddRange(searchResponse.Hashes);
            }

            _logger.LogInformation("Found {Count} files matching tags: {Tags}", hashes.Count, string.Join(", ", tags));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files in Hydrus with tags: {Tags}", string.Join(", ", tags));
            throw;
        }

        return hashes;
    }

    /// <summary>
    /// Gets detailed metadata for files, including their tags
    /// </summary>
    public async Task<List<FileMetadata>> GetFileMetadataAsync(List<string> hashes, CancellationToken cancellationToken = default)
    {
        var metadata = new List<FileMetadata>();

        if (hashes.Count == 0)
        {
            return metadata;
        }

        try
        {
            var url = $"{_settings.ApiUrl}/get_files/file_metadata";

            var queryParams = JsonSerializer.Serialize(hashes);
            var queryString = $"hashes={Uri.EscapeDataString(queryParams)}";
            var fullUrl = $"{url}?{queryString}";

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            AddApiKeyHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var metadataResponse = JsonSerializer.Deserialize<FileMetadataResponse>(jsonContent);

            if (metadataResponse?.Metadata != null)
            {
                metadata.AddRange(metadataResponse.Metadata);
            }

            _logger.LogInformation("Retrieved metadata for {Count} files", metadata.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file metadata from Hydrus for {Count} hashes", hashes.Count);
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
            var url = $"{_settings.ApiUrl}/get_files/file?hash={Uri.EscapeDataString(hash)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request);

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
            var url = $"{_settings.ApiUrl}/get_services";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request);

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
            var url = $"{_settings.ApiUrl}/add_tags/add_tags";

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
            AddApiKeyHeader(httpRequest);

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
            var url = $"{_settings.ApiUrl}/get_services";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request);

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

    /// <summary>
    /// Adds the API key header to the request
    /// </summary>
    private void AddApiKeyHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_settings.ApiAccessKey))
        {
            request.Headers.Add("Hydrus-Client-API-Access-Key", _settings.ApiAccessKey);
        }
    }
}
