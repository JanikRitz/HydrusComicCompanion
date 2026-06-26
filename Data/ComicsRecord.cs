namespace HydrusComicCompanion.Data;

public sealed class ComicsRecord
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? DisplayTitle { get; set; }

    public string? Comment { get; set; }

    public string? CoverFileHash { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public ICollection<ChapterRecord> Chapters { get; set; } = [];

    public ICollection<MetadataRecord> Metadata { get; set; } = [];
}
