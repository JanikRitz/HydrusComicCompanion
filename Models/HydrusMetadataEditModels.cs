using HydrusComicCompanion.Data;

namespace HydrusComicCompanion.Models;

public sealed class HydrusMetadataEditDialogModel
{
    public int ComicId { get; set; }

    public string HydrusTitle { get; set; } = string.Empty;

    public string? CoverFileHash { get; set; }

    public List<ImportPage> Pages { get; set; } = [];

    public HashSet<int> ChapterStartIndices { get; set; } = [0];

    public Dictionary<int, int> VolumeStartIndices { get; set; } = [];

    public Dictionary<int, string> PageThumbnails { get; set; } = [];
}

public sealed class HydrusMetadataEditRequest
{
    public int ComicId { get; set; }

    public string HydrusTitle { get; set; } = string.Empty;

    public string? CoverFileHash { get; set; }

    public List<ImportPage> Pages { get; set; } = [];

    public List<int> ChapterStartPageIndices { get; set; } = [];

    public List<VolumeStartEntry> VolumeStarts { get; set; } = [];
}

public static class HydrusMetadataEditMapper
{
    public static HydrusMetadataEditRequest ToRequest(this HydrusMetadataEditDialogModel model)
    {
        return new HydrusMetadataEditRequest
        {
            ComicId = model.ComicId,
            HydrusTitle = model.HydrusTitle.Trim(),
            CoverFileHash = string.IsNullOrWhiteSpace(model.CoverFileHash) ? null : model.CoverFileHash.Trim(),
            Pages = model.Pages
                .Select((page, index) => new ImportPage
                {
                    Index = page.Index,
                    ArchiveFileName = page.ArchiveFileName,
                    Data = page.Data,
                    Sha256Hash = page.Sha256Hash,
                    MimeType = page.MimeType,
                    GapBefore = page.GapBefore,
                    LogicalPageGroupId = page.LogicalPageGroupId,
                    IsDefaultVariant = page.IsDefaultVariant,
                    VariantLabel = page.VariantLabel,
                    PageNumber = ComputeFinalPageNumber(model, index)
                })
                .ToList(),
            ChapterStartPageIndices = model.ChapterStartIndices
                .Where(i => i >= 0 && i < model.Pages.Count)
                .Distinct()
                .OrderBy(i => i)
                .ToList(),
            VolumeStarts = model.VolumeStartIndices
                .OrderBy(kv => kv.Key)
                .Select(kv => new VolumeStartEntry { PageIndex = kv.Key, VolumeNumber = kv.Value })
                .ToList()
        };
    }

    private static int ComputeFinalPageNumber(HydrusMetadataEditDialogModel model, int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= model.Pages.Count)
        {
            return pageIndex + 1;
        }

        var groupId = model.Pages[pageIndex].LogicalPageGroupId;
        var primaryIndex = FindPrimaryVariantIndex(model.Pages, groupId);

        var chapterStart = model.ChapterStartIndices.Count > 0
            ? model.ChapterStartIndices.Where(s => s <= primaryIndex).DefaultIfEmpty(0).Max()
            : 0;

        var withinChapterPos = 0;
        var gapCount = 0;

        for (var i = chapterStart; i <= primaryIndex; i++)
        {
            if (FindPrimaryVariantIndex(model.Pages, model.Pages[i].LogicalPageGroupId) != i)
            {
                continue;
            }

            withinChapterPos++;
            gapCount += model.Pages[i].GapBefore;
        }

        return withinChapterPos + gapCount;
    }

    private static int FindPrimaryVariantIndex(List<ImportPage> pages, int logicalPageGroupId)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i].LogicalPageGroupId == logicalPageGroupId)
            {
                return i;
            }
        }

        return 0;
    }
}
