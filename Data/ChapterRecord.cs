namespace HydrusComicCompanion.Data;

public sealed class ChapterRecord
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public int VolumeNumber { get; set; } = 1;

    public decimal ChapterNumber { get; set; }

    public string? Title { get; set; }

    public SeriesRecord Series { get; set; } = null!;

    public ICollection<PageRecord> Pages { get; set; } = [];
}
