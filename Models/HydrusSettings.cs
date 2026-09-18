namespace HydrusComicCompanion.Models;

public sealed class HydrusSettings
{
    public string ApiUrl { get; set; } = "http://127.0.0.1:45869";

    public string ApiAccessKey { get; set; } = string.Empty;

    public string PrimaryTagService { get; set; } = "my tags";

    public string TagServiceKey { get; set; } = string.Empty;

    public string TargetFileDomain { get; set; } = "all local files";

    public string TitleNamespace { get; set; } = "comic:";

    public string SetNamespace { get; set; } = "set:";

    public string IndexNamespace { get; set; } = "index:";

    public string SeriesNamespace { get; set; } = string.Empty;

    public string VolumeNamespace { get; set; } = "volume:";

    public string ChapterNamespace { get; set; } = "chapter:";

    public string PageNamespace { get; set; } = "page:";

    public string AlternatePageNamespace { get; set; } = "variant:";

    public string AlternatePageDefaultValue { get; set; } = "default";

    public string CoverPageTag { get; set; } = "meta:cover page";

    public string SinglePageComicTag { get; set; } = "meta:single page comic";

    public string FullTitleNoteName { get; set; } = "title";

    public string ComicCommentNoteName { get; set; } = "comment";

    public string OcrTextNoteName { get; set; } = "ocr";

    public string OcrEditorUrl { get; set; } = "http://127.0.0.1:5045/ocr-editor";

    public int BackgroundSyncIntervalMinutes { get; set; } = 15;

    public HydrusSettings Clone()
    {
        return new HydrusSettings
        {
            ApiUrl = ApiUrl,
            ApiAccessKey = ApiAccessKey,
            PrimaryTagService = PrimaryTagService,
            TagServiceKey = TagServiceKey,
            TargetFileDomain = TargetFileDomain,
            TitleNamespace = TitleNamespace,
            SetNamespace = SetNamespace,
            IndexNamespace = IndexNamespace,
            SeriesNamespace = SeriesNamespace,
            VolumeNamespace = VolumeNamespace,
            ChapterNamespace = ChapterNamespace,
            PageNamespace = PageNamespace,
            AlternatePageNamespace = AlternatePageNamespace,
            AlternatePageDefaultValue = AlternatePageDefaultValue,
            CoverPageTag = CoverPageTag,
            SinglePageComicTag = SinglePageComicTag,
            FullTitleNoteName = FullTitleNoteName,
            ComicCommentNoteName = ComicCommentNoteName,
            OcrTextNoteName = OcrTextNoteName,
            OcrEditorUrl = OcrEditorUrl,
            BackgroundSyncIntervalMinutes = BackgroundSyncIntervalMinutes
        };
    }
}

public sealed class HydrusServiceOption
{
    public string Name { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}

public sealed class HydrusServiceCatalog
{
    public IReadOnlyList<HydrusServiceOption> TagServices { get; init; } = Array.Empty<HydrusServiceOption>();

    public IReadOnlyList<HydrusServiceOption> FileServices { get; init; } = Array.Empty<HydrusServiceOption>();
}
