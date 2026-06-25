namespace HydrusComicCompanion.Data;

public sealed class ComicsRecord
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? CoverFileHash { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public ICollection<ChapterRecord> Chapters { get; set; } = [];

    public ICollection<MetadataRecord> Metadata { get; set; } = [];
}
