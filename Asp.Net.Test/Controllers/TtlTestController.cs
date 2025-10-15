using CacheManager.Core;
using Asp.Net.Test.Models;
using Microsoft.AspNetCore.Mvc;

namespace Asp.Net.Test.Controllers;

/// <summary>
/// Controller để test tính năng TTL (Time To Live)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("TTL Test")]
public class TtlTestController : ControllerBase
{
    private readonly ICacheStorage _storage;
    private readonly ILogger<TtlTestController> _logger;

    public TtlTestController(ICacheStorage storage, ILogger<TtlTestController> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Create test sessions with TTL (expires after 2 minutes of inactivity)
    /// </summary>
    [HttpPost("create-sessions")]
    public async Task<IActionResult> CreateTestSessions([FromQuery] int count = 10)
    {
        var sessionsMap = await _storage.GetOrCreateMapAsync<string, TempSession>("temp-sessions");

        // Setup expiration listener
        sessionsMap.OnExpired((sessionId, session) =>
        {
            _logger.LogWarning(
                $"⏰ SESSION EXPIRED: {sessionId} | " +
                $"User: {session.UserId} | " +
                $"Created: {session.CreatedAt:HH:mm:ss} | " +
                $"Last Access: {session.LastAccessAt:HH:mm:ss}"
            );
        });

        var created = new List<TempSession>();

        for (int i = 1; i <= count; i++)
        {
            var sessionId = $"sess-{Guid.NewGuid():N}";
            var session = new TempSession
            {
                SessionId = sessionId,
                UserId = $"user-{i:000}",
                CreatedAt = DateTime.UtcNow,
                LastAccessAt = DateTime.UtcNow,
                IpAddress = $"192.168.1.{Random.Shared.Next(1, 255)}",
                UserAgent = "Test Browser"
            };

            await sessionsMap.SetValueAsync(sessionId, session);
            created.Add(session);
        }

        return Ok(new
        {
            message = $"Created {count} test sessions. They will expire after 2 minutes of inactivity.",
            sessions = created,
            instructions = new
            {
                access = "Use GET /api/ttltest/access-session/{sessionId} to reset TTL",
                check = "Use GET /api/ttltest/sessions to see active sessions",
                note = "Sessions will auto-delete after 2 minutes without access"
            }
        });
    }

    /// <summary>
    /// Access a session (resets TTL countdown)
    /// </summary>
    [HttpGet("access-session/{sessionId}")]
    public async Task<IActionResult> AccessSession(string sessionId)
    {
        try
        {
            var sessionsMap = await _storage.GetOrCreateMapAsync<string, TempSession>("temp-sessions");
            var session = await sessionsMap.GetValueAsync(sessionId);

            // Update last access time
            session.LastAccessAt = DateTime.UtcNow;
            await sessionsMap.SetValueAsync(sessionId, session);

            return Ok(new
            {
                message = "Session accessed successfully. TTL reset to 2 minutes.",
                session,
                ttlInfo = new
                {
                    resetAt = DateTime.UtcNow,
                    willExpireAt = DateTime.UtcNow.AddMinutes(2),
                    note = "If no access for 2 minutes, session will be deleted"
                }
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                error = $"Session '{sessionId}' not found or already expired"
            });
        }
    }

    /// <summary>
    /// Get all active sessions
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetActiveSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var mapInstance = _storage.GetMapInstance("temp-sessions");
        if (mapInstance == null)
        {
            return NotFound(new { error = "Sessions map not found" });
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
                    mapName = "temp-sessions",
                    data = pagedResult,
                    info = new
                    {
                        ttl = "2 minutes",
                        note = "Sessions shown here will expire after 2 minutes of inactivity"
                    }
                });
            }
        }

        return Problem("Unable to retrieve sessions");
    }

    /// <summary>
    /// Clear all test sessions
    /// </summary>
    [HttpDelete("sessions")]
    public async Task<IActionResult> ClearSessions()
    {
        var sessionsMap = await _storage.GetOrCreateMapAsync<string, TempSession>("temp-sessions");
        await sessionsMap.ClearAsync();

        return Ok(new { message = "All test sessions cleared" });
    }
}
