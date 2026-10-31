using HydrusComicCompanion.Data;

namespace HydrusComicCompanion.Models;

public sealed class ComicReaderVariantSelection
{
    private int? _comicId;
    private string? _pinnedLabel;
    private bool _pinDefault;

    public bool IsPinned => _pinDefault || _pinnedLabel is not null;
    public string PinnedDisplayName => _pinDefault ? "Default" : _pinnedLabel ?? string.Empty;

    public void SetComic(int comicId)
    {
        if (_comicId == comicId)
        {
            return;
        }

        _comicId = comicId;
        Unpin();
    }

    public static bool CanPin(PageVariantRecord variant) => variant.IsDefault || !string.IsNullOrWhiteSpace(variant.Label);

    public void Pin(PageVariantRecord variant)
    {
        if (!CanPin(variant))
        {
            throw new InvalidOperationException("An alternate variant needs a label before it can be pinned across pages.");
        }

        _pinDefault = variant.IsDefault;
        _pinnedLabel = _pinDefault ? null : variant.Label;
    }

    public void Unpin()
    {
        _pinDefault = false;
        _pinnedLabel = null;
    }

    public bool MatchesPin(PageVariantRecord variant) => IsPinned && (_pinDefault
        ? variant.IsDefault
        : string.Equals(variant.Label, _pinnedLabel, StringComparison.OrdinalIgnoreCase));

    public static List<PageVariantRecord> OrderVariants(PageRecord page) => page.Variants
        .OrderByDescending(variant => variant.IsDefault)
        .ThenBy(variant => variant.Label, StringComparer.OrdinalIgnoreCase)
        .ThenBy(variant => variant.FileHash, StringComparer.Ordinal)
        .ToList();

    public PageVariantRecord? Select(PageRecord page)
    {
        var variants = OrderVariants(page);
        if (IsPinned)
        {
            return variants.FirstOrDefault(MatchesPin) ?? variants.FirstOrDefault();
        }

        return variants.FirstOrDefault();
    }
}
