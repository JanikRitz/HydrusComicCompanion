namespace HydrusComicCompanion.Data;

public sealed class PageRecord
{
    public int Id { get; set; }

    public int ChapterId { get; set; }

    public int PageNumber { get; set; }

    public ChapterRecord Chapter { get; set; } = null!;

    public ICollection<PageVariantRecord> Variants { get; set; } = [];
}
