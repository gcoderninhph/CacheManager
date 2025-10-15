using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using CacheManager.Core;

namespace CacheManager;

/// <summary>
/// Options controlling the CacheManager dashboard behavior.
/// </summary>
public sealed class CacheManagerOptions
{
	public string DashboardPath { get; set; } = "/cache-manager";

	/// <summary>
	/// Redis connection string used to enable cache inspection and Pub/Sub helpers.
	/// </summary>
	public string? RedisConnectionString { get; set; }

	/// <summary>
	/// Target Redis database. Defaults to -1 (server default).
	/// </summary>
	public int RedisDatabase { get; set; } = -1;

	/// <summary>
	/// Batch update wait time in seconds. Defaults to 5 seconds.
	/// </summary>
	public int BatchWaitTimeSeconds { get; set; } = 5;
}

/// <summary>
/// Service registration helpers for the CacheManager library.
/// </summary>
public static class CacheManagerServiceCollectionExtensions
{
	/// <summary>
	/// Add CacheManager services with configuration from appsettings.json
	/// </summary>
	public static IServiceCollection AddCacheManager(
		this IServiceCollection services, 
		Microsoft.Extensions.Configuration.IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		// Bind configuration from appsettings.json "CacheManager" section
		services.Configure<CacheManagerOptions>(
			configuration.GetSection("CacheManager"));

		services.TryAddSingleton<CacheManagerDashboardResources>();
		services.TryAddSingleton<ICacheManagerDashboardRenderer, EmbeddedCacheManagerDashboardRenderer>();
		services.TryAddSingleton<ICacheManagerRedisClient, CacheManagerRedisClient>();

		// Register RedisCacheStorage
		services.TryAddSingleton<RedisCacheStorage>(sp =>
		{
			var options = sp.GetRequiredService<IOptions<CacheManagerOptions>>().Value;
			var redis = ConnectionMultiplexer.Connect(options.RedisConnectionString ?? "localhost:6379");
			var batchWaitTime = TimeSpan.FromSeconds(options.BatchWaitTimeSeconds);
			return new RedisCacheStorage(redis, options.RedisDatabase, batchWaitTime);
		});

		// Register ICacheStorage
		services.TryAddSingleton<ICacheStorage>(sp => sp.GetRequiredService<RedisCacheStorage>());

		return services;
	}

	/// <summary>
	/// Add CacheManager services with manual configuration (legacy support)
	/// </summary>
	public static IServiceCollection AddCacheManager(
		this IServiceCollection services, 
		Action<CacheManagerOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var optionsBuilder = services.AddOptions<CacheManagerOptions>();
		optionsBuilder.Configure(configure);

		services.TryAddSingleton<CacheManagerDashboardResources>();
		services.TryAddSingleton<ICacheManagerDashboardRenderer, EmbeddedCacheManagerDashboardRenderer>();
		services.TryAddSingleton<ICacheManagerRedisClient, CacheManagerRedisClient>();

		// Register RedisCacheStorage
		services.TryAddSingleton<RedisCacheStorage>(sp =>
		{
			var options = sp.GetRequiredService<IOptions<CacheManagerOptions>>().Value;
			var redis = ConnectionMultiplexer.Connect(options.RedisConnectionString ?? "localhost:6379");
			var batchWaitTime = TimeSpan.FromSeconds(options.BatchWaitTimeSeconds);
			return new RedisCacheStorage(redis, options.RedisDatabase, batchWaitTime);
		});

		// Register ICacheStorage
		services.TryAddSingleton<ICacheStorage>(sp => sp.GetRequiredService<RedisCacheStorage>());

		return services;
	}
}

/// <summary>
/// Application configuration helpers for wiring the CacheManager dashboard.
/// </summary>
public static class CacheManagerApplicationBuilderExtensions
{
	/// <summary>
	/// Enable CacheManager dashboard at the path specified in appsettings.json
	/// </summary>
	public static WebApplication UseCacheManagerDashboard(this WebApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);

		var options = app.Services.GetRequiredService<IOptions<CacheManagerOptions>>().Value;
		return app.CacheManagerView(options.DashboardPath);
	}

	/// <summary>
	/// Enable CacheManager dashboard at a custom path (overrides appsettings.json)
	/// </summary>
	public static WebApplication CacheManagerView(this WebApplication app, string? requestPath = null)
	{
		ArgumentNullException.ThrowIfNull(app);

		var options = app.Services.GetRequiredService<IOptions<CacheManagerOptions>>().Value;
		var normalizedPath = NormalizePath(requestPath ?? options.DashboardPath);

		// API endpoint to get all maps and buckets
		app.MapGet($"{normalizedPath}/api/registry", async (ICacheStorage storage) =>
		{
			var maps = await storage.GetAllMapNames();
			var buckets = storage.GetAllBucketNames();
			return Results.Json(new { maps, buckets });
		});

		// API endpoint to get map data with pagination and search
		app.MapGet($"{normalizedPath}/api/map/{{mapName}}", async (
			string mapName, 
			ICacheStorage storage,
			int page = 1,
			int pageSize = 20,
			string? search = null) =>
		{
			try
			{
				var mapInstance = storage.GetMapInstance(mapName);
				if (mapInstance == null)
				{
					return Results.NotFound(new { error = $"Map '{mapName}' not found" });
				}

				// Dùng GetEntriesPagedAsync (OPTIMIZED) thay vì GetAllEntriesAsync
				// Method mới này dùng HSCAN cursor, không load toàn bộ hash vào memory
				var method = mapInstance.GetType().GetMethod("GetEntriesPagedAsync");
				if (method == null)
				{
					return Results.Problem("Method 'GetEntriesPagedAsync' not found");
				}

				// Call với parameters: page, pageSize, search
				var task = method.Invoke(mapInstance, new object[] { page, pageSize, search! }) as Task;
				if (task != null)
				{
					await task.ConfigureAwait(false);
					
					// Extract PagedMapEntries result
					var resultProperty = task.GetType().GetProperty("Result");
					var pagedResult = resultProperty?.GetValue(task);
					
					if (pagedResult != null)
					{
						return Results.Json(new 
						{ 
							mapName,
							data = pagedResult // PagedMapEntries với Entries + Pagination info
						});
					}
				}

				return Results.Problem("Unable to retrieve map data");
			}
			catch (Exception ex)
			{
				return Results.Problem($"Error retrieving map data: {ex.Message}");
			}
		});

		// Main dashboard page
		app.MapGet(normalizedPath, async (HttpContext context, ICacheManagerDashboardRenderer renderer) =>
		{
			await renderer.RenderAsync(context, null);
		});

		// Static resources (CSS, JS)
		app.MapGet($"{normalizedPath}/{{*resource}}", async (HttpContext context, ICacheManagerDashboardRenderer renderer) =>
		{
			var resource = context.GetRouteValue("resource") as string;
			await renderer.RenderAsync(context, resource);
		});

		return app;
	}

	private static string NormalizePath(string? path)
	{
		const string fallback = "/cache-manager";

		if (string.IsNullOrWhiteSpace(path))
		{
			return fallback;
		}

		var trimmed = path.Trim();
		if (!trimmed.StartsWith("/", StringComparison.Ordinal))
		{
			trimmed = "/" + trimmed;
		}

		if (trimmed.Length > 1 && trimmed.EndsWith("/", StringComparison.Ordinal))
		{
			trimmed = trimmed.TrimEnd('/');
		}

		return trimmed;
	}
}

internal interface ICacheManagerDashboardRenderer
{
	Task RenderAsync(HttpContext context, string? resourcePath);
}

internal sealed class EmbeddedCacheManagerDashboardRenderer : ICacheManagerDashboardRenderer
{
	private readonly CacheManagerDashboardResources _resources;

	public EmbeddedCacheManagerDashboardRenderer(CacheManagerDashboardResources resources)
	{
		_resources = resources;
	}

	public async Task RenderAsync(HttpContext context, string? resourcePath)
	{
		ArgumentNullException.ThrowIfNull(context);

		var asset = _resources.GetResource(resourcePath);
		if (asset is null)
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		context.Response.ContentType = asset.ContentType;
		context.Response.Headers["Cache-Control"] = "no-store";
		
		// If serving HTML, inject base path
		if (string.IsNullOrWhiteSpace(resourcePath) && asset.ContentType.Contains("text/html"))
		{
			var basePath = context.Request.PathBase.ToString() + context.Request.Path.ToString();
			if (!basePath.EndsWith("/", StringComparison.Ordinal))
			{
				basePath += "/";
			}
			await asset.WriteWithBasePathAsync(context.Response.Body, basePath, context.RequestAborted);
		}
		else
		{
			await asset.WriteAsync(context.Response.Body, context.RequestAborted);
		}
	}
}

internal sealed class CacheManagerDashboardResources
{
	private readonly ManifestEmbeddedFileProvider _provider = new(typeof(CacheManagerDashboardResources).Assembly, "Dashboard");

	public DashboardAsset? GetResource(string? resourcePath)
	{
		var normalized = NormalizeResourcePath(resourcePath);
		var fileInfo = _provider.GetFileInfo(normalized);
		if (!fileInfo.Exists)
		{
			return null;
		}

		return new DashboardAsset(fileInfo, GetContentType(normalized));
	}

	private static string NormalizeResourcePath(string? resourcePath)
	{
		if (string.IsNullOrWhiteSpace(resourcePath))
		{
			return "index.html";
		}

		var trimmed = resourcePath.Trim();
		trimmed = trimmed.TrimStart('/', '\\');
		return trimmed.Replace('\\', '/');
	}

	private static string GetContentType(string path)
	{
		if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
		{
			return "text/css; charset=utf-8";
		}

		if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
		{
			return "application/javascript; charset=utf-8";
		}

		return "text/html; charset=utf-8";
	}
}

internal sealed class DashboardAsset
{
	private readonly IFileInfo _fileInfo;

	public DashboardAsset(IFileInfo fileInfo, string contentType)
	{
		_fileInfo = fileInfo;
		ContentType = contentType;
	}

	public string ContentType { get; }

	public async Task WriteAsync(Stream target, CancellationToken cancellationToken)
	{
		await using var stream = _fileInfo.CreateReadStream();
		await stream.CopyToAsync(target, cancellationToken);
	}

	public async Task WriteWithBasePathAsync(Stream target, string basePath, CancellationToken cancellationToken)
	{
		await using var stream = _fileInfo.CreateReadStream();
		using var reader = new StreamReader(stream);
		var html = await reader.ReadToEndAsync(cancellationToken);
		
		// Inject base tag after <head>
		html = html.Replace("<head>", $"<head>\n    <base href=\"{basePath}\">");
		
		await target.WriteAsync(System.Text.Encoding.UTF8.GetBytes(html), cancellationToken);
	}
}

/// <summary>
/// Exposes high-level Redis operations that CacheManager relies on for dashboard insights.
/// </summary>
public interface ICacheManagerRedisClient : IAsyncDisposable
{
	bool IsEnabled { get; }

	Task<HashEntry[]> GetHashAsync(string key, CommandFlags flags = CommandFlags.None);

	Task<TimeSpan?> GetTimeToLiveAsync(string key, CommandFlags flags = CommandFlags.None);

	Task<long> PublishAsync(string channel, string message, CommandFlags flags = CommandFlags.None);

	Task<IAsyncDisposable> SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None);
}

internal sealed class CacheManagerRedisClient : ICacheManagerRedisClient
{
	private readonly CacheManagerOptions _options;
	private readonly Lazy<Task<ConnectionMultiplexer>> _connectionFactory;
	private int _disposed;

	public CacheManagerRedisClient(IOptions<CacheManagerOptions> options)
	{
		_options = options.Value;
		_connectionFactory = new Lazy<Task<ConnectionMultiplexer>>(ConnectAsync, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.RedisConnectionString);

	public async Task<HashEntry[]> GetHashAsync(string key, CommandFlags flags = CommandFlags.None)
	{
		EnsureEnabled();
		ArgumentException.ThrowIfNullOrWhiteSpace(key);

		var db = await GetDatabaseAsync().ConfigureAwait(false);
		return await db.HashGetAllAsync(key, flags).ConfigureAwait(false);
	}

	public async Task<TimeSpan?> GetTimeToLiveAsync(string key, CommandFlags flags = CommandFlags.None)
	{
		EnsureEnabled();
		ArgumentException.ThrowIfNullOrWhiteSpace(key);

		var db = await GetDatabaseAsync().ConfigureAwait(false);
		return await db.KeyTimeToLiveAsync(key, flags).ConfigureAwait(false);
	}

	public async Task<long> PublishAsync(string channel, string message, CommandFlags flags = CommandFlags.None)
	{
		EnsureEnabled();
		ArgumentException.ThrowIfNullOrWhiteSpace(channel);

		var connection = await _connectionFactory.Value.ConfigureAwait(false);
		var subscriber = connection.GetSubscriber();
		var redisChannel = new RedisChannel(channel, RedisChannel.PatternMode.Literal);
		return await subscriber.PublishAsync(redisChannel, message, flags).ConfigureAwait(false);
	}

	public async Task<IAsyncDisposable> SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None)
	{
		EnsureEnabled();
		ArgumentException.ThrowIfNullOrWhiteSpace(channel);
		ArgumentNullException.ThrowIfNull(handler);

		var connection = await _connectionFactory.Value.ConfigureAwait(false);
		var subscriber = connection.GetSubscriber();
		var redisChannel = new RedisChannel(channel, RedisChannel.PatternMode.Literal);
		await subscriber.SubscribeAsync(redisChannel, handler, flags).ConfigureAwait(false);
		return new RedisSubscription(subscriber, redisChannel, handler);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		if (_connectionFactory.IsValueCreated)
		{
			var connection = await _connectionFactory.Value.ConfigureAwait(false);
			await connection.CloseAsync();
			connection.Dispose();
		}
	}

	private void EnsureEnabled()
	{
		if (!IsEnabled)
		{
			throw new InvalidOperationException("Redis integration is not enabled. Configure CacheManagerOptions.RedisConnectionString before invoking Redis helpers.");
		}
	}

	private Task<ConnectionMultiplexer> ConnectAsync()
	{
		EnsureEnabled();

		var configuration = ConfigurationOptions.Parse(_options.RedisConnectionString!, true);
		configuration.AbortOnConnectFail = false;
		if (_options.RedisDatabase >= 0)
		{
			configuration.DefaultDatabase = _options.RedisDatabase;
		}

		return ConnectionMultiplexer.ConnectAsync(configuration);
	}

	private async ValueTask<IDatabase> GetDatabaseAsync()
	{
		var connection = await _connectionFactory.Value.ConfigureAwait(false);
		return connection.GetDatabase(_options.RedisDatabase);
	}
}

internal sealed class RedisSubscription : IAsyncDisposable
{
	private readonly ISubscriber _subscriber;
	private readonly RedisChannel _channel;
	private readonly Action<RedisChannel, RedisValue> _handler;
	private int _disposed;

	public RedisSubscription(ISubscriber subscriber, RedisChannel channel, Action<RedisChannel, RedisValue> handler)
	{
		_subscriber = subscriber;
		_channel = channel;
		_handler = handler;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		await _subscriber.UnsubscribeAsync(_channel, _handler).ConfigureAwait(false);
	}
}
