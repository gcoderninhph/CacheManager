using Microsoft.AspNetCore.Mvc;
using CacheManager;
using CacheManager.Core;
using Asp.Net.Test.Models;
using System.Text;
using System.Diagnostics;

namespace Asp.Net.Test.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StreamTestController : ControllerBase
{
    private readonly ILogger<StreamTestController> _logger;
    private readonly ICacheStorage _storage;

    public StreamTestController(ILogger<StreamTestController> logger, ICacheStorage storage)
    {
        _logger = logger;
        _storage = storage;
    }

    /// <summary>
    /// Demo streaming keys - memory efficient
    /// GET /api/streamtest/keys?mapName=products
    /// </summary>
    [HttpGet("keys")]
    public async Task<IActionResult> StreamKeys(string mapName = "products")
    {
        var mapInstance = _storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return NotFound(new { error = $"Map '{mapName}' not found" });
        }

        var sw = Stopwatch.StartNew();
        var count = 0;
        var keys = new List<string>();

        try
        {
            // Use generic helper method to handle typed invocation
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var valueType = mapInstance.GetType().GetGenericArguments()[1];
            
            var helperMethod = typeof(StreamTestController)
                .GetMethod(nameof(StreamKeysGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(keyType, valueType);
            
            var result = await (Task<(int, List<string>)>)helperMethod.Invoke(this, new[] { mapInstance })!;
            count = result.Item1;
            keys = result.Item2;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming keys from map {MapName}", mapName);
            return StatusCode(500, new { error = ex.Message });
        }

        sw.Stop();

        return Ok(new
        {
            mapName,
            totalKeys = count,
            elapsedMs = sw.ElapsedMilliseconds,
            sampleKeys = keys.Take(10).ToList(),
            note = "Streaming approach - memory efficient for large datasets"
        });
    }

    private async Task<(int, List<string>)> StreamKeysGeneric<TKey, TValue>(object mapInstance) where TKey : notnull
    {
        var map = (IMap<TKey, TValue>)mapInstance;
        var count = 0;
        var keys = new List<string>();
        
        await map.GetAllKeysAsync(key => 
        {
            keys.Add(key?.ToString() ?? "");
            count++;
        });
        
        return (count, keys);
    }

    /// <summary>
    /// Demo streaming values - memory efficient
    /// GET /api/streamtest/values?mapName=products
    /// </summary>
    [HttpGet("values")]
    public async Task<IActionResult> StreamValues(string mapName = "products")
    {
        var mapInstance = _storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return NotFound(new { error = $"Map '{mapName}' not found" });
        }

        var sw = Stopwatch.StartNew();
        var count = 0;
        var sampleValues = new List<object>();

        try
        {
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var valueType = mapInstance.GetType().GetGenericArguments()[1];
            
            var helperMethod = typeof(StreamTestController)
                .GetMethod(nameof(StreamValuesGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(keyType, valueType);
            
            var result = await (Task<(int, List<object>)>)helperMethod.Invoke(this, new[] { mapInstance })!;
            count = result.Item1;
            sampleValues = result.Item2;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming values from map {MapName}", mapName);
            return StatusCode(500, new { error = ex.Message });
        }

        sw.Stop();

        return Ok(new
        {
            mapName,
            totalValues = count,
            elapsedMs = sw.ElapsedMilliseconds,
            sampleValues,
            note = "Streaming approach - only loaded 5 samples into memory"
        });
    }

    private async Task<(int, List<object>)> StreamValuesGeneric<TKey, TValue>(object mapInstance) where TKey : notnull
    {
        var map = (IMap<TKey, TValue>)mapInstance;
        var count = 0;
        var sampleValues = new List<object>();
        
        await map.GetAllValuesAsync(value => 
        {
            count++;
            if (count <= 5 && value != null)
            {
                sampleValues.Add(value);
            }
        });
        
        return (count, sampleValues);
    }

    /// <summary>
    /// Demo streaming entries - memory efficient
    /// GET /api/streamtest/entries?mapName=products&limit=10
    /// </summary>
    [HttpGet("entries")]
    public async Task<IActionResult> StreamEntries(string mapName = "products", int limit = 10)
    {
        var mapInstance = _storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return NotFound(new { error = $"Map '{mapName}' not found" });
        }

        var sw = Stopwatch.StartNew();
        var count = 0;
        var sampleEntries = new List<object>();

        try
        {
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var valueType = mapInstance.GetType().GetGenericArguments()[1];
            
            var helperMethod = typeof(StreamTestController)
                .GetMethod(nameof(StreamEntriesGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(keyType, valueType);
            
            var result = await (Task<(int, List<object>)>)helperMethod.Invoke(this, new object[] { mapInstance, limit })!;
            count = result.Item1;
            sampleEntries = result.Item2;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming entries from map {MapName}", mapName);
            return StatusCode(500, new { error = ex.Message });
        }

        sw.Stop();

        return Ok(new
        {
            mapName,
            totalEntries = count,
            elapsedMs = sw.ElapsedMilliseconds,
            limit,
            entries = sampleEntries,
            note = $"Streaming approach - only loaded {limit} entries into memory"
        });
    }

    private async Task<(int, List<object>)> StreamEntriesGeneric<TKey, TValue>(object mapInstance, int limit) where TKey : notnull
    {
        var map = (IMap<TKey, TValue>)mapInstance;
        var count = 0;
        var sampleEntries = new List<object>();
        
        await map.GetAllEntriesAsync(entry => 
        {
            count++;
            if (count <= limit)
            {
                sampleEntries.Add(new {
                    key = entry.GetKey(),
                    value = entry.GetValue()
                });
            }
        });
        
        return (count, sampleEntries);
    }

    /// <summary>
    /// Compare memory usage: GetAllEntriesAsync() vs GetAllEntriesAsync(Action)
    /// GET /api/streamtest/compare?mapName=products
    /// </summary>
    [HttpGet("compare")]
    public async Task<IActionResult> CompareMemoryUsage(string mapName = "products")
    {
        var mapInstance = _storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return NotFound(new { error = $"Map '{mapName}' not found" });
        }

        var keyType = mapInstance.GetType().GetGenericArguments()[0];
        var valueType = mapInstance.GetType().GetGenericArguments()[1];

        try
        {
            // Test 1: GetAllEntriesAsync() - Load all into memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var memBefore1 = GC.GetTotalMemory(false);
            var sw1 = Stopwatch.StartNew();

            var loadAllMethod = typeof(StreamTestController)
                .GetMethod(nameof(CompareMemoryUsage_LoadAll), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(keyType, valueType);
            
            var loadAllCount = await (Task<int>)loadAllMethod.Invoke(this, new[] { mapInstance })!;
            
            var memAfter1 = GC.GetTotalMemory(false);
            sw1.Stop();

            var loadAllResult = new
            {
                method = "GetAllEntriesAsync()",
                count = loadAllCount,
                elapsedMs = sw1.ElapsedMilliseconds,
                memoryUsedBytes = memAfter1 - memBefore1,
                memoryUsedMB = (memAfter1 - memBefore1) / 1024.0 / 1024.0,
                note = "Loads all entries into memory at once"
            };

            // Clean up
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Test 2: GetAllEntriesAsync(Action) - Stream
            var memBefore2 = GC.GetTotalMemory(false);
            var sw2 = Stopwatch.StartNew();

            var streamingMethod = typeof(StreamTestController)
                .GetMethod(nameof(CompareMemoryUsage_Streaming), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(keyType, valueType);
            
            var streamingCount = await (Task<int>)streamingMethod.Invoke(this, new[] { mapInstance })!;

            var memAfter2 = GC.GetTotalMemory(false);
            sw2.Stop();

            var streamResult = new
            {
                method = "GetAllEntriesAsync(Action<IEntry>)",
                count = streamingCount,
                elapsedMs = sw2.ElapsedMilliseconds,
                memoryUsedBytes = memAfter2 - memBefore2,
                memoryUsedMB = (memAfter2 - memBefore2) / 1024.0 / 1024.0,
                note = "Streams entries one by one - memory efficient"
            };

            return Ok(new
            {
                mapName,
                comparison = new
                {
                    getAllEntries = loadAllResult,
                    streamEntries = streamResult
                },
                recommendation = "Use GetAllEntriesAsync(Action) for large datasets to avoid OutOfMemoryException"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing memory usage for map {MapName}", mapName);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task<int> CompareMemoryUsage_LoadAll<TKey, TValue>(object mapInstance) where TKey : notnull
    {
        var map = (IMap<TKey, TValue>)mapInstance;
        var entries = await map.GetAllEntriesAsync();
        return entries.Count();
    }

    private async Task<int> CompareMemoryUsage_Streaming<TKey, TValue>(object mapInstance) where TKey : notnull
    {
        var map = (IMap<TKey, TValue>)mapInstance;
        var count = 0;
        await map.GetAllEntriesAsync(_ => { count++; });
        return count;
    }

    /// <summary>
    /// Test all IMap basic operations
    /// GET /api/streamtest/test-basic?mapName=products
    /// </summary>
    [HttpGet("test-basic")]
    public async Task<IActionResult> TestBasicOperations(string mapName = "products")
    {
        var mapInstance = _storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return NotFound(new { error = $"Map '{mapName}' not found" });
        }

        try
        {
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var valueType = mapInstance.GetType().GetGenericArguments()[1];

            var helperMethod = typeof(StreamTestController)
                .GetMethod(nameof(TestBasicOperationsGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(keyType, valueType);
            
            var results = await (Task<Dictionary<string, object>>)helperMethod.Invoke(this, new[] { mapInstance })!;

            return Ok(new
            {
                mapName,
                operations = results,
                note = "All IMap<TKey, TValue> basic operations tested successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing basic operations for map {MapName}", mapName);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task<Dictionary<string, object>> TestBasicOperationsGeneric<TKey, TValue>(object mapInstance) where TKey : notnull
    {
        var map = (IMap<TKey, TValue>)mapInstance;
        var results = new Dictionary<string, object>();

        try
        {
            // Test ContainsKeyAsync
            var testKey = (TKey)Convert.ChangeType(1, typeof(TKey)); // Test with key=1
            var exists = await map.ContainsKeyAsync(testKey);
            results["contains_key_1"] = exists;
        }
        catch (Exception ex)
        {
            results["contains_key_1"] = $"Error: {ex.Message}";
        }

        try
        {
            // Test CountAsync
            var count = await map.CountAsync();
            results["total_count"] = count;
        }
        catch (Exception ex)
        {
            results["total_count"] = $"Error: {ex.Message}";
        }

        try
        {
            // Test GetAllKeysAsync
            var sw = Stopwatch.StartNew();
            var keys = await map.GetAllKeysAsync();
            var keyList = keys.Take(5).Select(k => k?.ToString() ?? "").ToList();
            sw.Stop();
            results["get_all_keys"] = new
            {
                elapsed_ms = sw.ElapsedMilliseconds,
                sample_keys = keyList
            };
        }
        catch (Exception ex)
        {
            results["get_all_keys"] = $"Error: {ex.Message}";
        }

        try
        {
            // Test GetAllValuesAsync
            var sw = Stopwatch.StartNew();
            var values = await map.GetAllValuesAsync();
            var valueList = values.Take(3).ToList();
            sw.Stop();
            results["get_all_values"] = new
            {
                elapsed_ms = sw.ElapsedMilliseconds,
                sample_values = valueList
            };
        }
        catch (Exception ex)
        {
            results["get_all_values"] = $"Error: {ex.Message}";
        }

        return results;
    }
}
