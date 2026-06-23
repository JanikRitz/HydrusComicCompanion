using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

public interface IHydrusMediaService
{
    string BuildThumbnailUrl(string hash);

    string BuildRenderUrl(
        string hash,
        int? width = null,
        int? height = null,
        int? renderFormat = null,
        int? renderQuality = null);

    string BuildOriginalFileUrl(string hash, bool download = false);

    Task<HydrusMediaResult> GetThumbnailAsync(string hash, CancellationToken cancellationToken = default);

    Task<HydrusMediaResult> GetRenderAsync(
        string hash,
        int? width = null,
        int? height = null,
        int? renderFormat = null,
        int? renderQuality = null,
        CancellationToken cancellationToken = default);

    Task<HydrusMediaResult> GetOriginalFileAsync(string hash, bool download = false, CancellationToken cancellationToken = default);
}
