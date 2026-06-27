namespace HydrusComicCompanion.Models;

public sealed class SyncProgressUpdate
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string CurrentTitle { get; init; } = string.Empty;
}
