namespace HydrusComicCompanion.Data;

public sealed class MetadataRecord
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public SeriesRecord Series { get; set; } = null!;
}
