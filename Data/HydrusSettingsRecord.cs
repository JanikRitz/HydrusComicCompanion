namespace HydrusComicCompanion.Data;

public sealed class HydrusSettingsRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string ApiUrl { get; set; } = string.Empty;

    public string ProtectedApiAccessKey { get; set; } = string.Empty;

    public string PrimaryTagService { get; set; } = string.Empty;

    public string TargetFileDomain { get; set; } = string.Empty;

    public string SeriesNamespace { get; set; } = string.Empty;

    public string VolumeNamespace { get; set; } = string.Empty;

    public string ChapterNamespace { get; set; } = string.Empty;

    public string PageNamespace { get; set; } = string.Empty;

    public int BackgroundSyncIntervalMinutes { get; set; }
}
