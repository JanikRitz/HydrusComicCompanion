using System.Globalization;
using System.Text.Json.Nodes;
using HydrusComicCompanion.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace HydrusComicCompanion.Services;

public sealed class HydrusSettingsService(
    IOptions<HydrusSettings> defaults,
    IWebHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtectionProvider) : IHydrusSettingsService
{
    private const string TableName = "AppSettings";
    private const string ProtectedApiAccessKeySettingName = "ApiAccessKeyProtected";
    private const string ApiAccessKeyProtectorPurpose = "HydrusComicCompanion.Settings.ApiAccessKey.v1";

    private readonly HydrusSettings _defaults = defaults.Value.Clone();
    private readonly string _connectionString = BuildConnectionString(environment.ContentRootPath);
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IDataProtector _apiAccessKeyProtector = dataProtectionProvider.CreateProtector(ApiAccessKeyProtectorPurpose);

    public async Task<HydrusSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var settings = _defaults.Clone();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Key, Value FROM {TableName}";

        var protectedApiKeyLoaded = false;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);

            if (string.Equals(key, ProtectedApiAccessKeySettingName, StringComparison.Ordinal))
            {
                if (TryUnprotectApiAccessKey(value, out var apiAccessKey))
                {
                    settings.ApiAccessKey = apiAccessKey;
                    protectedApiKeyLoaded = true;
                }

                continue;
            }

            if (string.Equals(key, nameof(HydrusSettings.ApiAccessKey), StringComparison.Ordinal))
            {
                if (!protectedApiKeyLoaded)
                {
                    settings.ApiAccessKey = value;
                }

                continue;
            }

            ApplySetting(settings, key, value);
        }

        return settings;
    }

    public async Task SaveSettingsAsync(HydrusSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var normalized = Normalize(settings);
        var values = new Dictionary<string, string>
        {
            [nameof(HydrusSettings.ApiUrl)] = normalized.ApiUrl,
            [ProtectedApiAccessKeySettingName] = ProtectApiAccessKey(normalized.ApiAccessKey),
            [nameof(HydrusSettings.PrimaryTagService)] = normalized.PrimaryTagService,
            [nameof(HydrusSettings.TargetFileDomain)] = normalized.TargetFileDomain,
            [nameof(HydrusSettings.SeriesNamespace)] = normalized.SeriesNamespace,
            [nameof(HydrusSettings.VolumeNamespace)] = normalized.VolumeNamespace,
            [nameof(HydrusSettings.ChapterNamespace)] = normalized.ChapterNamespace,
            [nameof(HydrusSettings.PageNamespace)] = normalized.PageNamespace,
            [nameof(HydrusSettings.BackgroundSyncIntervalMinutes)] = normalized.BackgroundSyncIntervalMinutes.ToString(CultureInfo.InvariantCulture)
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (transaction)
        {
            foreach (var pair in values)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO {TableName}(Key, Value)
                    VALUES(@key, @value)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                    """;
                command.Parameters.AddWithValue("@key", pair.Key);
                command.Parameters.AddWithValue("@value", pair.Value);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var deleteLegacyCommand = connection.CreateCommand();
            deleteLegacyCommand.Transaction = transaction;
            deleteLegacyCommand.CommandText = $"DELETE FROM {TableName} WHERE Key = @legacyKey";
            deleteLegacyCommand.Parameters.AddWithValue("@legacyKey", nameof(HydrusSettings.ApiAccessKey));
            await deleteLegacyCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<HydrusServiceCatalog> GetServicesAsync(HydrusSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{normalized.ApiUrl.TrimEnd('/')}/get_services");

        if (!string.IsNullOrWhiteSpace(normalized.ApiAccessKey))
        {
            request.Headers.Add("Hydrus-Client-API-Access-Key", normalized.ApiAccessKey);
        }

        using var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rootNode = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (rootNode is null)
        {
            return new HydrusServiceCatalog();
        }

        return ParseServiceCatalog(rootNode);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {TableName} (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ApplySetting(HydrusSettings settings, string key, string value)
    {
        switch (key)
        {
            case nameof(HydrusSettings.ApiUrl):
                settings.ApiUrl = value;
                break;
            case nameof(HydrusSettings.PrimaryTagService):
                settings.PrimaryTagService = value;
                break;
            case nameof(HydrusSettings.TargetFileDomain):
                settings.TargetFileDomain = value;
                break;
            case nameof(HydrusSettings.SeriesNamespace):
                settings.SeriesNamespace = value;
                break;
            case nameof(HydrusSettings.VolumeNamespace):
                settings.VolumeNamespace = value;
                break;
            case nameof(HydrusSettings.ChapterNamespace):
                settings.ChapterNamespace = value;
                break;
            case nameof(HydrusSettings.PageNamespace):
                settings.PageNamespace = value;
                break;
            case nameof(HydrusSettings.BackgroundSyncIntervalMinutes):
                if (int.TryParse(value, out var minutes))
                {
                    settings.BackgroundSyncIntervalMinutes = minutes;
                }
                break;
        }
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
        normalized.TargetFileDomain = normalized.TargetFileDomain.Trim();
        normalized.SeriesNamespace = NormalizeNamespace(normalized.SeriesNamespace, "series:");
        normalized.VolumeNamespace = NormalizeNamespace(normalized.VolumeNamespace, "volume:");
        normalized.ChapterNamespace = NormalizeNamespace(normalized.ChapterNamespace, "chapter:");
        normalized.PageNamespace = NormalizeNamespace(normalized.PageNamespace, "page:");
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

    private static string BuildConnectionString(string contentRootPath)
    {
        var dataDirectory = Path.Combine(contentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);

        var databasePath = Path.Combine(dataDirectory, "settings.db");
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }
}
