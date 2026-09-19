using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

/// <summary>
/// Represents a Calibre book entry from the calibredb list output.
/// </summary>
internal readonly struct CalibreBookListEntry
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("authors")]
    public string? Authors { get; init; }

    [JsonPropertyName("formats")]
    public string[]? Formats { get; init; }
}

/// <summary>
/// Interacts with a Calibre library via the calibredb command-line tool.
/// </summary>
public sealed class CalibreService(IComicImportService comicImportService, ILogger<CalibreService> logger) : ICalibreService
{
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalibreBookEntry>> DiscoverBooksAsync(
        string libraryPath,
        string? searchQuery = null,
        IProgress<LongRunningProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReportProgress(
            progress,
            message: "Querying Calibre library...",
            detail: string.IsNullOrWhiteSpace(searchQuery)
                ? libraryPath
                : $"{libraryPath} · filter: {searchQuery.Trim()}");

        var command = new List<string>
        {
            "list",
            "--for-machine",
            "--fields",
            "title,authors,formats",
            "--with-library",
            libraryPath
        };

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            command.Add("--search");
            command.Add(searchQuery.Trim());
        }

        var stdout = await RunCalibreDbAsync(command, "list", DiscoverTimeout, cancellationToken);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = JsonSerializer.Deserialize<CalibreBookListEntry[]>(stdout, options)
            ?? [];

        ReportProgress(
            progress,
            message: "Filtering discovered books...",
            detail: $"{entries.Length} entries returned from Calibre.",
            total: entries.Length);

        var books = new List<CalibreBookEntry>();

        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[index];
            var availableFormats = ParseFormats(entry.Formats);
            if (!availableFormats.Select(Path.GetExtension).Any(format => format is ".cbz" or ".cbr"))
            {
                continue;
            }

            var title = entry.Title;
            var authors = entry.Authors;
            var authorLabel = string.IsNullOrWhiteSpace(authors) ? "Unknown author" : authors;
            var displayTitle = string.IsNullOrWhiteSpace(title) ? $"Book {entry.Id}" : title.Trim();

            books.Add(new CalibreBookEntry
            {
                Id = entry.Id,
                Title = displayTitle,
                Authors = authorLabel,
                Formats = availableFormats
            });

            if (index == entries.Length - 1 || (index + 1) % 100 == 0)
            {
                ReportProgress(
                    progress,
                    message: "Filtering discovered books...",
                    detail: $"Matched {books.Count} archive-ready book(s).",
                    current: index + 1,
                    total: entries.Length);
            }
        }

        books.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        ReportProgress(
            progress,
            message: "Calibre discovery complete.",
            detail: $"Found {books.Count} archive-ready book(s).",
            current: books.Count,
            total: books.Count == 0 ? 0 : books.Count);

        return books;
    }

    /// <inheritdoc />
    public async Task<(ComicImportPreparation Preparation, CalibreMetadataSnapshot Metadata)> ExtractBookAsync(
        int bookId,
        string libraryPath,
        IProgress<LongRunningProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReportProgress(progress, 1, 4, "Loading Calibre metadata...", $"Book #{bookId}");
        var metadata = await LoadMetadataAsync(bookId, libraryPath, cancellationToken);

        var tempRoot = Path.Combine(Path.GetTempPath(), "HydrusComicCompanion", "calibre", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ReportProgress(progress, 2, 4, "Exporting Calibre archive...", $"Book #{bookId}");
            await RunCalibreDbAsync(
            [
                "export",
                bookId.ToString(),
                "--to-dir",
                tempRoot,
                "--formats",
                "cbz,cbr",
                "--dont-save-cover",
                "--dont-save-extra-files",
                "--dont-write-opf",
                "--with-library",
                libraryPath
            ],
            "export",
            ExportTimeout,
            cancellationToken);

            ReportProgress(progress, 3, 4, "Locating exported archive...", $"Book #{bookId}");
            var archivePath = Directory
                .EnumerateFiles(tempRoot, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                {
                    var extension = Path.GetExtension(path);
                    return string.Equals(extension, ".cbz", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".cbr", StringComparison.OrdinalIgnoreCase);
                });

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                throw new InvalidOperationException("No CBZ/CBR archive was exported for this Calibre book.");
            }

            ReportProgress(progress, 4, 4, "Extracting comic pages...", Path.GetFileName(archivePath));
            await using var archiveStream = File.OpenRead(archivePath);
            var preparation = await comicImportService.ExtractArchiveAsync(
                archiveStream,
                Path.GetFileName(archivePath),
                cancellationToken);

            return (preparation, metadata);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // best effort temporary cleanup
            }
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────

    private async Task<CalibreMetadataSnapshot> LoadMetadataAsync(
        int bookId,
        string libraryPath,
        CancellationToken cancellationToken)
    {
        var opfXml = await RunCalibreDbAsync(
        [
            "show_metadata",
            "--as-opf",
            bookId.ToString(),
            "--with-library",
            libraryPath
        ],
        "show_metadata",
        MetadataTimeout,
        cancellationToken);

        return ParseCalibreOpfMetadata(opfXml);
    }

    private static CalibreMetadataSnapshot ParseCalibreOpfMetadata(string opfXml)
    {
        if (string.IsNullOrWhiteSpace(opfXml))
        {
            return new CalibreMetadataSnapshot();
        }

        var document = XDocument.Parse(opfXml, LoadOptions.PreserveWhitespace);
        var dc = (XNamespace)"http://purl.org/dc/elements/1.1/";
        var calibre = (XNamespace)"http://calibre.kovidgoyal.net/2009/metadata";

        var title = document.Descendants(dc + "title")
            .Select(e => e.Value.Trim())
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        var series = document.Descendants(calibre + "series")
            .Select(e => e.Value.Trim())
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        var creator = string.Join(", ", document.Descendants(dc + "creator")
            .Select(e => e.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v)));

        var commentsRaw = document.Descendants(dc + "description")
            .Select(e => e.Value)
            .FirstOrDefault() ?? string.Empty;
        var comments = StripHtml(commentsRaw).Trim();

        var tags = document.Descendants(dc + "subject")
            .Select(e => e.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int? volumeNumber = null;
        var seriesIndexRaw = document.Descendants(calibre + "series_index")
            .Select(e => e.Value.Trim())
            .FirstOrDefault();
        if (double.TryParse(seriesIndexRaw, out var seriesIndex))
        {
            var rounded = (int)Math.Round(seriesIndex, MidpointRounding.AwayFromZero);
            if (rounded > 0)
            {
                volumeNumber = rounded;
            }
        }

        return new CalibreMetadataSnapshot
        {
            Title = title,
            Series = series,
            Creator = creator,
            Comments = comments,
            VolumeNumber = volumeNumber,
            Tags = tags
        };
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var withoutTags = System.Text.RegularExpressions.Regex.Replace(
            input,
            "<[^>]+>",
            " ",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return System.Net.WebUtility.HtmlDecode(withoutTags);
    }

    private async Task<string> RunCalibreDbAsync(
        IReadOnlyList<string> arguments,
        string operationName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("calibredb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startedAt = Stopwatch.StartNew();
        logger.LogInformation("Starting calibredb {Operation} for library command {Arguments}", operationName, string.Join(' ', arguments));

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start calibredb process.");
            }

            using var registration = timeoutCts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // best effort process cleanup during cancellation/timeout
                }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr)
                    ? $"calibredb exited with code {process.ExitCode}."
                    : stderr.Trim();
                throw new InvalidOperationException(message);
            }

            logger.LogInformation("Completed calibredb {Operation} in {ElapsedMilliseconds} ms", operationName, startedAt.ElapsedMilliseconds);
            return stdout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Canceled calibredb {Operation} after {ElapsedMilliseconds} ms", operationName, startedAt.ElapsedMilliseconds);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Timed out calibredb {Operation} after {ElapsedMilliseconds} ms", operationName, startedAt.ElapsedMilliseconds);
            throw new TimeoutException($"calibredb {operationName} timed out after {timeout.TotalMinutes:0.#} minute(s).");
        }
    }

    private static void ReportProgress(
        IProgress<LongRunningProgressUpdate>? progress,
        int current = 0,
        int total = 0,
        string message = "",
        string detail = "")
    {
        progress?.Report(new LongRunningProgressUpdate
        {
            Current = current,
            Total = total,
            Message = message,
            Detail = detail
        });
    }

    private static List<string> ParseFormats(string[]? formats)
    {
        if (formats == null || formats.Length == 0)
        {
            return [];
        }

        return formats
            .SelectMany(format => format.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(format => format.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
