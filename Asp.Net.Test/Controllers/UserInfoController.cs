using CacheManager;
using CacheManager.Core;
using Asp.Net.Test.Models;
using Microsoft.AspNetCore.Mvc;

namespace Asp.Net.Test.Controllers;

/// <summary>
/// CRUD operations for UserInfo
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("UserInfo CRUD")]
public class UserInfoController : ControllerBase
{
    private readonly ICacheStorage _storage;

    public UserInfoController(ICacheStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Get a user by userId
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetUserInfo(string userId)
    {
        try
        {
            var map = _storage.GetOrCreateMap<string, UserInfo>("user-info");
            var userInfo = await map.GetValueAsync(userId);
            
            if (userInfo == null)
            {
                return NotFound(new { error = $"User '{userId}' not found" });
            }
            
            return Ok(userInfo);
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Create or update a user
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrUpdateUserInfo([FromBody] UserInfo userInfo)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userInfo.UserId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            var map = _storage.GetOrCreateMap<string, UserInfo>("user-info");
            await map.SetValueAsync(userInfo.UserId, userInfo);
            return Ok(new { message = "User created/updated successfully", userInfo });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Update a user
    /// </summary>
    [HttpPut("{userId}")]
    public async Task<IActionResult> UpdateUserInfo(string userId, [FromBody] UserInfo userInfo)
    {
        try
        {
            var map = _storage.GetOrCreateMap<string, UserInfo>("user-info");
            
            // Check if user exists
            var existingUser = await map.GetValueAsync(userId);
            if (existingUser == null)
            {
                return NotFound(new { error = $"User '{userId}' not found" });
            }

            // Update user
            userInfo.UserId = userId; // Ensure userId matches route
            await map.SetValueAsync(userId, userInfo);
            return Ok(new { message = "User updated successfully", userInfo });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> DeleteUserInfo(string userId)
    {
        try
        {
            // Note: IMap doesn't have RemoveAsync, only ClearAsync
            // For individual deletion, we need to use Redis directly or use a workaround
            // For now, return NotImplemented
            return StatusCode(501, new 
            { 
                error = "Individual user deletion not supported",
                message = "Use DELETE /api/userinfo to clear all users, or implement custom Redis delete"
            });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all users (with pagination)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllUserInfo([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var mapInstance = _storage.GetMapInstance("user-info");
            if (mapInstance == null)
            {
                return NotFound(new { error = "UserInfo map not found" });
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
                        mapName = "user-info",
                        data = pagedResult
                    });
                }
            }
            
            return Problem("Unable to retrieve user data");
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all users
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> ClearAllUserInfo()
    {
        try
        {
            var map = _storage.GetOrCreateMap<string, UserInfo>("user-info");
            await map.ClearAsync();
            return Ok(new { message = "All users cleared successfully" });
        }
        catch (Exception ex)
        {
            return Problem($"Error: {ex.Message}");
        }
    }
}
