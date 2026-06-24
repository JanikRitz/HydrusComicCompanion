using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services;

namespace HydrusComicCompanion.Components.ImportWizard.Services;

/// <summary>
/// Helper service for managing page thumbnail preloading during the chapter placement step.
/// </summary>
public class ThumbnailPreloadService
{
    private readonly IHydrusMediaService _mediaService;
    private const int PreloadBatchSize = 8;

    public ThumbnailPreloadService(IHydrusMediaService mediaService)
    {
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
    }

    /// <summary>
    /// Preloads thumbnails for all pages in batches, yielding control between batches.
    /// </summary>
    /// <param name="pages">List of pages to generate thumbnails for.</param>
    /// <param name="existingThumbnails">Dictionary to populate with generated thumbnail URLs.</param>
    /// <param name="onBatchCompleted">Callback invoked after each batch is processed.</param>
    /// <param name="shouldContinue">Predicate to check if preloading should continue.</param>
    public async Task PreloadThumbnailsAsync(
        List<ImportPage> pages,
        Dictionary<int, string> existingThumbnails,
        Func<Task> onBatchCompleted,
        Func<bool> shouldContinue)
    {
        var processed = 0;

        foreach (var page in pages)
        {
            if (!shouldContinue())
            {
                break;
            }

            if (existingThumbnails.ContainsKey(page.Index))
            {
                continue;
            }

            existingThumbnails[page.Index] = BuildThumbnailSrc(page);
            processed++;

            if (processed % PreloadBatchSize == 0)
            {
                await onBatchCompleted();
                await Task.Yield();
            }
        }

        await onBatchCompleted();
    }

    /// <summary>
    /// Builds a data URL or Hydrus thumbnail URL for a given page.
    /// </summary>
    private string BuildThumbnailSrc(ImportPage page)
    {
        if (page.Data is { Length: > 0 })
        {
            var mimeType = string.IsNullOrWhiteSpace(page.MimeType) ? "image/jpeg" : page.MimeType;
            return $"data:{mimeType};base64,{Convert.ToBase64String(page.Data)}";
        }

        if (!string.IsNullOrWhiteSpace(page.Sha256Hash))
        {
            return _mediaService.BuildThumbnailUrl(page.Sha256Hash);
        }

        return string.Empty;
    }
}
