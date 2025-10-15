using CacheManager.Core;
using Asp.Net.Test.Models;

namespace Asp.Net.Test.Services;

/// <summary>
/// Background service để test Batch Update
/// - Tạo 100 products ban đầu
/// - Mỗi phút update ngẫu nhiên 5 products
/// - Log các batch updates
/// </summary>
public class ProductUpdateBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductUpdateBackgroundService> _logger;
    private readonly Random _random = new();

    public ProductUpdateBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ProductUpdateBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for cache registration to complete
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        using var scope = _serviceProvider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ICacheStorage>();

        // Get products map
        var productsMap = storage.GetOrCreateMap<int, Product>("products");

        // Setup batch update listener
        productsMap.OnBatchUpdate(entries =>
        {
            var entryList = entries.ToList();
            _logger.LogInformation("=== BATCH UPDATE TRIGGERED ===");
            _logger.LogInformation($"Total items in batch: {entryList.Count}");
            
            foreach (var entry in entryList)
            {
                var product = entry.GetValue();
                _logger.LogInformation(
                    $"  → Product #{product.ProductId}: {product.Name} | " +
                    $"Price: ${product.Price:F2} | Stock: {product.Stock} | " +
                    $"Updates: {product.UpdateCount}"
                );
            }
            
            _logger.LogInformation("==============================\n");
        });

        // Initialize 100 products
        _logger.LogInformation("Initializing 100 products...");
        for (int i = 1; i <= 100; i++)
        {
            var product = new Product
            {
                ProductId = i,
                Name = $"Product {i}",
                Price = (decimal)(_random.NextDouble() * 100 + 10),
                Stock = _random.Next(0, 1000),
                LastUpdated = DateTime.UtcNow,
                UpdateCount = 0
            };
            
            await productsMap.SetValueAsync(i, product);
        }
        _logger.LogInformation("✅ 100 products initialized successfully\n");

        // Update 5 random products every minute
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                _logger.LogInformation("🔄 Starting random product updates...");
                
                // Select 5 random product IDs
                var randomIds = Enumerable.Range(1, 100)
                    .OrderBy(_ => _random.Next())
                    .Take(5)
                    .ToList();

                foreach (var productId in randomIds)
                {
                    try
                    {
                        var product = await productsMap.GetValueAsync(productId);
                        
                        // Update product data
                        product.Price = (decimal)(_random.NextDouble() * 100 + 10);
                        product.Stock = _random.Next(0, 1000);
                        product.LastUpdated = DateTime.UtcNow;
                        product.UpdateCount++;

                        await productsMap.SetValueAsync(productId, product);
                        
                        _logger.LogInformation(
                            $"  Updated Product #{productId}: {product.Name} | " +
                            $"New Price: ${product.Price:F2} | New Stock: {product.Stock}"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating product {productId}");
                    }
                }

                _logger.LogInformation($"✅ Updated {randomIds.Count} products. Waiting 3 seconds for batch...\n");
                
                // Wait a bit for batch to trigger (batch wait time is 5 seconds by default)
                await Task.Delay(TimeSpan.FromSeconds(6), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in product update loop");
            }
        }

        _logger.LogInformation("Product update service stopped");
    }
}
