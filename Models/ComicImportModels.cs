using System.Text.Json.Serialization;

namespace HydrusComicCompanion.Models;

/// <summary>
/// A single page extracted from a CBZ/CBR archive.
/// </summary>
public sealed class ImportPage
{
    /// <summary>0-based index of the page within the archive (by sorted filename).</summary>
    public int Index { get; set; }

    /// <summary>Original entry name inside the archive.</summary>
    public string ArchiveFileName { get; set; } = string.Empty;

    /// <summary>Raw image bytes extracted from the archive.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>SHA-256 hex string of <see cref="Data"/> (lower-case, no separators).</summary>
    public string Sha256Hash { get; set; } = string.Empty;

    /// <summary>MIME type inferred from the file extension.</summary>
    public string MimeType { get; set; } = "image/jpeg";
}

/// <summary>
/// Metadata parsed from a ComicInfo.xml found inside the archive.
/// </summary>
public sealed class ComicMetadata
{
    public string Series { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public int VolumeNumber { get; set; } = 1;
}

/// <summary>
/// Full configuration for an import operation submitted by the user.
/// </summary>
public sealed class ComicImportRequest
{
    public string SeriesName { get; set; } = string.Empty;
    public string? Creator { get; set; }
    public int VolumeNumber { get; set; } = 1;
    public List<ImportPage> Pages { get; set; } = [];

    /// <summary>
    /// 0-based page indices that start a new chapter.
    /// Index 0 (page 1) is always a chapter start and must be included.
    /// </summary>
    public List<int> ChapterStartPageIndices { get; set; } = [0];
}

/// <summary>
/// Progress update emitted during an import operation.
/// </summary>
public sealed class ImportProgressUpdate
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of uploading a single file to Hydrus via POST /add_files/add_file.
/// </summary>
public sealed class HydrusAddFileResult
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    /// <summary>Status 1 = import success, 2 = already in DB. Both mean the file is available in Hydrus.</summary>
    public bool IsAvailable => Status == 1 || Status == 2;
}
