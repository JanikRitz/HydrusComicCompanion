using System.Globalization;

namespace HydrusComicCompanion.Services;

public sealed class ManagerComicSync(
    IServiceScopeFactory scopeFactory, ILogger<ManagerComicSync> logger, TimeSpan interval) : BackgroundService
{
    public static TimeSpan? ParseInterval(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes)
            : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<IHydrusSyncService>();
                await sync.SyncLibraryAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Scheduled Hydrus catalog sync failed; will retry after the configured interval.");
            }

            var remaining = interval;
            while (remaining > TimeSpan.Zero)
            {
                var delay = remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining;
                await Task.Delay(delay, stoppingToken);
                remaining -= delay;
            }
        }
    }
}