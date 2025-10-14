using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheManager.Core;

/// <summary>
/// Background service to register maps and buckets on application startup
/// </summary>
public abstract class CacheManagerRegistrationService : BackgroundService
{
    private readonly ICacheRegisterService _registerService;
    private readonly ILogger<CacheManagerRegistrationService> _logger;

    protected CacheManagerRegistrationService(
        ICacheRegisterService registerService,
        ILogger<CacheManagerRegistrationService> logger)
    {
        _registerService = registerService;
        _logger = logger;
    }

    /// <summary>
    /// Override this method to configure your maps and buckets
    /// </summary>
    /// <param name="builder">Register builder for creating maps and buckets</param>
    protected abstract void ConfigureCache(IRegisterBuilder builder);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting CacheManager registration...");

            // Execute registration
            var builder = _registerService.RegisterBuilder();
            ConfigureCache(builder);
            builder.Build();

            _logger.LogInformation("CacheManager registration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CacheManager registration");
            throw;
        }

        // Service completes immediately after registration
        await Task.CompletedTask;
    }
}
