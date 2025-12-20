using CacheManager;
using CacheManager.Core;
using Asp.Net.Test.Models;
using Microsoft.AspNetCore.Mvc;

namespace Asp.Net.Test.Controllers;

/// <summary>
/// Test data generation endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Testing")]
public class TestController : ControllerBase
{
    private readonly ICacheStorage _storage;

    public TestController(ICacheStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Add sample test data to maps
    /// </summary>
    [HttpGet("add-data")]
    public async Task<IActionResult> AddTestData()
    {
        var userSessions = _storage.GetOrCreateMap<string, string>("user-sessions");
        
        // Add more than 20 records to test pagination
        for (int i = 1; i <= 50; i++)
        {
            await userSessions.SetValueAsync($"user{i}", $"session-token-{Guid.NewGuid().ToString().Substring(0, 8)}");
        }
        
        var userData = _storage.GetOrCreateMap<int, string>("user-data");
        for (int i = 1; i <= 30; i++)
        {
            await userData.SetValueAsync(i, $"{{\"name\":\"User{i}\",\"email\":\"user{i}@example.com\"}}");
        }
        
        // Add UserInfo test data
        var userInfoMap = _storage.GetOrCreateMap<string, UserInfo>("user-info");
        var names = new[] { "Alice", "Bob", "Charlie", "David", "Eve", "Frank", "Grace", "Henry", "Ivy", "Jack" };
        for (int i = 1; i <= 25; i++)
        {
            await userInfoMap.SetValueAsync($"user-{i:000}", new UserInfo
            {
                UserId = $"user-{i:000}",
                Name = names[(i - 1) % names.Length] + i,
                Age = 20 + (i % 50)
            });
        }
        
        return Ok(new 
        { 
            success = true, 
            message = "Test data added: 50 user-sessions, 30 user-data, 25 user-info!" 
        });
    }

    /// <summary>
    /// Get all map names (to verify __meta keys are filtered)
    /// </summary>
    [HttpGet("list-maps")]
    public IActionResult ListMaps()
    {
        var mapNames = _storage.GetAllMapNames().ToList();
        var hasMetaKeys = mapNames.Any(name => name.Contains("__meta:"));
        
        return Ok(new 
        { 
            success = !hasMetaKeys,
            mapCount = mapNames.Count,
            maps = mapNames,
            hasMetadataKeys = hasMetaKeys,
            message = hasMetaKeys 
                ? "❌ ERROR: Metadata keys visible!" 
                : "✅ SUCCESS: Metadata keys filtered correctly"
        });
    }

    /// <summary>
    /// Set TTL for a map (to test Redis TTL storage)
    /// </summary>
    [HttpGet("set-map-ttl")]
    public Task<IActionResult> SetMapTtl([FromQuery] string mapName, [FromQuery] int ttlMinutes)
    {
        try
        {
            var map = _storage.GetOrCreateMap<string, string>(mapName);
            map.SetItemExpiration(TimeSpan.FromMinutes(ttlMinutes));
            
            return Task.FromResult<IActionResult>(Ok(new 
            { 
                success = true,
                message = $"✅ TTL set to {ttlMinutes} minutes for map '{mapName}'. TTL config stored in Redis: map:{mapName}:__meta:ttl-config"
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult<IActionResult>(BadRequest(new 
            { 
                success = false,
                message = $"❌ Error: {ex.Message}"
            }));
        }
    }
}
