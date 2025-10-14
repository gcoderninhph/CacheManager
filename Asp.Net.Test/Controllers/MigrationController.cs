using CacheManager.Core;
using Microsoft.AspNetCore.Mvc;

namespace Asp.Net.Test.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
	private readonly ICacheStorage _cacheStorage;

	public MigrationController(ICacheStorage cacheStorage)
	{
		_cacheStorage = cacheStorage;
	}

	/// <summary>
	/// Migrate ALL maps from Hash to Sorted Set (one-time operation)
	/// </summary>
	[HttpPost("migrate-all")]
	public async Task<IActionResult> MigrateAll()
	{
		try
		{
			var mapNames = await _cacheStorage.GetAllMapNames();
			var results = new List<object>();

			Console.WriteLine($"[MIGRATION] Starting migration for {mapNames.Count()} maps");

			foreach (var mapName in mapNames)
			{
				try
				{
					// Get map instance WITHOUT casting (use GetMapInstance to get raw object)
					var mapInstance = _cacheStorage.GetMapInstance(mapName);
					
					if (mapInstance == null)
					{
						results.Add(new
						{
							mapName,
							status = "error",
							error = "Map not found"
						});
						continue;
					}
					
					// Use reflection to call MigrateTimestampsToSortedSetAsync (since IMap doesn't expose it)
					var mapType = mapInstance.GetType();
					var migrateMethod = mapType.GetMethod("MigrateTimestampsToSortedSetAsync");
					
					if (migrateMethod != null)
					{
						var task = migrateMethod.Invoke(mapInstance, null) as Task;
						if (task != null)
						{
							await task;
						}
						
						// Get status after migration
						var statusMethod = mapType.GetMethod("GetMigrationStatusAsync");
						if (statusMethod != null)
						{
							var statusTask = statusMethod.Invoke(mapInstance, null);
							if (statusTask != null)
							{
								await (Task)statusTask;
								var status = await (dynamic)statusTask;
								results.Add(new
								{
									mapName = status.MapName,
									hashCount = status.HashCount,
									sortedSetCount = status.SortedSetCount,
									isMigrated = status.IsMigrated,
									isComplete = status.IsComplete,
									status = "success"
								});
							}
						}
					}
					else
					{
						results.Add(new
						{
							mapName,
							status = "error",
							error = "Migration method not found"
						});
					}
				}
				catch (Exception ex)
				{
					results.Add(new
					{
						mapName,
						status = "error",
						error = ex.Message
					});
					Console.WriteLine($"[MIGRATION] Error migrating {mapName}: {ex.Message}");
				}
			}

			Console.WriteLine($"[MIGRATION] Migration complete!");

			return Ok(new
			{
				message = "Migration complete",
				totalMaps = mapNames.Count(),
				results
			});
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { error = ex.Message });
		}
	}

	/// <summary>
	/// Migrate a single map from Hash to Sorted Set
	/// </summary>
	[HttpPost("migrate/{mapName}")]
	public async Task<IActionResult> MigrateSingleMap(string mapName)
	{
		try
		{
			var mapInstance = _cacheStorage.GetMapInstance(mapName);
			
			if (mapInstance == null)
			{
				return NotFound(new { error = $"Map {mapName} not found" });
			}
			
			// Use reflection to call migration methods
			var mapType = mapInstance.GetType();
			var migrateMethod = mapType.GetMethod("MigrateTimestampsToSortedSetAsync");
			
			if (migrateMethod != null)
			{
				var task = migrateMethod.Invoke(mapInstance, null) as Task;
				if (task != null)
				{
					await task;
				}
			}
			
			// Get status after migration
			var statusMethod = mapType.GetMethod("GetMigrationStatusAsync");
			if (statusMethod != null)
			{
				var statusTask = statusMethod.Invoke(mapInstance, null);
				if (statusTask != null)
				{
					await (Task)statusTask;
					var status = await (dynamic)statusTask;
					return Ok(new
					{
						message = "Migration complete",
						mapName = status.MapName,
						hashCount = status.HashCount,
						sortedSetCount = status.SortedSetCount,
						isMigrated = status.IsMigrated,
						isComplete = status.IsComplete
					});
				}
			}

			return Ok(new { message = "Migration initiated", mapName });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { error = ex.Message });
		}
	}

	/// <summary>
	/// Get migration status for all maps
	/// </summary>
	[HttpGet("status")]
	public async Task<IActionResult> GetStatus()
	{
		try
		{
			var mapNames = await _cacheStorage.GetAllMapNames();
			var results = new List<object>();

			foreach (var mapName in mapNames)
			{
				try
				{
					var mapInstance = _cacheStorage.GetMapInstance(mapName);
					
					if (mapInstance == null)
					{
						results.Add(new
						{
							mapName,
							error = "Map not found"
						});
						continue;
					}
					
					var mapType = mapInstance.GetType();
					var statusMethod = mapType.GetMethod("GetMigrationStatusAsync");
					
					if (statusMethod != null)
					{
						var statusTask = statusMethod.Invoke(mapInstance, null);
						if (statusTask != null)
						{
							await (Task)statusTask;
							var status = await (dynamic)statusTask;
							results.Add(new
							{
								mapName = status.MapName,
								hashCount = status.HashCount,
								sortedSetCount = status.SortedSetCount,
								isMigrated = status.IsMigrated,
								isComplete = status.IsComplete
							});
						}
					}
					else
					{
						results.Add(new
						{
							mapName,
							error = "Status method not found"
						});
					}
				}
				catch (Exception ex)
				{
					results.Add(new
					{
						mapName,
						error = ex.Message
					});
				}
			}

			return Ok(new
			{
				totalMaps = mapNames.Count(),
				results
			});
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { error = ex.Message });
		}
	}

	/// <summary>
	/// Get migration status for a single map
	/// </summary>
	[HttpGet("status/{mapName}")]
	public async Task<IActionResult> GetMapStatus(string mapName)
	{
		try
		{
			var mapInstance = _cacheStorage.GetMapInstance(mapName);
			
			if (mapInstance == null)
			{
				return NotFound(new { error = $"Map {mapName} not found" });
			}
			
			var mapType = mapInstance.GetType();
			var statusMethod = mapType.GetMethod("GetMigrationStatusAsync");
			
			if (statusMethod != null)
			{
				var statusTask = statusMethod.Invoke(mapInstance, null);
				if (statusTask != null)
				{
					await (Task)statusTask;
					var status = await (dynamic)statusTask;
					return Ok(new
					{
						mapName = status.MapName,
						hashCount = status.HashCount,
						sortedSetCount = status.SortedSetCount,
						isMigrated = status.IsMigrated,
						isComplete = status.IsComplete
					});
				}
			}

			return NotFound(new { error = $"Status method not found for map {mapName}" });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { error = ex.Message });
		}
	}
}
