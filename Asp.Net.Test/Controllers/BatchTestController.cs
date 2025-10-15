using CacheManager.Core;
using Asp.Net.Test.Models;
using Microsoft.AspNetCore.Mvc;

namespace Asp.Net.Test.Controllers;

/// <summary>
/// Controller để test tính năng Batch Update
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Batch Update Test")]
public class BatchTestController : ControllerBase
{
    private readonly ICacheStorage _storage;
    private readonly ILogger<BatchTestController> _logger;

    public BatchTestController(ICacheStorage storage, ILogger<BatchTestController> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Get all products with pagination
    /// </summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var mapInstance = _storage.GetMapInstance("products");
        if (mapInstance == null)
        {
            return NotFound(new { error = "Products map not found" });
        }

        var method = mapInstance.GetType().GetMethod("GetEntriesPagedAsync");
        if (method != null)
        {
            var task = method.Invoke(mapInstance, new object[] { page, pageSize, null! }) as Task;
            if (task != null)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                var pagedResult = resultProperty?.GetValue(task);

                return Ok(new
                {
                    mapName = "products",
                    data = pagedResult,
                    info = new
                    {
                        totalProducts = 100,
                        autoUpdate = "5 random products every minute",
                        batchWaitTime = "3 seconds",
                        note = "Check server logs to see batch update events"
                    }
                });
            }
        }

        return Problem("Unable to retrieve products");
    }

    /// <summary>
    /// Get product by ID
    /// </summary>
    [HttpGet("products/{productId}")]
    public async Task<IActionResult> GetProduct(int productId)
    {
        try
        {
            var productsMap = await _storage.GetOrCreateMapAsync<int, Product>("products");
            var product = await productsMap.GetValueAsync(productId);

            return Ok(product);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Product {productId} not found" });
        }
    }

    /// <summary>
    /// Manually update products (to test batch)
    /// </summary>
    [HttpPost("update-products")]
    public async Task<IActionResult> UpdateProducts([FromQuery] int count = 5)
    {
        var productsMap = await _storage.GetOrCreateMapAsync<int, Product>("products");
        var random = new Random();

        // Select random product IDs
        var randomIds = Enumerable.Range(1, 100)
            .OrderBy(_ => random.Next())
            .Take(count)
            .ToList();

        var updated = new List<Product>();

        foreach (var productId in randomIds)
        {
            try
            {
                var product = await productsMap.GetValueAsync(productId);

                // Update product
                product.Price = (decimal)(random.NextDouble() * 100 + 10);
                product.Stock = random.Next(0, 1000);
                product.LastUpdated = DateTime.UtcNow;
                product.UpdateCount++;

                await productsMap.SetValueAsync(productId, product);
                updated.Add(product);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning($"Product {productId} not found");
            }
        }

        return Ok(new
        {
            message = $"Updated {updated.Count} products. Check server logs for batch update event.",
            updatedProducts = updated,
            info = new
            {
                batchWaitTime = "5 seconds (default)",
                note = "Batch update will be triggered after 5 seconds of inactivity"
            }
        });
    }

    /// <summary>
    /// Get products sorted by update count (most updated first)
    /// </summary>
    [HttpGet("products/top-updated")]
    public async Task<IActionResult> GetTopUpdatedProducts([FromQuery] int top = 10)
    {
        var mapInstance = _storage.GetMapInstance("products");
        if (mapInstance == null)
        {
            return NotFound(new { error = "Products map not found" });
        }

        var method = mapInstance.GetType().GetMethod("GetAllEntriesForDashboardAsync");
        if (method != null)
        {
            var task = method.Invoke(mapInstance, null) as Task;
            if (task != null)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                var allEntries = resultProperty?.GetValue(task) as IEnumerable<object>;

                if (allEntries != null)
                {
                    var products = new List<Product>();

                    foreach (var entry in allEntries)
                    {
                        var valueProperty = entry.GetType().GetProperty("Value");
                        var valueJson = valueProperty?.GetValue(entry) as string;

                        if (!string.IsNullOrEmpty(valueJson))
                        {
                            var product = System.Text.Json.JsonSerializer.Deserialize<Product>(valueJson);
                            if (product != null)
                            {
                                products.Add(product);
                            }
                        }
                    }

                    var topProducts = products
                        .OrderByDescending(p => p.UpdateCount)
                        .Take(top)
                        .ToList();

                    return Ok(new
                    {
                        topUpdatedProducts = topProducts,
                        info = new
                        {
                            note = "Products with highest update count",
                            autoUpdate = "5 random products every minute"
                        }
                    });
                }
            }
        }

        return Problem("Unable to retrieve products");
    }
}
