namespace HydrusComicCompanion.Data;

public sealed class PageVariantRecord
{
    public int Id { get; set; }

    public int PageId { get; set; }

    public string FileHash { get; set; } = string.Empty;

    public string? MimeType { get; set; }

    public string? OcrText { get; set; }

    public bool IsDefault { get; set; }

    public string? Label { get; set; }

    public PageRecord Page { get; set; } = null!;
}
