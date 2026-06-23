using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace HydrusComicCompanion.Services;

public sealed class ComicImportService : IComicImportService
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif", ".avif" };

    private readonly IHydrusApiService _apiService;
    private readonly IDbContextFactory<SettingsDbContext> _dbContextFactory;
    private readonly HydrusSettings _settings;
    private readonly ILogger<ComicImportService> _logger;

    public ComicImportService(
        IHydrusApiService apiService,
        IDbContextFactory<SettingsDbContext> dbContextFactory,
        IOptions<HydrusSettings> settings,
        ILogger<ComicImportService> logger)
    {
        _apiService = apiService;
        _dbContextFactory = dbContextFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(List<ImportPage> Pages, ComicMetadata? Metadata)> ExtractArchiveAsync(
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

        // Ensure page 0 is always a chapter start
        if (!chapterStarts.Contains(0))
        {
            chapterStarts.Insert(0, 0);
        }

        var total = request.Pages.Count;
        var seriesTag = BuildTag(_settings.SeriesNamespace, request.SeriesName);

        // Step 1: Upload all pages to Hydrus (or confirm they already exist)
        var pageHashes = new string[total];
        var pageMimeTypes = new string[total];

        for (var i = 0; i < request.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = request.Pages[i];
            progress?.Report(new ImportProgressUpdate
            {
                Current = i + 1,
                Total = total,
                Message = $"Uploading page {i + 1}/{total}: {page.ArchiveFileName}"
            });

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
            pageMimeTypes[i] = page.MimeType;
        }

        // Step 2: Tag all pages in Hydrus
        var tagServiceName = _settings.PrimaryTagService;

        progress?.Report(new ImportProgressUpdate
        {
            Current = total,
            Total = total,
            Message = "Tagging pages in Hydrus…"
        });

        for (var i = 0; i < request.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (chapterNumber, pageNumber) = GetChapterAndPage(i, chapterStarts);

            var tags = new List<string>
            {
                seriesTag,
                BuildTag(_settings.VolumeNamespace, request.VolumeNumber.ToString()),
                BuildTag(_settings.ChapterNamespace, chapterNumber.ToString()),
                BuildTag(_settings.PageNamespace, pageNumber.ToString())
            };

            if (!string.IsNullOrWhiteSpace(request.Creator))
            {
                tags.Add($"creator:{request.Creator.Trim()}");
            }

            await _apiService.AddTagsAsync(pageHashes[i], tagServiceName, tags, cancellationToken);
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

    private async Task<(List<ImportPage> Pages, ComicMetadata? Metadata)> ExtractCbzAsync(
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

        var pages = BuildSortedPages(rawPages);
        return (pages, metadata);
    }

    private async Task<(List<ImportPage> Pages, ComicMetadata? Metadata)> ExtractCbrAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        using var rar = RarArchive.OpenArchive(ms, new SharpCompress.Readers.ReaderOptions());

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

        var pages = BuildSortedPages(rawPages);
        return (pages, metadata);
    }

    private static List<ImportPage> BuildSortedPages(List<(string Name, byte[] Data)> rawPages)
    {
        return rawPages
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select((p, index) => new ImportPage
            {
                Index = index,
                ArchiveFileName = p.Name,
                Data = p.Data,
                Sha256Hash = ComputeSha256(p.Data),
                MimeType = GetMimeType(p.Name)
            })
            .ToList();
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
            var numberRaw = root.Element("Number")?.Value?.Trim();
            var volume = int.TryParse(numberRaw, out var v) ? v : 1;

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

        var series = await db.Series
            .Include(s => s.Chapters).ThenInclude(c => c.Pages)
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Title == request.SeriesName, cancellationToken);

        if (series == null)
        {
            series = new SeriesRecord { Title = request.SeriesName };
            db.Series.Add(series);
        }

        series.Chapters.Clear();

        // Build chapter records
        for (var ci = 0; ci < chapterStarts.Count; ci++)
        {
            var chapterStartIdx = chapterStarts[ci];
            var nextChapterStartIdx = ci + 1 < chapterStarts.Count ? chapterStarts[ci + 1] : request.Pages.Count;
            var chapterNumber = ci + 1;

            var chapter = new ChapterRecord
            {
                VolumeNumber = request.VolumeNumber,
                ChapterNumber = chapterNumber,
                Title = $"Ch. {chapterNumber}"
            };

            var pageNumber = 1;
            for (var pi = chapterStartIdx; pi < nextChapterStartIdx; pi++)
            {
                chapter.Pages.Add(new PageRecord
                {
                    FileHash = pageHashes[pi],
                    PageNumber = pageNumber++,
                    MimeType = pageMimeTypes[pi]
                });
            }

            series.Chapters.Add(chapter);
        }

        // Rebuild metadata
        series.Metadata.Clear();
        if (!string.IsNullOrWhiteSpace(request.Creator))
        {
            series.Metadata.Add(new MetadataRecord { Key = "creator", Value = request.Creator.Trim() });
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
