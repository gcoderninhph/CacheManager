using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace CacheManager.Core;

/// <summary>
/// Lưu trữ các map, bucket đã được đăng ký
/// Mặc định sẽ có một instance của class này trong asp.net core
/// Giao diện web sẽ hiển thị toàn bộ map, bucket trong cache storage
/// Luy ý: Map hiển thị trên giao diện giới hạn 20 bản ghi / trang, có chức năng next, prev, search theo key
/// </summary>
public interface ICacheStorage
{
	IMap<TKey, TValue> GetMap<TKey, TValue>(string mapName) where TKey : notnull;
	Task<IMap<TKey, TValue>> GetOrCreateMapAsync<TKey, TValue>(string mapName) where TKey : notnull;
	Task<IEnumerable<string>> GetAllMapNames();
	IEnumerable<string> GetAllBucketNames();
	object? GetMapInstance(string mapName);
}

internal sealed class RedisCacheStorage : ICacheStorage
{
	private readonly IConnectionMultiplexer _redis;
	private readonly int _database;
	private readonly ConcurrentDictionary<string, object> _maps;
	private readonly ConcurrentDictionary<string, object> _buckets;
	private readonly ConcurrentDictionary<string, Type> _mapTypes;
	private readonly ConcurrentDictionary<string, Type> _bucketTypes;
	private readonly TimeSpan _batchWaitTime;

	public RedisCacheStorage(IConnectionMultiplexer redis, int database = -1, TimeSpan? batchWaitTime = null)
	{
		_redis = redis;
		_database = database;
		_maps = new ConcurrentDictionary<string, object>();
		_buckets = new ConcurrentDictionary<string, object>();
		_mapTypes = new ConcurrentDictionary<string, Type>();
		_bucketTypes = new ConcurrentDictionary<string, Type>();
		_batchWaitTime = batchWaitTime ?? TimeSpan.FromSeconds(5);
	}

	public IMap<TKey, TValue> GetMap<TKey, TValue>(string mapName) where TKey : notnull
	{
		if (_maps.TryGetValue(mapName, out var existing))
		{
			return (IMap<TKey, TValue>)existing;
		}

		throw new KeyNotFoundException($"Map '{mapName}' not found. Please register it first.");
	}

	public async Task<IMap<TKey, TValue>> GetOrCreateMapAsync<TKey, TValue>(string mapName) where TKey : notnull
	{
		if (_maps.TryGetValue(mapName, out var existing))
		{
			return (IMap<TKey, TValue>)existing;
		}

		// Create new map if not exists
		RegisterMap<TKey, TValue>(mapName);
		return await Task.FromResult(GetMap<TKey, TValue>(mapName));
	}

	public async Task<IEnumerable<string>> GetAllMapNames()
	{
		return await Task.FromResult(_maps.Keys
			.Where(name => !name.Contains(":__meta:")) // ✅ Filter out internal metadata keys
			.ToList());
	}

	public IEnumerable<string> GetAllBucketNames() => _buckets.Keys.ToList();

	public object? GetMapInstance(string mapName)
	{
		_maps.TryGetValue(mapName, out var map);
		return map;
	}

	internal void RegisterMap<TKey, TValue>(string mapName) where TKey : notnull
	{
		if (_maps.ContainsKey(mapName))
		{
			return;
		}

		var map = new RedisMap<TKey, TValue>(_redis, mapName, _database, _batchWaitTime);
		_maps.TryAdd(mapName, map);
		_mapTypes.TryAdd(mapName, typeof(IMap<TKey, TValue>));
	}

	internal void RegisterBucket<TValue>(string bucketName)
	{
		if (_buckets.ContainsKey(bucketName))
		{
			return;
		}

		var bucket = new RedisBucket<TValue>(_redis, bucketName, _database);
		_buckets.TryAdd(bucketName, bucket);
		_bucketTypes.TryAdd(bucketName, typeof(TValue));
	}
}