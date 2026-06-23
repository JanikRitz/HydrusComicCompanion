using System.Text;
using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

public sealed class HydrusMediaService : IHydrusMediaService
{
    private readonly IHydrusApiService _hydrusApiService;

    public HydrusMediaService(IHydrusApiService hydrusApiService)
    {
        _hydrusApiService = hydrusApiService;
    }

    public string BuildThumbnailUrl(string hash)
    {
        return $"/media/thumbnail/{Uri.EscapeDataString(hash)}";
    }

    public string BuildRenderUrl(
        string hash,
        int? width = null,
        int? height = null,
        int? renderFormat = null,
        int? renderQuality = null)
    {
        var basePath = $"/media/render/{Uri.EscapeDataString(hash)}";
        var query = BuildRenderQuery(width, height, renderFormat, renderQuality);
        return string.IsNullOrWhiteSpace(query) ? basePath : $"{basePath}?{query}";
    }

    public string BuildOriginalFileUrl(string hash, bool download = false)
    {
        var basePath = $"/media/file/{Uri.EscapeDataString(hash)}";
        return download ? $"{basePath}?download=true" : basePath;
    }

    public Task<HydrusMediaResult> GetThumbnailAsync(string hash, CancellationToken cancellationToken = default)
    {
        return _hydrusApiService.GetThumbnailAsync(hash, cancellationToken);
    }

    public Task<HydrusMediaResult> GetRenderAsync(
        string hash,
        int? width = null,
        int? height = null,
        int? renderFormat = null,
        int? renderQuality = null,
        CancellationToken cancellationToken = default)
    {
        return _hydrusApiService.GetRenderedImageAsync(
            hash,
            width,
            height,
            renderFormat,
            renderQuality,
            download: false,
            cancellationToken);
    }

    public Task<HydrusMediaResult> GetOriginalFileAsync(string hash, bool download = false, CancellationToken cancellationToken = default)
    {
        return _hydrusApiService.GetOriginalFileAsync(hash, download, cancellationToken);
    }

    private static string BuildRenderQuery(int? width, int? height, int? renderFormat, int? renderQuality)
    {
        if ((width.HasValue && !height.HasValue) || (!width.HasValue && height.HasValue))
        {
            throw new ArgumentException("Width and height must both be provided when one is specified.");
        }

        var query = new StringBuilder();

        if (width.HasValue && height.HasValue)
        {
            AppendQueryValue(query, "width", width.Value.ToString());
            AppendQueryValue(query, "height", height.Value.ToString());
        }

        if (renderFormat.HasValue)
        {
            AppendQueryValue(query, "renderFormat", renderFormat.Value.ToString());
        }

        if (renderQuality.HasValue)
        {
            AppendQueryValue(query, "renderQuality", renderQuality.Value.ToString());
        }

        return query.ToString();
    }

    private static void AppendQueryValue(StringBuilder query, string key, string value)
    {
        if (query.Length > 0)
        {
            query.Append('&');
        }

        query
            .Append(Uri.EscapeDataString(key))
            .Append('=')
            .Append(Uri.EscapeDataString(value));
    }
}
