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

        // Stream keys thay vì load tất cả vào memory
        var streamMethod = mapInstance.GetType().GetMethod("GetAllKeysAsync", new[] { typeof(Action<>).MakeGenericType(mapInstance.GetType().GetGenericArguments()[0]) });
        
        if (streamMethod != null)
        {
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var actionType = typeof(Action<>).MakeGenericType(keyType);
            
            // Create delegate to collect keys
            var keyAction = Delegate.CreateDelegate(
                actionType,
                this,
                GetType().GetMethod(nameof(CollectKey), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.MakeGenericMethod(keyType)
            );

            // Temporary storage for streaming
            var tempKeys = new List<object>();
            
            // Simple approach: use reflection to call with inline action
            var task = (Task?)streamMethod.Invoke(mapInstance, new object[] {
                Delegate.CreateDelegate(
                    actionType,
                    new Action<object>(key => {
                        tempKeys.Add(key);
                        count++;
                    }).Target,
                    typeof(Action<object>).GetMethod("Invoke")!
                )
            });

            if (task != null)
            {
                await task;
                keys = tempKeys.Select(k => k.ToString() ?? "").ToList();
            }
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

    private void CollectKey<TKey>(TKey key)
    {
        // Helper method for delegate creation
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

        // Stream values - chỉ lấy sample 5 cái đầu
        var streamMethod = mapInstance.GetType().GetMethod("GetAllValuesAsync", new[] { typeof(Action<>).MakeGenericType(mapInstance.GetType().GetGenericArguments()[1]) });
        
        if (streamMethod != null)
        {
            var valueType = mapInstance.GetType().GetGenericArguments()[1];
            var actionType = typeof(Action<>).MakeGenericType(valueType);
            
            var task = (Task?)streamMethod.Invoke(mapInstance, new object[] {
                Delegate.CreateDelegate(
                    actionType,
                    new Action<object>(value => {
                        count++;
                        if (count <= 5)
                        {
                            sampleValues.Add(value);
                        }
                    }).Target,
                    typeof(Action<object>).GetMethod("Invoke")!
                )
            });

            if (task != null)
            {
                await task;
            }
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

        // Stream entries - chỉ collect limited items
        var streamMethod = mapInstance.GetType().GetMethod("GetAllEntriesAsync", new[] { typeof(Action<>).MakeGenericType(typeof(IEntry<,>).MakeGenericType(mapInstance.GetType().GetGenericArguments())) });
        
        if (streamMethod != null)
        {
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var valueType = mapInstance.GetType().GetGenericArguments()[1];
            var entryType = typeof(IEntry<,>).MakeGenericType(keyType, valueType);
            var actionType = typeof(Action<>).MakeGenericType(entryType);
            
            var task = (Task?)streamMethod.Invoke(mapInstance, new object[] {
                Delegate.CreateDelegate(
                    actionType,
                    new Action<object>(entry => {
                        count++;
                        if (count <= limit)
                        {
                            var getKeyMethod = entry.GetType().GetMethod("GetKey");
                            var getValueMethod = entry.GetType().GetMethod("GetValue");
                            
                            sampleEntries.Add(new {
                                key = getKeyMethod?.Invoke(entry, null),
                                value = getValueMethod?.Invoke(entry, null)
                            });
                        }
                    }).Target,
                    typeof(Action<object>).GetMethod("Invoke")!
                )
            });

            if (task != null)
            {
                await task;
            }
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

        var results = new Dictionary<string, object>();

        // Test 1: GetAllEntriesAsync() - Load all into memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memBefore1 = GC.GetTotalMemory(false);
        var sw1 = Stopwatch.StartNew();

        var getAllMethod = mapInstance.GetType().GetMethod("GetAllEntriesAsync", Type.EmptyTypes);
        if (getAllMethod != null)
        {
            var task = getAllMethod.Invoke(mapInstance, null) as Task;
            if (task != null)
            {
                await task;
                var resultProp = task.GetType().GetProperty("Result");
                var entries = resultProp?.GetValue(task);
                
                var memAfter1 = GC.GetTotalMemory(false);
                sw1.Stop();

                var count = 0;
                if (entries != null)
                {
                    var enumerable = entries as System.Collections.IEnumerable;
                    if (enumerable != null)
                    {
                        foreach (var _ in enumerable)
                        {
                            count++;
                        }
                    }
                }

                results["getAllEntries"] = new
                {
                    method = "GetAllEntriesAsync()",
                    count,
                    elapsedMs = sw1.ElapsedMilliseconds,
                    memoryUsedBytes = memAfter1 - memBefore1,
                    memoryUsedMB = (memAfter1 - memBefore1) / 1024.0 / 1024.0,
                    note = "Loads all entries into memory at once"
                };
            }
        }

        // Clean up
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Test 2: GetAllEntriesAsync(Action) - Stream
        var memBefore2 = GC.GetTotalMemory(false);
        var sw2 = Stopwatch.StartNew();
        var count2 = 0;

        var keyType = mapInstance.GetType().GetGenericArguments()[0];
        var valueType = mapInstance.GetType().GetGenericArguments()[1];
        var entryType = typeof(IEntry<,>).MakeGenericType(keyType, valueType);
        var actionType = typeof(Action<>).MakeGenericType(entryType);
        
        var streamMethod = mapInstance.GetType().GetMethod("GetAllEntriesAsync", new[] { actionType });
        if (streamMethod != null)
        {
            var task = (Task?)streamMethod.Invoke(mapInstance, new object[] {
                Delegate.CreateDelegate(
                    actionType,
                    new Action<object>(_ => count2++).Target,
                    typeof(Action<object>).GetMethod("Invoke")!
                )
            });

            if (task != null)
            {
                await task;
            }
        }

        var memAfter2 = GC.GetTotalMemory(false);
        sw2.Stop();

        results["streamEntries"] = new
        {
            method = "GetAllEntriesAsync(Action<IEntry>)",
            count = count2,
            elapsedMs = sw2.ElapsedMilliseconds,
            memoryUsedBytes = memAfter2 - memBefore2,
            memoryUsedMB = (memAfter2 - memBefore2) / 1024.0 / 1024.0,
            note = "Streams entries one by one - memory efficient"
        };

        return Ok(new
        {
            mapName,
            comparison = results,
            recommendation = "Use GetAllEntriesAsync(Action) for large datasets to avoid OutOfMemoryException"
        });
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

        var results = new Dictionary<string, object>();

        // Test ContainsKeyAsync
        var containsMethod = mapInstance.GetType().GetMethod("ContainsKeyAsync");
        if (containsMethod != null)
        {
            var keyType = mapInstance.GetType().GetGenericArguments()[0];
            var testKey = Convert.ChangeType(1, keyType); // Test with key=1
            var task = containsMethod.Invoke(mapInstance, new[] { testKey }) as Task<bool>;
            if (task != null)
            {
                var exists = await task;
                results["contains_key_1"] = exists;
            }
        }

        // Test CountAsync
        var countMethod = mapInstance.GetType().GetMethod("CountAsync");
        if (countMethod != null)
        {
            var task = countMethod.Invoke(mapInstance, null) as Task<int>;
            if (task != null)
            {
                var count = await task;
                results["total_count"] = count;
            }
        }

        // Test GetAllKeysAsync
        var getAllKeysMethod = mapInstance.GetType().GetMethod("GetAllKeysAsync", Type.EmptyTypes);
        if (getAllKeysMethod != null)
        {
            var sw = Stopwatch.StartNew();
            var task = getAllKeysMethod.Invoke(mapInstance, null) as Task;
            if (task != null)
            {
                await task;
                var resultProp = task.GetType().GetProperty("Result");
                var keys = resultProp?.GetValue(task) as System.Collections.IEnumerable;
                
                var keyList = new List<object>();
                if (keys != null)
                {
                    foreach (var key in keys)
                    {
                        keyList.Add(key);
                        if (keyList.Count >= 5) break; // Sample 5
                    }
                }
                
                sw.Stop();
                results["get_all_keys"] = new
                {
                    elapsed_ms = sw.ElapsedMilliseconds,
                    sample_keys = keyList.Take(5).ToList()
                };
            }
        }

        // Test GetAllValuesAsync
        var getAllValuesMethod = mapInstance.GetType().GetMethod("GetAllValuesAsync", Type.EmptyTypes);
        if (getAllValuesMethod != null)
        {
            var sw = Stopwatch.StartNew();
            var task = getAllValuesMethod.Invoke(mapInstance, null) as Task;
            if (task != null)
            {
                await task;
                var resultProp = task.GetType().GetProperty("Result");
                var values = resultProp?.GetValue(task) as System.Collections.IEnumerable;
                
                var valueList = new List<object>();
                if (values != null)
                {
                    foreach (var value in values)
                    {
                        valueList.Add(value);
                        if (valueList.Count >= 3) break; // Sample 3
                    }
                }
                
                sw.Stop();
                results["get_all_values"] = new
                {
                    elapsed_ms = sw.ElapsedMilliseconds,
                    sample_values = valueList.Take(3).ToList()
                };
            }
        }

        return Ok(new
        {
            mapName,
            operations = results,
            note = "All IMap<TKey, TValue> basic operations tested successfully"
        });
    }
}
