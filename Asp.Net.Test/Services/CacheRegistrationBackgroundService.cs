using CacheManager.Core;
using Asp.Net.Test.Models;
using Microsoft.Extensions.Hosting;

namespace Asp.Net.Test.Services;

/// <summary>
/// Background service to initialize maps on application startup
/// </summary>
public class CacheRegistrationBackgroundService : BackgroundService
{
    private readonly ICacheStorage _storage;
    private readonly ILogger<CacheRegistrationBackgroundService> _logger;

    public CacheRegistrationBackgroundService(
        ICacheStorage storage,
        ILogger<CacheRegistrationBackgroundService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting CacheManager initialization...");

            // Initialize maps with GetOrCreateMapAsync
            await _storage.GetOrCreateMapAsync<string, string>("user-sessions");
            await _storage.GetOrCreateMapAsync<int, string>("user-data");
            await _storage.GetOrCreateMapAsync<string, UserInfo>("user-info");
            
            // TTL Test: Temp sessions expire after 2 minutes of inactivity
            await _storage.GetOrCreateMapAsync<string, TempSession>("temp-sessions", TimeSpan.FromMinutes(2));
            
            // Batch Update Test: Products with 5-second batch wait time (default)
            await _storage.GetOrCreateMapAsync<int, Product>("products");

            _logger.LogInformation("CacheManager initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CacheManager initialization");
            throw;
        }
    }
}