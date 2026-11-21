using System.Text.Json;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HydrusComicCompanion.Services;

public sealed record ManagerComic(int Id, string Title, string? CoverHash, DateTimeOffset? UpdatedAt,
    string Kind, string? ReaderPath, int? PageCount, int? ImageCount);

public enum ManagerComicSort
{
    Default,
    TitleAscending,
    TitleDescending,
    DateNewest,
    DateOldest,
    Random
}

public sealed class ManagerComicCatalog(IDbContextFactory<SettingsDbContext> dbContextFactory)
{
    public static void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/manager/comics", async (
            string? q, int? limit, string? sort, string? kind, ManagerComicCatalog catalog, CancellationToken cancellationToken) =>
        {
            var sortOrder = ManagerComicSort.Default;
            if (sort is not null && (!Enum.TryParse(sort, ignoreCase: true, out sortOrder)
                || !string.Equals(sort, sortOrder.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest("Unknown comic sort order.");
            }

            if (limit is <= 0)
                return Results.BadRequest("Limit must be positive.");

            CollectionKind? collectionKind = null;
            if (kind is not null)
            {
                if (string.Equals(kind, nameof(CollectionKind.Comic), StringComparison.OrdinalIgnoreCase))
                    collectionKind = CollectionKind.Comic;
                else if (string.Equals(kind, nameof(CollectionKind.Imageset), StringComparison.OrdinalIgnoreCase))
                    collectionKind = CollectionKind.Imageset;
                else
                    return Results.BadRequest("Unknown collection kind.");
            }

            return Results.Ok(await catalog.SearchAsync(q, Math.Min(limit ?? 50, 200), sortOrder, cancellationToken, collectionKind));
        });
    }

    public async Task<List<ManagerComic>> SearchAsync(
        string? query, int limit, ManagerComicSort sort, CancellationToken cancellationToken = default,
        CollectionKind? kind = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.CreateCollation("manager_title", StringComparer.OrdinalIgnoreCase.Compare);
        connection.CreateFunction<string?, string?, bool>("manager_contains", (text, value) =>
            value is not null && text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true, isDeterministic: true);
        connection.CreateFunction<string?, string?, bool>("manager_has_tag", HasTag, isDeterministic: true);

        var orderBy = sort switch
        {
            ManagerComicSort.TitleDescending => "Title COLLATE manager_title DESC, series.Id",
            ManagerComicSort.DateNewest => "series.LastSyncedAt IS NULL, series.LastSyncedAt DESC, series.Id",
            ManagerComicSort.DateOldest => "series.LastSyncedAt IS NULL, series.LastSyncedAt, series.Id",
            ManagerComicSort.Random => "random()",
            _ => "Title COLLATE manager_title, series.Id"
        };

        var sql = $"""
            SELECT series.Id, COALESCE(NULLIF(TRIM(series.DisplayTitle), ''), series.Title) AS Title,
                   COALESCE(NULLIF(TRIM(series.CoverFileHash), ''), firstFile.FileHash) AS CoverHash,
                   series.LastSyncedAt AS UpdatedAt,
                   CASE WHEN series.Kind = @comicKind THEN 'Comic' ELSE 'Imageset' END AS Kind,
                   CASE WHEN series.Kind = @comicKind THEN (
                       SELECT COUNT(*) FROM Pages AS page
                       JOIN Chapters AS chapter ON chapter.Id = page.ChapterId
                       WHERE chapter.SeriesId = series.Id
                   ) END AS PageCount,
                   CASE WHEN series.Kind = @imagesetKind THEN (
                       SELECT COUNT(*) FROM PageVariants AS image
                       JOIN Pages AS page ON page.Id = image.PageId
                       JOIN Chapters AS chapter ON chapter.Id = page.ChapterId
                       WHERE chapter.SeriesId = series.Id
                   ) END AS ImageCount,
                   CASE
                       WHEN firstFile.Id IS NULL THEN NULL
                       WHEN series.Kind = @imagesetKind THEN 'collection/' || series.Id || '/image/' || firstFile.FileHash
                       ELSE 'read/' || series.Id || '/' || firstPage.ChapterId || '/' || (
                           SELECT COUNT(*) FROM Pages AS preceding
                           WHERE preceding.ChapterId = firstPage.ChapterId AND (
                               preceding.PageNumber < firstPage.PageNumber
                               OR (preceding.PageNumber = firstPage.PageNumber AND preceding.Id <= firstPage.Id)
                           )
                       )
                   END AS ReaderPath
            FROM Series AS series
            LEFT JOIN PageVariants AS firstFile ON firstFile.Id = (
                SELECT variant.Id FROM Chapters AS chapter
                JOIN Pages AS page ON page.ChapterId = chapter.Id
                JOIN PageVariants AS variant ON variant.PageId = page.Id
                WHERE chapter.SeriesId = series.Id AND TRIM(variant.FileHash) <> ''
                ORDER BY
                    CASE WHEN series.Kind = @imagesetKind THEN variant.ImageIndex IS NULL END,
                    CASE WHEN series.Kind = @imagesetKind THEN variant.ImageIndex END,
                    CASE WHEN series.Kind = @comicKind THEN chapter.VolumeNumber END,
                    CASE WHEN series.Kind = @comicKind THEN CAST(chapter.ChapterNumber AS NUMERIC) END,
                    CASE WHEN series.Kind = @comicKind THEN chapter.Id END,
                    CASE WHEN series.Kind = @comicKind THEN page.PageNumber END,
                    CASE WHEN series.Kind = @comicKind THEN page.Id END,
                    CASE WHEN series.Kind = @comicKind THEN variant.IsDefault END DESC,
                    variant.FileHash COLLATE BINARY, variant.Id
                LIMIT 1
            )
            LEFT JOIN Pages AS firstPage ON firstPage.Id = firstFile.PageId
                        WHERE series.Kind IN (@comicKind, @imagesetKind)
                            AND (@kind IS NULL OR series.Kind = @kind) AND (
                @query IS NULL OR manager_contains(series.Title, @query)
                OR manager_contains(series.DisplayTitle, @query)
                OR manager_has_tag(series.CachedPageTagsJson, @query)
                OR EXISTS (
                    SELECT 1 FROM Chapters AS chapter
                    JOIN Pages AS page ON page.ChapterId = chapter.Id
                    JOIN PageVariants AS variant ON variant.PageId = page.Id
                    WHERE chapter.SeriesId = series.Id AND manager_contains(variant.OcrText, @query)
                )
            )
            ORDER BY {orderBy}
            LIMIT @limit
            """;
        return await db.Database.SqlQueryRaw<ManagerComic>(sql,
            new SqliteParameter { ParameterName = "@comicKind", Value = (int)CollectionKind.Comic },
            new SqliteParameter { ParameterName = "@imagesetKind", Value = (int)CollectionKind.Imageset },
            new SqliteParameter { ParameterName = "@kind", Value = kind is { } filterKind ? (object)(int)filterKind : DBNull.Value },
            new SqliteParameter { ParameterName = "@query", Value = string.IsNullOrWhiteSpace(query) ? DBNull.Value : query },
            new SqliteParameter { ParameterName = "@limit", Value = limit }).ToListAsync(cancellationToken);
    }

    private static bool HasTag(string? cachedTags, string? query)
    {
        if (cachedTags is null || query is null)
            return false;

        var qualified = query.Contains(':');
        foreach (var tag in JsonSerializer.Deserialize<string[]>(cachedTags) ?? [])
        {
            var candidate = qualified ? tag.AsSpan() : tag.AsSpan(tag.IndexOf(':') + 1);
            if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}