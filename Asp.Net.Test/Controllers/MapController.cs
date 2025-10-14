using CacheManager;
using CacheManager.Core;
using Microsoft.AspNetCore.Mvc;

namespace Asp.Net.Test.Controllers;

/// <summary>
/// CRUD operations for generic Map
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Map CRUD")]
public class MapController : ControllerBase
{
    private readonly ICacheStorage _storage;

    public MapController(ICacheStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Get a value from map by key
    /// </summary>
    [HttpGet("{mapName}/{key}")]
    public async Task<IActionResult> GetMapValue(string mapName, string key)
    {
        try
        {
            var map = _storage.GetMap<string, string>(mapName);
            var value = await map.GetValueAsync(key);
            return Ok(new { key, value });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Key '{key}' not found in map '{mapName}'" });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set a value in map
    /// </summary>
    [HttpPost("{mapName}/{key}")]
    public async Task<IActionResult> SetMapValue(string mapName, string key, [FromBody] string value)
    {
        try
        {
            var map = _storage.GetMap<string, string>(mapName);
            await map.SetValueAsync(key, value);
            return Ok(new { message = "Value set successfully", key, value });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Update a value in map (same as Set)
    /// </summary>
    [HttpPut("{mapName}/{key}")]
    public async Task<IActionResult> UpdateMapValue(string mapName, string key, [FromBody] string value)
    {
        try
        {
            var map = _storage.GetMap<string, string>(mapName);
            await map.SetValueAsync(key, value);
            return Ok(new { message = "Value updated successfully", key, value });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete all data in map
    /// </summary>
    [HttpDelete("{mapName}")]
    public async Task<IActionResult> ClearMap(string mapName)
    {
        try
        {
            var map = _storage.GetMap<string, string>(mapName);
            await map.ClearAsync();
            return Ok(new { message = $"Map '{mapName}' cleared successfully" });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all values from map (with pagination)
    /// </summary>
    [HttpGet("{mapName}")]
    public async Task<IActionResult> GetAllMapValues(string mapName, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var mapInstance = _storage.GetMapInstance(mapName);
            if (mapInstance == null)
            {
                return NotFound(new { error = $"Map '{mapName}' not found" });
            }

            var method = mapInstance.GetType().GetMethod("GetAllEntriesAsync");
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
                        var entriesList = allEntries.ToList();
                        var totalCount = entriesList.Count;
                        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                        
                        var pagedEntries = entriesList
                            .Skip((page - 1) * pageSize)
                            .Take(pageSize)
                            .ToList();
                        
                        return Ok(new 
                        { 
                            mapName,
                            entries = pagedEntries,
                            pagination = new
                            {
                                currentPage = page,
                                pageSize,
                                totalCount,
                                totalPages
                            }
                        });
                    }
                }
            }
            
            return Problem("Unable to retrieve map data");
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }
}
