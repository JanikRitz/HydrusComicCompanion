namespace HydrusComicCompanion.Data;

public sealed class PageRecord
{
    public int Id { get; set; }

    public int ChapterId { get; set; }

    public string FileHash { get; set; } = string.Empty;

    public int PageNumber { get; set; }

    public string? MimeType { get; set; }

    public string? OcrText { get; set; }

    public ChapterRecord Chapter { get; set; } = null!;
}
