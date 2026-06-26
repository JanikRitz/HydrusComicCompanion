using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydrusComicCompanion.Models;

/// <summary>
/// Response from Hydrus tag search endpoint
/// </summary>
public class TagSearchResponse
{
    [JsonPropertyName("tags")]
    public List<TagInfo> Tags { get; set; } = new();
}

/// <summary>
/// Individual tag information from Hydrus
/// </summary>
public class TagInfo
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>
/// Response from Hydrus file search endpoint
/// </summary>
public class FileSearchResponse
{
    [JsonPropertyName("file_ids")]
    public List<long> FileIds { get; set; } = new();

    [JsonPropertyName("hashes")]
    public List<string> Hashes { get; set; } = new();
}

/// <summary>
/// File metadata response from Hydrus
/// </summary>
public class FileMetadataResponse
{
    [JsonPropertyName("services")]
    public JsonElement Services { get; set; }

    [JsonPropertyName("metadata")]
    public List<FileMetadata> Metadata { get; set; } = new();
}

/// <summary>
/// Individual file metadata
/// </summary>
public class FileMetadata
{
    [JsonPropertyName("file_id")]
    public long FileId { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("mime")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("is_inbox")]
    public bool IsInbox { get; set; }

    [JsonPropertyName("time_modified")]
    public double? TimeModified { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, ServiceTagBucket> Tags { get; set; } = new();

    [JsonPropertyName("notes")]
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /*
     * The tags structure is similar to the /add_tags/add_tags scheme, excepting that the status numbers are:
       0 - current
       1 - pending
       2 - deleted
       3 - petitioned
     */

    public IReadOnlyList<string> GetStorageTagsForService(string? serviceKey)
    {
        if (string.IsNullOrWhiteSpace(serviceKey))
        {
            return Array.Empty<string>();
        }

        if (!Tags.TryGetValue(serviceKey, out var serviceTags))
        {
            return Array.Empty<string>();
        }

        // Only use 0 -> current tags 
        return serviceTags.StorageTags.TryGetValue("0", out var currentTags)
            ? currentTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
    }
    public IReadOnlyList<string> GetAllStorageTags()
    {
        return Tags
            .SelectMany(kvp => kvp.Value.StorageTags.TryGetValue("0", out var currentTags)
                ? currentTags.AsEnumerable()
                : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetStorageTagsExcludingService(string? excludedServiceKey)
    {
        return Tags
            .Where(kvp => !string.Equals(kvp.Key, excludedServiceKey, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kvp => kvp.Value.StorageTags.TryGetValue("0", out var currentTags)
                ? currentTags.AsEnumerable()
                : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Tag buckets grouped by status for a single service key.
/// </summary>
public class ServiceTagBucket
{
    [JsonPropertyName("storage_tags")]
    public Dictionary<string, List<string>> StorageTags { get; set; } = new();

    [JsonPropertyName("display_tags")]
    public Dictionary<string, List<string>> DisplayTags { get; set; } = new();
}

/// <summary>
/// Response from Hydrus services endpoint
/// </summary>
public class ServicesResponse
{
    [JsonPropertyName("local_file_services")]
    public List<ServiceInfo> LocalFileServices { get; set; } = new();

    [JsonPropertyName("file_repositories")]
    public List<ServiceInfo> FileRepositories { get; set; } = new();

    [JsonPropertyName("local_tag_services")]
    public List<ServiceInfo> LocalTagServices { get; set; } = new();

    [JsonPropertyName("tag_repositories")]
    public List<ServiceInfo> TagRepositories { get; set; } = new();
}

/// <summary>
/// Individual service information
/// </summary>
public class ServiceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("service_key")]
    public string ServiceKey { get; set; } = string.Empty;

    [JsonPropertyName("service_type")]
    public int ServiceType { get; set; }
}

/// <summary>
/// Request payload for adding tags to files
/// </summary>
public class AddTagsRequest
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("service_keys_to_tags")]
    public Dictionary<string, List<string>> ServiceKeysToTags { get; set; } = new();
}

public sealed class SetNotesRequest
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("merge_cleverly")]
    public bool MergeCleverly { get; set; }

    [JsonPropertyName("extend_existing_note_if_possible")]
    public bool ExtendExistingNoteIfPossible { get; set; } = true;

    [JsonPropertyName("conflict_resolution")]
    public int ConflictResolution { get; set; } = 3;
}

public sealed class SetNotesResponse
{
    [JsonPropertyName("notes")]
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Stream payload returned from Hydrus media endpoints.
/// </summary>
public sealed class HydrusMediaResult
{
    public required Stream Content { get; init; }

    public string ContentType { get; init; } = "application/octet-stream";
}
