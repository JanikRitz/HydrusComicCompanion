namespace HydrusComicCompanion.Models;

public sealed class LongRunningProgressUpdate
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
