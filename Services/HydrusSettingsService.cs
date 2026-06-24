using System.Text.Json.Nodes;
using HydrusComicCompanion.Data;
using HydrusComicCompanion.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HydrusComicCompanion.Services;

public sealed class HydrusSettingsService(
    IOptions<HydrusSettings> defaults,
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtectionProvider,
    IDbContextFactory<SettingsDbContext> dbContextFactory) : IHydrusSettingsService
{
    private const string ApiAccessKeyProtectorPurpose = "HydrusComicCompanion.Settings.ApiAccessKey.v1";

    private readonly HydrusSettings _defaults = defaults.Value.Clone();
    private readonly IDataProtector _apiAccessKeyProtector = dataProtectionProvider.CreateProtector(ApiAccessKeyProtectorPurpose);

    public async Task<HydrusSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _defaults.Clone();
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await context.HydrusSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == HydrusSettingsRecord.SingletonId, cancellationToken);

        if (stored is null)
        {
            return settings;
        }

        settings.ApiUrl = stored.ApiUrl;
        settings.PrimaryTagService = stored.PrimaryTagService;
        settings.TagServiceKey = stored.TagServiceKey;
        settings.TargetFileDomain = stored.TargetFileDomain;
        settings.TitleNamespace = stored.TitleNamespace;
        settings.VolumeNamespace = stored.VolumeNamespace;
        settings.ChapterNamespace = stored.ChapterNamespace;
        settings.PageNamespace = stored.PageNamespace;
        settings.CoverPageTag = stored.CoverPageTag;
        settings.BackgroundSyncIntervalMinutes = stored.BackgroundSyncIntervalMinutes;

        if (TryUnprotectApiAccessKey(stored.ProtectedApiAccessKey, out var apiAccessKey))
        {
            settings.ApiAccessKey = apiAccessKey;
        }

        return settings;
    }

    public async Task SaveSettingsAsync(HydrusSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await context.HydrusSettings
            .SingleOrDefaultAsync(x => x.Id == HydrusSettingsRecord.SingletonId, cancellationToken);

        if (stored is null)
        {
            stored = new HydrusSettingsRecord { Id = HydrusSettingsRecord.SingletonId };
            context.HydrusSettings.Add(stored);
        }

        stored.ApiUrl = normalized.ApiUrl;
        stored.ProtectedApiAccessKey = ProtectApiAccessKey(normalized.ApiAccessKey);
        stored.PrimaryTagService = normalized.PrimaryTagService;
        stored.TagServiceKey = normalized.TagServiceKey;
        stored.TargetFileDomain = normalized.TargetFileDomain;
        stored.TitleNamespace = normalized.TitleNamespace;
        stored.VolumeNamespace = normalized.VolumeNamespace;
        stored.ChapterNamespace = normalized.ChapterNamespace;
        stored.PageNamespace = normalized.PageNamespace;
        stored.CoverPageTag = normalized.CoverPageTag;
        stored.BackgroundSyncIntervalMinutes = normalized.BackgroundSyncIntervalMinutes;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<HydrusServiceCatalog> GetServicesAsync(HydrusSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{normalized.ApiUrl.TrimEnd('/')}/get_services");

        if (!string.IsNullOrWhiteSpace(normalized.ApiAccessKey))
        {
            request.Headers.Add("Hydrus-Client-API-Access-Key", normalized.ApiAccessKey);
        }

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rootNode = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (rootNode is null)
        {
            return new HydrusServiceCatalog();
        }

        return ParseServiceCatalog(rootNode);
    }

    private string ProtectApiAccessKey(string value)
    {
        return _apiAccessKeyProtector.Protect(value);
    }

    private bool TryUnprotectApiAccessKey(string protectedValue, out string value)
    {
        try
        {
            value = _apiAccessKeyProtector.Unprotect(protectedValue);
            return true;
        }
        catch
        {
            value = string.Empty;
            return false;
        }
    }

    private static HydrusSettings Normalize(HydrusSettings settings)
    {
        var normalized = settings.Clone();

        normalized.ApiUrl = NormalizeUrl(normalized.ApiUrl);
        normalized.ApiAccessKey = normalized.ApiAccessKey.Trim();
        normalized.PrimaryTagService = normalized.PrimaryTagService.Trim();
        normalized.TagServiceKey = normalized.TagServiceKey.Trim();
        normalized.TargetFileDomain = normalized.TargetFileDomain.Trim();
        normalized.TitleNamespace = NormalizeTitleNamespace(normalized);
        normalized.VolumeNamespace = NormalizeNamespace(normalized.VolumeNamespace, "volume:");
        normalized.ChapterNamespace = NormalizeNamespace(normalized.ChapterNamespace, "chapter:");
        normalized.PageNamespace = NormalizeNamespace(normalized.PageNamespace, "page:");
        normalized.CoverPageTag = NormalizeCoverPageTag(normalized.CoverPageTag, "meta:cover page");
        normalized.BackgroundSyncIntervalMinutes = Math.Max(0, normalized.BackgroundSyncIntervalMinutes);

        return normalized;
    }

    private static string NormalizeNamespace(string value, string fallback)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        return trimmed.EndsWith(':') ? trimmed : $"{trimmed}:";
    }

    private static string NormalizeCoverPageTag(string value, string fallback)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string NormalizeTitleNamespace(HydrusSettings settings)
    {
        var normalizedTitle = NormalizeNamespace(settings.TitleNamespace, "title:");
        if (!string.Equals(normalizedTitle, "title:", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedTitle;
        }

        var legacyTitle = NormalizeNamespace(settings.SeriesNamespace, "");
        return string.IsNullOrWhiteSpace(legacyTitle) ? normalizedTitle : legacyTitle;
    }

    private static string NormalizeUrl(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "http://127.0.0.1:45869";
        }

        return trimmed.TrimEnd('/');
    }

    private static HydrusServiceCatalog ParseServiceCatalog(JsonObject root)
    {
        var tagServices = new Dictionary<string, HydrusServiceOption>(StringComparer.OrdinalIgnoreCase);
        var fileServices = new Dictionary<string, HydrusServiceOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root)
        {
            if (property.Value is not JsonArray array)
            {
                continue;
            }

            foreach (var node in array)
            {
                if (node is not JsonObject serviceObject)
                {
                    continue;
                }

                var name = serviceObject["name"]?.GetValue<string>()?.Trim();
                var key = serviceObject["service_key"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var option = new HydrusServiceOption
                {
                    Name = name,
                    Key = key ?? string.Empty
                };

                var propertyName = property.Key;
                if (propertyName.Contains("tag", StringComparison.OrdinalIgnoreCase))
                {
                    tagServices[name] = option;
                    continue;
                }

                if (propertyName.Contains("file", StringComparison.OrdinalIgnoreCase)
                    || propertyName.Contains("local", StringComparison.OrdinalIgnoreCase)
                    || propertyName.Contains("trash", StringComparison.OrdinalIgnoreCase))
                {
                    fileServices[name] = option;
                }
            }
        }

        return new HydrusServiceCatalog
        {
            TagServices = tagServices.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            FileServices = fileServices.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
