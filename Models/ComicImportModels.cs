using System.Text.Json.Serialization;

namespace HydrusComicCompanion.Models;

/// <summary>
/// A single page extracted from a CBZ/CBR archive.
/// </summary>
public sealed class ImportPage
{
    /// <summary>0-based index of the page within the extracted source order.</summary>
    public int Index { get; set; }

    /// <summary>Display name for the page source entry.</summary>
    public string ArchiveFileName { get; set; } = string.Empty;

    /// <summary>Raw image bytes extracted from the source, when available.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>SHA-256 hex string for the page. Used for archive extraction and Hydrus-sourced pages.</summary>
    public string Sha256Hash { get; set; } = string.Empty;

    /// <summary>MIME type inferred from the file extension or source metadata.</summary>
    public string MimeType { get; set; } = "image/jpeg";

    /// <summary>
    /// Computed 1-based page number written to Hydrus during import.
    /// Set by <c>ImportWizardState.BuildImportRequest</c>; do not edit directly in the wizard UI.
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Number of source pages that are missing immediately before this page.
    /// Each unit adds +1 to this page's final number and all subsequent pages in the same chapter.
    /// </summary>
    public int GapBefore { get; set; }

    /// <summary>
    /// Logical page group id. Multiple entries with the same id represent alternate variants of one logical page.
    /// </summary>
    public int LogicalPageGroupId { get; set; }

    /// <summary>
    /// Marks the default variant for a logical page group.
    /// </summary>
    public bool IsDefaultVariant { get; set; } = true;

    /// <summary>
    /// Optional user-facing label for this variant.
    /// </summary>
    public string? VariantLabel { get; set; }

    /// <summary>
    /// Marks this page row as excluded from the next import request.
    /// Excluded rows remain in the editor UI but are filtered out when importing.
    /// </summary>
    public bool IsExcluded { get; set; }
}

/// <summary>
/// Shared extraction result used by the import workflow before metadata and chapter edits.
/// </summary>
public sealed class ComicImportPreparation
{
    public List<ImportPage> Pages { get; set; } = [];
    public ComicMetadata? Metadata { get; set; }
    public List<int> ChapterStartPageIndices { get; set; } = [];
}

/// <summary>
/// Metadata parsed from a ComicInfo.xml found inside the archive.
/// </summary>
public sealed class ComicMetadata
{
    public string Series { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public int? VolumeNumber { get; set; }
}

/// <summary>
/// One-off override of the Hydrus tag service and structural namespaces used when extracting
/// existing titles through the "Hydrus Mapped" import source. Blank fields fall back to the
/// configured global settings so extraction parsing never breaks.
/// </summary>
public sealed class HydrusSourceMapping
{
    /// <summary>Display name of the tag service to read from (e.g. "my tags").</summary>
    public string TagServiceName { get; set; } = string.Empty;

    /// <summary>Service key of the tag service to read from.</summary>
    public string TagServiceKey { get; set; } = string.Empty;

    /// <summary>Namespace prefix for the title tag (e.g. "title:").</summary>
    public string TitleNamespace { get; set; } = string.Empty;

    /// <summary>Namespace prefix for the volume tag (e.g. "volume:").</summary>
    public string VolumeNamespace { get; set; } = string.Empty;

    /// <summary>Namespace prefix for the chapter tag (e.g. "chapter:").</summary>
    public string ChapterNamespace { get; set; } = string.Empty;

    /// <summary>Namespace prefix for the page tag (e.g. "page:").</summary>
    public string PageNamespace { get; set; } = string.Empty;

    /// <summary>Namespace prefix for alternate page tags (e.g. "variant:").</summary>
    public string AlternatePageNamespace { get; set; } = string.Empty;

    /// <summary>Value in the alternate namespace that marks the default variant (e.g. "default").</summary>
    public string AlternatePageDefaultValue { get; set; } = string.Empty;

    /// <summary>Full tag that marks the cover page (e.g. "meta:cover page").</summary>
    public string CoverPageTag { get; set; } = string.Empty;

    /// <summary>Minimum number of pages a title must have to be included in discovery results. Null or 0 means no minimum.</summary>
    public int? MinimumPages { get; set; }
}

/// <summary>
/// Represents a discovered title with its page count for filtering purposes during discovery.
/// </summary>
public sealed class TitleWithPageCount
{
    /// <summary>The title name extracted from the namespace tag.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Number of distinct files/pages with a page tag for this title.</summary>
    public int PageCount { get; set; }
}

/// <summary>
/// Marks the page index where a new volume begins, carrying the user-specified volume number.
/// A volume start is always implicitly also a chapter start.
/// </summary>
public sealed class VolumeStartEntry
{
    /// <summary>0-based page index where the volume begins.</summary>
    public int PageIndex { get; set; }

    /// <summary>User-specified volume number for this volume.</summary>
    public int VolumeNumber { get; set; }
}

/// <summary>
/// Full configuration for an import operation submitted by the user.
/// </summary>
public sealed class ComicImportRequest
{
    public string SeriesName { get; set; } = string.Empty;
    public string? DisplayTitle { get; set; }
    public string? Comment { get; set; }
    public string? Creator { get; set; }

    /// <summary>
    /// Volume number for single-volume imports.
    /// Ignored when <see cref="VolumeStarts"/> is non-empty.
    /// </summary>
    public int? VolumeNumber { get; set; }
    public List<ImportPage> Pages { get; set; } = [];
    public List<string> CustomTags { get; set; } = [];

    /// <summary>
    /// 0-based page indices that start a new chapter.
    /// Leave empty for chapterless imports.
    /// </summary>
    public List<int> ChapterStartPageIndices { get; set; } = [];

    /// <summary>
    /// Per-volume start entries for multi-volume imports.
    /// When non-empty, overrides <see cref="VolumeNumber"/> and resets chapter numbering at each entry.
    /// </summary>
    public List<VolumeStartEntry> VolumeStarts { get; set; } = [];

    /// <summary>
    /// Hydrus file hashes for rows the user excluded during chapter placement.
    /// Used to clean up stale structural tags after import.
    /// </summary>
    public List<string> ExcludedPageHashes { get; set; } = [];
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
/// A book entry discovered in a Calibre library via calibredb.
/// </summary>
public sealed class CalibreBookEntry
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Authors { get; init; } = string.Empty;
    public IReadOnlyList<string> Formats { get; init; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Authors)
        ? $"#{Id:D4} — {Title}"
        : $"#{Id:D4} — {Title} ({Authors})";
}

/// <summary>
/// Metadata for a Calibre book parsed from its OPF metadata via calibredb show_metadata.
/// </summary>
public sealed class CalibreMetadataSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public int? VolumeNumber { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
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
