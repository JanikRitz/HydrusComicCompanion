using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services.Abstractions;

namespace HydrusComicCompanion.Components.ImportWizard.State;

/// <summary>
/// Manages the state of the import wizard workflow across all steps.
/// </summary>
public class ImportWizardState
{
    public enum Step
    {
        SourceSelection,
        SourceSpecific,
        Metadata,
        Chapters,
        Progress,
        Done
    }

    // ─── Current Step ───────────────────────────────────────────────────
    public Step CurrentStep { get; set; } = Step.SourceSelection;

    // ─── Source Selection ───────────────────────────────────────────────
    public ImportSource SelectedSource { get; set; } = ImportSource.Archive;

    // ─── Source-Specific State ──────────────────────────────────────────
    public bool IsExtracting { get; set; }
    public bool IsSyncing { get; set; }
    public string TitleInput { get; set; } = string.Empty;

    // ─── Extracted Data ─────────────────────────────────────────────────
    public List<ImportPage> Pages { get; set; } = [];
    public HashSet<int> ChapterStartIndices { get; set; } = [0];
    public Dictionary<int, string> PageThumbnailDataUrls { get; set; } = [];
    public bool ThumbnailPreloadQueued { get; set; }
    public bool IsPreloadingThumbnails { get; set; }
    public bool UseChapterTags { get; set; } = true;

    // ─── Metadata Fields ────────────────────────────────────────────────
    public string TitleName { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string CustomTags { get; set; } = string.Empty;
    public int? VolumeNumber { get; set; }

    // ─── Progress and Errors ────────────────────────────────────────────
    public int ProgressCurrent { get; set; }
    public int ProgressTotal { get; set; }
    public string ProgressMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    // ─── Result ─────────────────────────────────────────────────────────
    public int ImportedSeriesId { get; set; }

    // ─── Navigation Methods ─────────────────────────────────────────────

    public void GoToSourceSpecific()
    {
        CurrentStep = Step.SourceSpecific;
        ErrorMessage = string.Empty;
    }

    public void GoToMetadata()
    {
        CurrentStep = Step.Metadata;
    }

    public void GoToChapters()
    {
        if (UseChapterTags && !ChapterStartIndices.Contains(0))
        {
            ChapterStartIndices.Add(0);
        }
        else if (!UseChapterTags)
        {
            ChapterStartIndices.Clear();
        }

        CurrentStep = Step.Chapters;
        ThumbnailPreloadQueued = true;
    }

    public void GoToProgress()
    {
        CurrentStep = Step.Progress;
        ErrorMessage = string.Empty;
        ProgressCurrent = 0;
        ProgressTotal = Pages.Count;
        ProgressMessage = string.Empty;
    }

    public void GoToDone()
    {
        CurrentStep = Step.Done;
    }

    // ─── Data Application ───────────────────────────────────────────────

    public void ApplyPreparation(ComicImportPreparation preparation)
    {
        Pages = preparation.Pages;
        ReindexPages();

        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].PageNumber = Pages[i].PageNumber is > 0 ? Pages[i].PageNumber : i + 1;
        }

        TitleName = preparation.Metadata?.Series?.Trim() ?? string.Empty;
        Creator = preparation.Metadata?.Creator?.Trim() ?? string.Empty;
        VolumeNumber = preparation.Metadata?.VolumeNumber;
        ChapterStartIndices = preparation.ChapterStartPageIndices.Count > 0
            ? [.. preparation.ChapterStartPageIndices.Where(i => i >= 0 && i < Pages.Count).Distinct().OrderBy(i => i)]
            : [0];
        UseChapterTags = ChapterStartIndices.Count > 0;
        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = false;
        IsPreloadingThumbnails = false;
    }

    // ─── Chapter Management ─────────────────────────────────────────────

    public void AddChapterStart(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        ChapterStartIndices.Add(pageIndex);
    }

    public void RemoveChapterStart(int pageIndex)
    {
        if (pageIndex != 0)
        {
            ChapterStartIndices.Remove(pageIndex);
        }
    }

    public void MovePage(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= Pages.Count || toIndex >= Pages.Count || fromIndex == toIndex)
        {
            return;
        }

        var movedPage = Pages[fromIndex];
        Pages.RemoveAt(fromIndex);
        Pages.Insert(toIndex, movedPage);

        RemapChapterStartsAfterMove(fromIndex, toIndex);
        ReindexPages();

        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = true;
    }

    public void SetPageNumber(int pageIndex, int? pageNumber)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        Pages[pageIndex].PageNumber = pageNumber is > 0 ? pageNumber : null;
    }

    private void RemapChapterStartsAfterMove(int fromIndex, int toIndex)
    {
        var remapped = new HashSet<int>();

        foreach (var chapterStart in ChapterStartIndices)
        {
            if (chapterStart == fromIndex)
            {
                remapped.Add(toIndex);
            }
            else if (fromIndex < toIndex && chapterStart > fromIndex && chapterStart <= toIndex)
            {
                remapped.Add(chapterStart - 1);
            }
            else if (toIndex < fromIndex && chapterStart >= toIndex && chapterStart < fromIndex)
            {
                remapped.Add(chapterStart + 1);
            }
            else
            {
                remapped.Add(chapterStart);
            }
        }

        ChapterStartIndices = remapped;

        if (UseChapterTags)
        {
            ChapterStartIndices.Add(0);
        }
    }

    private void ReindexPages()
    {
        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].Index = i;
        }
    }

    // ─── Reset Methods ──────────────────────────────────────────────────

    public void ResetToSourceSelection()
    {
        CurrentStep = Step.SourceSelection;
        Pages = [];
        UseChapterTags = true;
        ChapterStartIndices = [0];
        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = false;
        IsPreloadingThumbnails = false;
        TitleName = string.Empty;
        Creator = string.Empty;
        CustomTags = string.Empty;
        VolumeNumber = null;
        TitleInput = string.Empty;
        ErrorMessage = string.Empty;
        ProgressCurrent = 0;
        ProgressTotal = 0;
        ProgressMessage = string.Empty;
        ImportedSeriesId = 0;
    }

    public void ResetToSourceSpecific()
    {
        CurrentStep = Step.SourceSpecific;
        Pages = [];
        UseChapterTags = true;
        ChapterStartIndices = [0];
        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = false;
        IsPreloadingThumbnails = false;
        TitleName = string.Empty;
        Creator = string.Empty;
        CustomTags = string.Empty;
        VolumeNumber = null;
        ErrorMessage = string.Empty;
        ProgressCurrent = 0;
        ProgressTotal = 0;
        ProgressMessage = string.Empty;
    }

    // ─── Progress Update ────────────────────────────────────────────────

    public void UpdateProgress(ImportProgressUpdate update)
    {
        ProgressCurrent = update.Current;
        ProgressTotal = update.Total;
        ProgressMessage = update.Message;
    }

    // ─── Import Request Builder ────────────────────────────────────────

    public ComicImportRequest BuildImportRequest()
    {
        return new ComicImportRequest
        {
            SeriesName = TitleName.Trim(),
            Creator = string.IsNullOrWhiteSpace(Creator) ? null : Creator.Trim(),
            VolumeNumber = VolumeNumber,
            Pages = Pages,
            CustomTags = string.IsNullOrWhiteSpace(CustomTags)
                ? []
                : [.. CustomTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
            ChapterStartPageIndices = UseChapterTags
                ? [.. ChapterStartIndices.Where(i => i >= 0 && i < Pages.Count).OrderBy(i => i)]
                : []
        };
    }
}
