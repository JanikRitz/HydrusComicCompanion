namespace HydrusComicCompanion.Models;

public sealed class HydrusSettings
{
    public string ApiUrl { get; set; } = "http://127.0.0.1:45869";

    public string ApiAccessKey { get; set; } = string.Empty;

    public string PrimaryTagService { get; set; } = "my tags";

    public string TagServiceKey { get; set; } = string.Empty;

    public string TargetFileDomain { get; set; } = "all local files";

    public string TitleNamespace { get; set; } = "title:";

    public string SeriesNamespace { get; set; } = string.Empty;

    public string VolumeNamespace { get; set; } = "volume:";

    public string ChapterNamespace { get; set; } = "chapter:";

    public string PageNamespace { get; set; } = "page:";

    public string CoverPageTag { get; set; } = "meta:cover page";

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
            SeriesNamespace = SeriesNamespace,
            VolumeNamespace = VolumeNamespace,
            ChapterNamespace = ChapterNamespace,
            PageNamespace = PageNamespace,
            CoverPageTag = CoverPageTag,
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
