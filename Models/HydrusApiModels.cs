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

    [JsonPropertyName("mime_type")]
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
    public long TimeModified { get; set; }

    [JsonPropertyName("tags")]
    public FileTagsMetadata Tags { get; set; } = new();
}

/// <summary>
/// Tag metadata organized by service
/// </summary>
public class FileTagsMetadata
{
    [JsonPropertyName("my tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TagNamespace? MyTags { get; set; }

    [JsonExtensionData]
    public Dictionary<string, TagNamespace> OtherServices { get; set; } = new();
}

/// <summary>
/// A namespace of tags (e.g., "my tags", "public tag repository")
/// </summary>
public class TagNamespace
{
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("display_tags")]
    public List<string>? DisplayTags { get; set; }
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

    [JsonPropertyName("service_names_to_tags")]
    public Dictionary<string, List<string>> ServiceNamesTags { get; set; } = new();
}
