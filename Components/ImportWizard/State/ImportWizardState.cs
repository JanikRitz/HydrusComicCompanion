using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services.Abstractions;
using Microsoft.AspNetCore.Components.Forms;

namespace HydrusComicCompanion.Components.ImportWizard.State;

/// <summary>
/// Manages the state of the import wizard workflow across all steps.
/// </summary>
public class ImportWizardState
{
    public enum Step
    {
        SourceSelection,
        Mapping,
        SourceSpecific,
        Metadata,
        Chapters,
        Progress,
        Done
    }

    /// <summary>Status of a comic queued for import.</summary>
    public enum QueueItemStatus
    {
        Pending,
        Imported,
        Failed,
        Skipped
    }

    /// <summary>
    /// A single comic queued for sequential import. Holds a lightweight source descriptor
    /// (archive file reference or Hydrus title) that is extracted lazily when it becomes current.
    /// </summary>
    public sealed class QueuedComic
    {
        public ImportSource Source { get; init; }

        /// <summary>Friendly label shown in the queue list and summary (file name or title).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Archive file reference for <see cref="ImportSource.Archive"/> items.</summary>
        public IBrowserFile? ArchiveFile { get; init; }

        /// <summary>Title text for <see cref="ImportSource.Title"/> and <see cref="ImportSource.HydrusMapped"/> items.</summary>
        public string Title { get; init; } = string.Empty;

        public QueueItemStatus Status { get; set; } = QueueItemStatus.Pending;

        /// <summary>Imported series id once the item completes successfully.</summary>
        public int ImportedSeriesId { get; set; }

        /// <summary>Error message captured when the item fails to extract or import.</summary>
        public string? Error { get; set; }
    }

    // ─── Current Step ───────────────────────────────────────────────────
    public Step CurrentStep { get; set; } = Step.SourceSelection;

    // ─── Source Selection ───────────────────────────────────────────────
    public ImportSource SelectedSource { get; set; } = ImportSource.Archive;

    // ─── Hydrus Mapped Source Mapping (one-off, Feature 2) ──────────────
    public HydrusSourceMapping SourceMapping { get; set; } = new();

    // ─── Import Queue ───────────────────────────────────────────────────
    public List<QueuedComic> Queue { get; set; } = [];
    public int CurrentQueueIndex { get; set; } = -1;

    public QueuedComic? CurrentQueueItem =>
        CurrentQueueIndex >= 0 && CurrentQueueIndex < Queue.Count ? Queue[CurrentQueueIndex] : null;

    public int QueueTotal => Queue.Count;
    public int CurrentQueuePosition => CurrentQueueIndex + 1;
    public bool HasQueuedItems => Queue.Count > 0;
    public bool HasMultipleQueuedItems => Queue.Count > 1;

    // ─── Source-Specific State ──────────────────────────────────────────
    public bool IsExtracting { get; set; }
    public bool IsSyncing { get; set; }
    public bool IsDiscovering { get; set; }
    public string TitleInput { get; set; } = string.Empty;

    // ─── Extracted Data ─────────────────────────────────────────────────
    public List<ImportPage> Pages { get; set; } = [];
    public HashSet<int> ChapterStartIndices { get; set; } = [0];
    public Dictionary<int, int> VolumeStartIndices { get; set; } = [];
    public Dictionary<int, string> PageThumbnailDataUrls { get; set; } = [];
    public bool ThumbnailPreloadQueued { get; set; }
    public bool IsPreloadingThumbnails { get; set; }
    public bool UseChapterTags { get; set; } = true;

    public bool HasIncludedPages => Pages.Any(p => !p.IsExcluded);
    public int IncludedPageCount => Pages.Count(p => !p.IsExcluded);

    private int _nextLogicalPageGroupId = 1;

    // ─── Metadata Fields ────────────────────────────────────────────────
    public string TitleName { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
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

    public void GoToMapping()
    {
        CurrentStep = Step.Mapping;
        ErrorMessage = string.Empty;
    }

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
            VolumeStartIndices.Clear();
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

    // ─── Queue Management ───────────────────────────────────────────────

    /// <summary>Sets the selected source, clearing the queue when the source changes.</summary>
    public void SetSelectedSource(ImportSource source)
    {
        if (SelectedSource != source)
        {
            Queue.Clear();
            CurrentQueueIndex = -1;
        }

        SelectedSource = source;
    }

    public void AddArchiveToQueue(IBrowserFile file)
    {
        Queue.Add(new QueuedComic
        {
            Source = ImportSource.Archive,
            ArchiveFile = file,
            DisplayName = file.Name
        });
    }

    /// <summary>
    /// Replaces all queued archives with a fresh selection. Required because Blazor Server
    /// invalidates earlier <see cref="IBrowserFile"/> references whenever the file input changes.
    /// </summary>
    public void ReplaceArchiveQueue(IReadOnlyList<IBrowserFile> files)
    {
        Queue.Clear();
        CurrentQueueIndex = -1;

        foreach (var file in files)
        {
            AddArchiveToQueue(file);
        }
    }

    public void AddTitleToQueue(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (Queue.Any(item => item.Source == SelectedSource
            && string.Equals(item.Title, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Queue.Add(new QueuedComic
        {
            Source = SelectedSource,
            Title = trimmed,
            DisplayName = trimmed
        });
    }

    /// <summary>
    /// Replaces the queue with titles discovered through the Hydrus Mapped source so each one can be
    /// taken through metadata and page ordering sequentially. Duplicates and blanks are ignored.
    /// </summary>
    public void ReplaceQueueWithMappedTitles(IEnumerable<TitleWithPageCount> titles)
    {
        Queue.Clear();
        CurrentQueueIndex = -1;

        foreach (var titleWithPageCount in titles)
        {
            var title = titleWithPageCount?.Title?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (Queue.Any(item => string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Queue.Add(new QueuedComic
            {
                Source = ImportSource.HydrusMapped,
                Title = title,
                DisplayName = title
            });
        }
    }

    public void RemoveQueueItem(QueuedComic item)
    {
        Queue.Remove(item);
    }

    /// <summary>Finds the index of the next item still pending import at or after <paramref name="startInclusive"/>, or -1.</summary>
    public int FindNextPendingIndex(int startInclusive = 0)
    {
        for (var i = Math.Max(0, startInclusive); i < Queue.Count; i++)
        {
            if (Queue[i].Status == QueueItemStatus.Pending)
            {
                return i;
            }
        }

        return -1;
    }

    public void MarkCurrentImported(int seriesId)
    {
        if (CurrentQueueItem is { } item)
        {
            item.Status = QueueItemStatus.Imported;
            item.ImportedSeriesId = seriesId;
            item.Error = null;
        }

        ImportedSeriesId = seriesId;
    }

    public void MarkCurrentFailed(string error)
    {
        if (CurrentQueueItem is { } item)
        {
            item.Status = QueueItemStatus.Failed;
            item.Error = error;
        }
    }

    public void MarkCurrentSkipped()
    {
        if (CurrentQueueItem is { } item)
        {
            item.Status = QueueItemStatus.Skipped;
            item.Error = null;
        }
    }

    // ─── Data Application ───────────────────────────────────────────────

    public void ApplyPreparation(ComicImportPreparation preparation)
    {
        Pages = preparation.Pages;
        ReindexPages();
        InitializeLogicalPageGroups();

        TitleName = preparation.Metadata?.Series?.Trim() ?? string.Empty;
        DisplayTitle = TitleName;
        Comments = string.Empty;
        CustomTags = string.Empty;
        Creator = preparation.Metadata?.Creator?.Trim() ?? string.Empty;
        VolumeNumber = preparation.Metadata?.VolumeNumber;
        ChapterStartIndices = preparation.ChapterStartPageIndices.Count > 0
            ? [.. preparation.ChapterStartPageIndices.Where(i => i >= 0 && i < Pages.Count).Distinct().OrderBy(i => i)]
            : [0];
        UseChapterTags = ChapterStartIndices.Count > 0;

        // Infer gap markers from any existing page numbers (e.g. Hydrus re-import with missing pages),
        // then clear PageNumber — it is always recomputed from position + gaps at request-build time.
        InferGapsFromPageNumbers();
        foreach (var p in Pages)
        {
            p.PageNumber = null;
            p.IsExcluded = false;
        }

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
        RemapVolumeStartsAfterMove(fromIndex, toIndex);
        ReindexPages();

        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = true;
    }

    public bool IsPrimaryVariant(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return false;
        }

        var groupId = Pages[pageIndex].LogicalPageGroupId;
        var firstGroupIndex = FindPrimaryVariantIndex(groupId);
        return firstGroupIndex == pageIndex;
    }

    public bool HasAlternateVariants(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return false;
        }

        var groupId = Pages[pageIndex].LogicalPageGroupId;
        return Pages.Count(p => p.LogicalPageGroupId == groupId) > 1;
    }

    public void GroupWithPreviousPage(int pageIndex)
    {
        if (pageIndex <= 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        var targetGroupId = Pages[pageIndex - 1].LogicalPageGroupId;
        Pages[pageIndex].LogicalPageGroupId = targetGroupId;

        if (!Pages.Any(p => p.LogicalPageGroupId == targetGroupId && p.IsDefaultVariant))
        {
            Pages[pageIndex - 1].IsDefaultVariant = true;
        }

        EnsureSingleDefaultVariant(targetGroupId, FindPrimaryVariantIndex(targetGroupId));
    }

    public void UngroupPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        var currentGroupId = Pages[pageIndex].LogicalPageGroupId;
        var hasSibling = false;
        for (var i = 0; i < Pages.Count; i++)
        {
            if (i == pageIndex)
            {
                continue;
            }

            if (Pages[i].LogicalPageGroupId == currentGroupId)
            {
                hasSibling = true;
                break;
            }
        }

        if (!hasSibling)
        {
            return;
        }

        Pages[pageIndex].LogicalPageGroupId = _nextLogicalPageGroupId++;
        Pages[pageIndex].IsDefaultVariant = true;

        EnsureSingleDefaultVariant(currentGroupId, FindPrimaryVariantIndex(currentGroupId));
    }

    public void SetDefaultVariant(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        var groupId = Pages[pageIndex].LogicalPageGroupId;
        EnsureSingleDefaultVariant(groupId, pageIndex);
    }

    public void SetPageExcluded(int pageIndex, bool isExcluded)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        if (Pages[pageIndex].IsExcluded == isExcluded)
        {
            return;
        }

        Pages[pageIndex].IsExcluded = isExcluded;
        EnsureIncludedDefaultVariant(Pages[pageIndex].LogicalPageGroupId);
    }

    // ─── Page Gap Management ─────────────────────────────────────────────

    /// <summary>Returns true when the page at <paramref name="pageIndex"/> has at least one gap marker.</summary>
    public bool IsPageGap(int pageIndex) => pageIndex > 0 && !ChapterStartIndices.Contains(pageIndex) && Pages[pageIndex].GapBefore > 0;

    /// <summary>Increments the gap counter for the page at <paramref name="pageIndex"/> by one missing page.</summary>
    public void AddPageGap(int pageIndex)
    {
        if (pageIndex <= 0 || pageIndex >= Pages.Count || ChapterStartIndices.Contains(pageIndex))
        {
            return;
        }

        Pages[pageIndex].GapBefore++;
    }

    /// <summary>Decrements the gap counter for the page at <paramref name="pageIndex"/> by one (minimum 0).</summary>
    public void RemovePageGap(int pageIndex)
    {
        if (pageIndex <= 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        Pages[pageIndex].GapBefore = Math.Max(0, Pages[pageIndex].GapBefore - 1);
    }

    /// <summary>
    /// Computes the final 1-based page number that will be written to Hydrus for the given page.
    /// Equals the within-chapter position plus the cumulative gap count up to that position.
    /// </summary>
    public int ComputeFinalPageNumber(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return pageIndex + 1;
        }

        if (Pages[pageIndex].IsExcluded)
        {
            return 0;
        }

        var groupId = Pages[pageIndex].LogicalPageGroupId;
        var primaryIndex = FindPrimaryIncludedVariantIndex(groupId);
        return ComputeFinalPageNumberCore(primaryIndex);
    }

    private int ComputeFinalPageNumberCore(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return 0;
        }

        var chapterStart = 0;
        if (UseChapterTags && ChapterStartIndices.Count > 0)
        {
            chapterStart = ChapterStartIndices
                .Where(s => s <= pageIndex)
                .DefaultIfEmpty(0)
                .Max();
        }

        var withinChapterPos = 0;
        var gapCount = 0;
        for (var i = chapterStart; i <= pageIndex; i++)
        {
            if (!IsPrimaryVariant(i) || Pages[i].IsExcluded)
            {
                continue;
            }

            withinChapterPos++;
            gapCount += Pages[i].GapBefore;
        }

        return withinChapterPos + gapCount;
    }

    /// <summary>
    /// Infers <see cref="ImportPage.GapBefore"/> values from existing <see cref="ImportPage.PageNumber"/> values.
    /// Called inside <see cref="ApplyPreparation"/> so that Hydrus re-imports with non-sequential tags
    /// (e.g. 1, 2, 3, 5, 6) are automatically converted to the equivalent gap representation.
    /// </summary>
    private void InferGapsFromPageNumbers()
    {
        var chapterStarts = (UseChapterTags && ChapterStartIndices.Count > 0)
            ? ChapterStartIndices.OrderBy(i => i).ToList()
            : (List<int>)[0];

        for (var ci = 0; ci < chapterStarts.Count; ci++)
        {
            var start = chapterStarts[ci];
            var end = ci + 1 < chapterStarts.Count ? chapterStarts[ci + 1] : Pages.Count;

            var expected = 1;
            for (var i = start; i < end; i++)
            {
                var actual = Pages[i].PageNumber is > 0 ? Pages[i].PageNumber!.Value : (i - start + 1);
                Pages[i].GapBefore = Math.Max(0, actual - expected);
                expected = actual + 1;
            }
        }
    }

    // ─── Volume Management ──────────────────────────────────────────────

    /// <summary>Returns true when the page at <paramref name="pageIndex"/> is a volume start marker.</summary>
    public bool IsVolumeStart(int pageIndex) => VolumeStartIndices.ContainsKey(pageIndex);

    /// <summary>
    /// Marks <paramref name="pageIndex"/> as the start of a new volume with <paramref name="volumeNumber"/>.
    /// Also adds it as a chapter start.
    /// </summary>
    public void AddVolumeStart(int pageIndex, int volumeNumber)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        VolumeStartIndices[pageIndex] = volumeNumber;
        ChapterStartIndices.Add(pageIndex);
    }

    /// <summary>
    /// Removes the volume start marker from <paramref name="pageIndex"/>.
    /// For page indices &gt; 0 also removes the chapter start (one marker does both).
    /// Page 0 always remains a chapter start.
    /// </summary>
    public void RemoveVolumeStart(int pageIndex)
    {
        VolumeStartIndices.Remove(pageIndex);
        if (pageIndex != 0)
        {
            ChapterStartIndices.Remove(pageIndex);
        }
    }

    /// <summary>Updates the volume number stored for an existing volume start at <paramref name="pageIndex"/>.</summary>
    public void UpdateVolumeNumber(int pageIndex, int volumeNumber)
    {
        if (VolumeStartIndices.ContainsKey(pageIndex))
        {
            VolumeStartIndices[pageIndex] = volumeNumber;
        }
    }

    private void RemapVolumeStartsAfterMove(int fromIndex, int toIndex)
    {
        var remapped = new Dictionary<int, int>();

        foreach (var (key, value) in VolumeStartIndices)
        {
            int newKey;
            if (key == fromIndex)
            {
                newKey = toIndex;
            }
            else if (fromIndex < toIndex && key > fromIndex && key <= toIndex)
            {
                newKey = key - 1;
            }
            else if (toIndex < fromIndex && key >= toIndex && key < fromIndex)
            {
                newKey = key + 1;
            }
            else
            {
                newKey = key;
            }

            remapped[newKey] = value;
        }

        VolumeStartIndices = remapped;
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

    private void InitializeLogicalPageGroups()
    {
        _nextLogicalPageGroupId = 1;

        foreach (var page in Pages)
        {
            if (page.LogicalPageGroupId <= 0)
            {
                page.LogicalPageGroupId = _nextLogicalPageGroupId++;
                page.IsDefaultVariant = true;
                continue;
            }

            _nextLogicalPageGroupId = Math.Max(_nextLogicalPageGroupId, page.LogicalPageGroupId + 1);
        }

        foreach (var groupId in Pages.Select(p => p.LogicalPageGroupId).Distinct())
        {
            EnsureSingleDefaultVariant(groupId, FindPrimaryVariantIndex(groupId));
        }
    }

    private int FindPrimaryVariantIndex(int groupId)
    {
        for (var i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].LogicalPageGroupId == groupId)
            {
                return i;
            }
        }

        return 0;
    }

    private int FindPrimaryIncludedVariantIndex(int groupId)
    {
        for (var i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].LogicalPageGroupId == groupId && !Pages[i].IsExcluded)
            {
                return i;
            }
        }

        return -1;
    }

    private void EnsureIncludedDefaultVariant(int groupId)
    {
        var includedIndexes = Pages
            .Select((page, index) => new { page, index })
            .Where(x => x.page.LogicalPageGroupId == groupId && !x.page.IsExcluded)
            .Select(x => x.index)
            .ToList();

        if (includedIndexes.Count == 0)
        {
            return;
        }

        var selectedDefaultIndex = includedIndexes
            .Where(i => Pages[i].IsDefaultVariant)
            .Select(i => (int?)i)
            .FirstOrDefault() ?? includedIndexes[0];

        foreach (var i in includedIndexes)
        {
            Pages[i].IsDefaultVariant = i == selectedDefaultIndex;
        }
    }

    private void EnsureSingleDefaultVariant(int groupId, int defaultIndex)
    {
        var hasGroup = false;
        for (var i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].LogicalPageGroupId != groupId)
            {
                continue;
            }

            hasGroup = true;
            Pages[i].IsDefaultVariant = i == defaultIndex;
        }

        if (!hasGroup)
        {
            return;
        }

        if (!Pages.Any(p => p.LogicalPageGroupId == groupId && p.IsDefaultVariant))
        {
            var primaryIndex = FindPrimaryVariantIndex(groupId);
            if (primaryIndex >= 0 && primaryIndex < Pages.Count)
            {
                Pages[primaryIndex].IsDefaultVariant = true;
            }
        }
    }

    // ─── Reset Methods ──────────────────────────────────────────────────

    public void ResetToSourceSelection()
    {
        CurrentStep = Step.SourceSelection;
        Pages = [];
        UseChapterTags = true;
        ChapterStartIndices = [0];
        VolumeStartIndices = [];
        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = false;
        IsPreloadingThumbnails = false;
        TitleName = string.Empty;
        DisplayTitle = string.Empty;
        Comments = string.Empty;
        Creator = string.Empty;
        CustomTags = string.Empty;
        VolumeNumber = null;
        TitleInput = string.Empty;
        ErrorMessage = string.Empty;
        ProgressCurrent = 0;
        ProgressTotal = 0;
        ProgressMessage = string.Empty;
        ImportedSeriesId = 0;
        Queue = [];
        CurrentQueueIndex = -1;
        SourceMapping = new();
        _nextLogicalPageGroupId = 1;
    }

    public void ResetToSourceSpecific()
    {
        CurrentStep = Step.SourceSpecific;
        Pages = [];
        UseChapterTags = true;
        ChapterStartIndices = [0];
        VolumeStartIndices = [];
        PageThumbnailDataUrls = [];
        ThumbnailPreloadQueued = false;
        IsPreloadingThumbnails = false;
        TitleName = string.Empty;
        DisplayTitle = string.Empty;
        Comments = string.Empty;
        Creator = string.Empty;
        CustomTags = string.Empty;
        VolumeNumber = null;
        ErrorMessage = string.Empty;
        ProgressCurrent = 0;
        ProgressTotal = 0;
        ProgressMessage = string.Empty;
        _nextLogicalPageGroupId = 1;
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
        var includedPages = Pages
            .Select((p, index) => new { Page = p, OriginalIndex = index })
            .Where(x => !x.Page.IsExcluded)
            .ToList();

        if (includedPages.Count == 0)
        {
            throw new InvalidOperationException("At least one page must be included before importing.");
        }

        var chapterStarts = UseChapterTags
            ? ChapterStartIndices.Where(i => i >= 0 && i < Pages.Count).Distinct().OrderBy(i => i).ToList()
            : [];

        var remappedChapterStarts = new Dictionary<int, int>();
        var remappedVolumeStarts = new Dictionary<int, (int NewIndex, int VolumeNumber)>();
        var orderedVolumeStarts = VolumeStartIndices.OrderBy(kv => kv.Key).ToList();

        var pagesWithNumbers = includedPages
            .Select((entry, newIndex) =>
            {
                var originalIndex = entry.OriginalIndex;
                var chapterStart = chapterStarts.Count > 0
                    ? chapterStarts.Where(s => s <= originalIndex).DefaultIfEmpty(0).Max()
                    : 0;

                if (!remappedChapterStarts.ContainsKey(chapterStart))
                {
                    remappedChapterStarts[chapterStart] = newIndex;
                }

                if (orderedVolumeStarts.Count > 0)
                {
                    var activeVolumeStart = orderedVolumeStarts
                        .Where(kv => kv.Key <= originalIndex)
                        .Select(kv => (Key: (int?)kv.Key, Value: (int?)kv.Value))
                        .LastOrDefault();

                    if (activeVolumeStart.Key.HasValue && activeVolumeStart.Value.HasValue
                        && !remappedVolumeStarts.ContainsKey(activeVolumeStart.Key.Value))
                    {
                        remappedVolumeStarts[activeVolumeStart.Key.Value] = (newIndex, activeVolumeStart.Value.Value);
                    }
                }

                return new ImportPage
                {
                    Index = newIndex,
                    ArchiveFileName = entry.Page.ArchiveFileName,
                    Data = entry.Page.Data,
                    Sha256Hash = entry.Page.Sha256Hash,
                    MimeType = entry.Page.MimeType,
                    GapBefore = entry.Page.GapBefore,
                    LogicalPageGroupId = entry.Page.LogicalPageGroupId,
                    IsDefaultVariant = entry.Page.IsDefaultVariant,
                    VariantLabel = entry.Page.VariantLabel,
                    IsExcluded = false,
                    PageNumber = ComputeFinalPageNumber(originalIndex)
                };
            })
            .ToList();

        foreach (var group in pagesWithNumbers.GroupBy(p => p.LogicalPageGroupId))
        {
            var defaultVariant = group.FirstOrDefault(p => p.IsDefaultVariant) ?? group.First();
            foreach (var page in group)
            {
                page.IsDefaultVariant = ReferenceEquals(page, defaultVariant);
            }
        }

        return new ComicImportRequest
        {
            SeriesName = TitleName.Trim(),
            DisplayTitle = string.IsNullOrWhiteSpace(DisplayTitle) ? null : DisplayTitle.Trim(),
            Comment = string.IsNullOrWhiteSpace(Comments) ? null : Comments.Trim(),
            Creator = string.IsNullOrWhiteSpace(Creator) ? null : Creator.Trim(),
            VolumeNumber = VolumeNumber,
            Pages = pagesWithNumbers,
            CustomTags = string.IsNullOrWhiteSpace(CustomTags)
                ? []
                : [.. CustomTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
            ChapterStartPageIndices = UseChapterTags
                ? [.. remappedChapterStarts.Values.OrderBy(i => i)]
                : [],
            VolumeStarts = remappedVolumeStarts.Count > 0
                ? [.. remappedVolumeStarts.Values
                    .OrderBy(v => v.NewIndex)
                    .Select(v => new VolumeStartEntry { PageIndex = v.NewIndex, VolumeNumber = v.VolumeNumber })]
                : [],
            ExcludedPageHashes = [.. Pages
                .Where(p => p.IsExcluded && !string.IsNullOrWhiteSpace(p.Sha256Hash))
                .Select(p => p.Sha256Hash.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)]
        };
    }
}
