using HydrusComicCompanion.Data;

namespace HydrusComicCompanion.Models;

public sealed record ImagesetImage(int? Index, PageVariantRecord File)
{
    public string IndexLabel => Index.HasValue ? $"Index {Index}" : "Unnumbered";

    public IEnumerable<string> Labels => (File.Label ?? string.Empty)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static List<ImagesetImage> FromCollection(ComicsRecord collection) => collection.Chapters
        .SelectMany(chapter => chapter.Pages)
        .SelectMany(page => page.Variants.Select(file => new ImagesetImage(file.ImageIndex, file)))
        .OrderBy(image => image.Index.HasValue ? 0 : 1)
        .ThenBy(image => image.Index)
        .ThenBy(image => image.File.FileHash, StringComparer.Ordinal)
        .ToList();
}
