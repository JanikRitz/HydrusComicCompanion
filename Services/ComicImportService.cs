using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Archives.Rar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace HydrusComicCompanion.Services;

public sealed class ComicImportService : IComicImportService
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif", ".avif" };

    private readonly IHydrusApiService _apiService;
    private readonly IHydrusSettingsService _settingsService;
    private readonly IDbContextFactory<SettingsDbContext> _dbContextFactory;
    private readonly ILogger<ComicImportService> _logger;

    public ComicImportService(
        IHydrusApiService apiService,
        IDbContextFactory<SettingsDbContext> dbContextFactory,
        IHydrusSettingsService settingsService,
        ILogger<ComicImportService> logger)
    {
        _apiService = apiService;
        _dbContextFactory = dbContextFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ComicImportPreparation> ExtractArchiveAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".cbz" => await ExtractCbzAsync(fileStream, cancellationToken),
            ".cbr" => await ExtractCbrAsync(fileStream, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported archive format: {extension}. Only .cbz and .cbr files are supported.")
        };
    }

    /// <inheritdoc/>
    public async Task<int> ImportComicAsync(
        ComicImportRequest request,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SeriesName))
        {
            throw new ArgumentException("Series name is required.", nameof(request));
        }

        if (request.Pages.Count == 0)
        {
            throw new ArgumentException("No pages to import.", nameof(request));
        }

        var chapterStarts = request.ChapterStartPageIndices
            .Where(i => i >= 0 && i < request.Pages.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        // Ensure page 0 is always a chapter start when the import is chaptered.
        if (chapterStarts.Count > 0 && !chapterStarts.Contains(0))
        {
            chapterStarts.Insert(0, 0);
        }

        var total = request.Pages.Count;
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        var titleTag = BuildTag(settings.TitleNamespace, request.SeriesName);

        // Step 1: Upload all pages to Hydrus (or confirm they already exist)
        var pageHashes = new string[total];
        var pageMimeTypes = new string[total];

        for (var i = 0; i < request.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = request.Pages[i];
            var hasBytes = page.Data is { Length: > 0 };
            var hasHash = !string.IsNullOrWhiteSpace(page.Sha256Hash);

            progress?.Report(new ImportProgressUpdate
            {
                Current = i + 1,
                Total = total,
                Message = hasBytes
                    ? $"Uploading page {i + 1}/{total}: {page.ArchiveFileName}"
                    : $"Using existing Hydrus file {i + 1}/{total}: {page.ArchiveFileName}"
            });

            if (hasBytes)
            {
                var result = await _apiService.AddFileAsync(page.Data, page.MimeType, cancellationToken);

                if (!result.IsAvailable)
                {
                    _logger.LogWarning("Page {Index} ({File}) could not be imported into Hydrus (status={Status}, note={Note})",
                        i, page.ArchiveFileName, result.Status, result.Note);
                    throw new InvalidOperationException(
                        $"Hydrus rejected page {i + 1} ({page.ArchiveFileName}): status={result.Status}, note={result.Note}");
                }

                // Use the hash returned by Hydrus (matches our SHA-256 but canonicalized by Hydrus)
                pageHashes[i] = result.Hash;
            }
            else if (hasHash)
            {
                pageHashes[i] = page.Sha256Hash;
            }
            else
            {
                throw new InvalidOperationException($"Page {i + 1} ({page.ArchiveFileName}) has neither file bytes nor a Hydrus hash.");
            }

            pageMimeTypes[i] = page.MimeType;
        }

        // Step 2: Tag all pages in Hydrus
        progress?.Report(new ImportProgressUpdate
        {
            Current = total,
            Total = total,
            Message = "Tagging pages in Hydrus…"
        });

        for (var i = 0; i < request.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tags = new List<string>
            {
                titleTag
            };

            var effectiveVolumeNumber = GetVolumeForPage(i, request);
            if (effectiveVolumeNumber.HasValue)
            {
                tags.Add(BuildTag(settings.VolumeNamespace, effectiveVolumeNumber.Value.ToString()));
            }

            if (chapterStarts.Count > 0)
            {
                var (_, fallbackPageNumber) = GetChapterAndPage(i, chapterStarts);
                var chapterNumber = GetChapterWithinVolume(i, chapterStarts, request.VolumeStarts);
                var pageNumber = ResolvePageNumber(request.Pages[i].PageNumber, fallbackPageNumber);
                tags.Add(BuildTag(settings.ChapterNamespace, chapterNumber.ToString()));
                tags.Add(BuildTag(settings.PageNamespace, pageNumber.ToString()));
            }
            else
            {
                var pageNumber = ResolvePageNumber(request.Pages[i].PageNumber, i + 1);
                tags.Add(BuildTag(settings.PageNamespace, pageNumber.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(request.Creator))
            {
                tags.Add($"creator:{request.Creator.Trim()}");
            }

            tags.AddRange(request.CustomTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

            await _apiService.AddTagsAsync(pageHashes[i], settings.TagServiceKey, tags, cancellationToken);
        }

        // Step 3: Persist to local cache
        progress?.Report(new ImportProgressUpdate
        {
            Current = total,
            Total = total,
            Message = "Saving to local cache…"
        });

        var seriesId = await PersistToCacheAsync(request, chapterStarts, pageHashes, pageMimeTypes, cancellationToken);

        progress?.Report(new ImportProgressUpdate
        {
            Current = total,
            Total = total,
            Message = "Import complete."
        });

        return seriesId;
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private async Task<ComicImportPreparation> ExtractCbzAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        // Copy to a MemoryStream so ZipArchive can seek (browser stream may not support seeking)
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

        ComicMetadata? metadata = null;
        var rawPages = new List<(string Name, byte[] Data)>();

        foreach (var entry in zip.Entries)
        {
            if (entry.Length == 0)
            {
                continue;
            }

            var entryName = entry.FullName;

            if (string.Equals(Path.GetFileName(entryName), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
            {
                using var entryStream = entry.Open();
                metadata = ParseComicInfoXml(entryStream);
                continue;
            }

            if (IsImageEntry(entryName))
            {
                using var entryStream = entry.Open();
                var bytes = await ReadAllBytesAsync(entryStream, cancellationToken);
                rawPages.Add((entryName, bytes));
            }
        }

        return BuildPreparation(rawPages, metadata, []);
    }

    private async Task<ComicImportPreparation> ExtractCbrAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        using var rar = RarArchive.OpenArchive(ms, new SharpCompress.Readers.ReaderOptions()); // default options are sufficient for standard CBR files

        ComicMetadata? metadata = null;
        var rawPages = new List<(string Name, byte[] Data)>();

        foreach (var entry in rar.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            var entryKey = entry.Key ?? string.Empty;

            if (string.Equals(Path.GetFileName(entryKey), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
            {
                await using var entryStream = entry.OpenEntryStream();
                metadata = ParseComicInfoXml(entryStream);
                continue;
            }

            if (IsImageEntry(entryKey))
            {
                await using var entryStream = entry.OpenEntryStream();
                var bytes = await ReadAllBytesAsync(entryStream, cancellationToken);
                rawPages.Add((entryKey, bytes));
            }
        }

        return BuildPreparation(rawPages, metadata, []);
    }

    private static ComicImportPreparation BuildPreparation(
        List<(string Name, byte[] Data)> rawPages,
        ComicMetadata? metadata,
        List<int> chapterStartPageIndices)
    {
        return new ComicImportPreparation
        {
            Pages = rawPages
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select((p, index) => new ImportPage
                {
                    Index = index,
                    ArchiveFileName = p.Name,
                    Data = p.Data,
                    Sha256Hash = ComputeSha256(p.Data),
                    MimeType = GetMimeType(p.Name),
                    PageNumber = index + 1
                })
                .ToList(),
            Metadata = metadata,
            ChapterStartPageIndices = chapterStartPageIndices.Count == 0 ? [0] : chapterStartPageIndices
        };
    }

    private static ComicMetadata? ParseComicInfoXml(Stream stream)
    {
        try
        {
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root == null)
            {
                return null;
            }

            var series = root.Element("Series")?.Value?.Trim() ?? string.Empty;
            var writer = root.Element("Writer")?.Value?.Trim() ?? string.Empty;
            var volumeRaw = root.Element("Number")?.Value?.Trim();
            var volume = int.TryParse(volumeRaw, out var v) ? v : (int?)null;

            return new ComicMetadata
            {
                Series = series,
                Creator = writer,
                VolumeNumber = volume
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<int> PersistToCacheAsync(
        ComicImportRequest request,
        List<int> chapterStarts,
        string[] pageHashes,
        string[] pageMimeTypes,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var series = await db.Comic
            .Include(s => s.Chapters).ThenInclude(c => c.Pages)
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Title == request.SeriesName, cancellationToken);

        if (series == null)
        {
            series = new ComicsRecord { Title = request.SeriesName };
            db.Comic.Add(series);
        }

        // Merge chapters into existing series and reject conflicting overlaps.
        if (chapterStarts.Count == 0)
        {
            var incomingPages = new List<PageRecord>();
            for (var pi = 0; pi < request.Pages.Count; pi++)
            {
                incomingPages.Add(new PageRecord
                {
                    FileHash = pageHashes[pi],
                    PageNumber = ResolvePageNumber(request.Pages[pi].PageNumber, pi + 1),
                    MimeType = pageMimeTypes[pi]
                });
            }

            var flatVolumeNumber = GetVolumeForPage(0, request);
            var existingChapter = series.Chapters.FirstOrDefault(ch =>
                ch.VolumeNumber == flatVolumeNumber &&
                ch.ChapterNumber == null);

            if (existingChapter == null)
            {
                var chapter = new ChapterRecord
                {
                    VolumeNumber = flatVolumeNumber,
                    ChapterNumber = null,
                    Title = flatVolumeNumber.HasValue ? $"Vol. {flatVolumeNumber.Value}" : null,
                    Pages = incomingPages
                };

                series.Chapters.Add(chapter);
            }
            else if (!HasSamePages(existingChapter, incomingPages))
            {
                throw new InvalidOperationException(
                    $"Cannot merge import for series '{request.SeriesName}': conflicting content for volume {flatVolumeNumber?.ToString() ?? "(none)"}, chapter (none).");
            }
        }
        else
        {
            for (var ci = 0; ci < chapterStarts.Count; ci++)
            {
                var chapterStartIdx = chapterStarts[ci];
                var nextChapterStartIdx = ci + 1 < chapterStarts.Count ? chapterStarts[ci + 1] : request.Pages.Count;
                var chapterVolumeNumber = GetVolumeForPage(chapterStartIdx, request);
                var chapterNumber = (decimal?)GetChapterWithinVolume(chapterStartIdx, chapterStarts, request.VolumeStarts);

                var incomingPages = new List<PageRecord>();
                var fallbackPageNumber = 1;
                for (var pi = chapterStartIdx; pi < nextChapterStartIdx; pi++)
                {
                    incomingPages.Add(new PageRecord
                    {
                        FileHash = pageHashes[pi],
                        PageNumber = ResolvePageNumber(request.Pages[pi].PageNumber, fallbackPageNumber++),
                        MimeType = pageMimeTypes[pi]
                    });
                }

                var existingChapter = series.Chapters.FirstOrDefault(ch =>
                    ch.VolumeNumber == chapterVolumeNumber &&
                    ch.ChapterNumber == chapterNumber);

                if (existingChapter == null)
                {
                    var chapter = new ChapterRecord
                    {
                        VolumeNumber = chapterVolumeNumber,
                        ChapterNumber = chapterNumber,
                        Title = $"Ch. {chapterNumber}",
                        Pages = incomingPages
                    };

                    series.Chapters.Add(chapter);
                    continue;
                }

                if (!HasSamePages(existingChapter, incomingPages))
                {
                    throw new InvalidOperationException(
                        $"Cannot merge import for series '{request.SeriesName}': conflicting content for volume {chapterVolumeNumber?.ToString() ?? "(none)"}, chapter {chapterNumber}.");
                }
            }
        }

        // Merge metadata without deleting existing values.
        if (!string.IsNullOrWhiteSpace(request.Creator))
        {
            var creator = request.Creator.Trim();
            var hasCreator = series.Metadata.Any(m =>
                string.Equals(m.Key, "creator", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Value, creator, StringComparison.OrdinalIgnoreCase));

            if (!hasCreator)
            {
                series.Metadata.Add(new MetadataRecord { Key = "creator", Value = creator });
            }
        }

        if (string.IsNullOrEmpty(series.CoverFileHash) && pageHashes.Length > 0)
        {
            series.CoverFileHash = pageHashes[0];
        }

        series.LastSyncedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return series.Id;
    }

    /// <summary>Returns the (1-based chapter number, 1-based page-within-chapter) for a given 0-based page index.</summary>
    private static (int Chapter, int Page) GetChapterAndPage(int pageIndex, List<int> chapterStarts)
    {
        var chapterNumber = 1;
        var chapterStart = 0;

        for (var i = 0; i < chapterStarts.Count; i++)
        {
            if (chapterStarts[i] <= pageIndex)
            {
                chapterNumber = i + 1;
                chapterStart = chapterStarts[i];
            }
            else
            {
                break;
            }
        }

        var pageNumber = pageIndex - chapterStart + 1;
        return (chapterNumber, pageNumber);
    }

    /// <summary>
    /// Returns the effective volume number for the given page index.
    /// Uses per-page <see cref="VolumeStartEntry"/> data when available; falls back to
    /// <see cref="ComicImportRequest.VolumeNumber"/> for single-volume imports.
    /// </summary>
    private static int? GetVolumeForPage(int pageIndex, ComicImportRequest request)
    {
        if (request.VolumeStarts.Count == 0)
        {
            return request.VolumeNumber;
        }

        var entry = request.VolumeStarts
            .Where(vs => vs.PageIndex <= pageIndex)
            .OrderByDescending(vs => vs.PageIndex)
            .FirstOrDefault();

        return entry?.VolumeNumber ?? request.VolumeNumber;
    }

    /// <summary>
    /// Returns the 1-based chapter number within its volume for the chapter containing
    /// <paramref name="pageIndex"/>. Resets to 1 at each volume start.
    /// Falls back to sequential global numbering when no volume starts are defined.
    /// </summary>
    private static int GetChapterWithinVolume(int pageIndex, List<int> chapterStarts, List<VolumeStartEntry> volumeStarts)
    {
        if (volumeStarts.Count == 0)
        {
            // No volume boundaries — use global sequential chapter number (original behaviour).
            var chapterStart = chapterStarts.Where(s => s <= pageIndex).DefaultIfEmpty(0).Max();
            return chapterStarts.Count(s => s <= chapterStart);
        }

        var chapterPageStart = chapterStarts.Where(s => s <= pageIndex).DefaultIfEmpty(0).Max();
        var volumePageStart = volumeStarts
            .Where(vs => vs.PageIndex <= chapterPageStart)
            .OrderByDescending(vs => vs.PageIndex)
            .Select(vs => (int?)vs.PageIndex)
            .FirstOrDefault() ?? 0;

        // Count chapter starts from the volume boundary up to and including this chapter.
        return chapterStarts.Count(s => s >= volumePageStart && s <= chapterPageStart);
    }

    private static int ResolvePageNumber(int? pageNumber, int fallback)
        => pageNumber is > 0 ? pageNumber.Value : fallback;

    private static bool HasSamePages(ChapterRecord existingChapter, List<PageRecord> incomingPages)
    {
        var existingPages = existingChapter.Pages
            .OrderBy(p => p.PageNumber)
            .ToList();

        if (existingPages.Count != incomingPages.Count)
        {
            return false;
        }

        for (var i = 0; i < existingPages.Count; i++)
        {
            var existing = existingPages[i];
            var incoming = incomingPages[i];

            if (existing.PageNumber != incoming.PageNumber)
            {
                return false;
            }

            if (!string.Equals(existing.FileHash, incoming.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(existing.MimeType, incoming.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildTag(string @namespace, string value)
    {
        var ns = @namespace.Trim().TrimEnd(':');
        return string.IsNullOrWhiteSpace(ns) ? value : $"{ns}:{value}";
    }

    private static bool IsImageEntry(string name)
    {
        var ext = Path.GetExtension(name);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    private static string GetMimeType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".avif" => "image/avif",
            _ => "application/octet-stream"
        };
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }
}
