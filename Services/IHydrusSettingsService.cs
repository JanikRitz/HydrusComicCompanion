using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services;

public interface IHydrusSettingsService
{
    Task<HydrusSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(HydrusSettings settings, CancellationToken cancellationToken = default);

    Task<HydrusServiceCatalog> GetServicesAsync(HydrusSettings settings, CancellationToken cancellationToken = default);
}
