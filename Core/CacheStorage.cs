using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
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
	IMap<TKey, TValue> GetOrCreateMap<TKey, TValue>(string mapName, TimeSpan? itemTtl = null) where TKey : notnull;
	IMap<TKey, TValue> GetOrCreateMapProtoBuf<TKey, TValue>(string mapName, TimeSpan? itemTtl = null)
		where TKey : notnull
		where TValue : class, IMessage<TValue>, new();
	void Return<TProtobuf>(TProtobuf protobuf) where TProtobuf : class, IMessage<TProtobuf>, new();


	IEnumerable<string> GetAllMapNames();
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

	public IMap<TKey, TValue> GetOrCreateMap<TKey, TValue>(string mapName, TimeSpan? itemTtl = null) where TKey : notnull
	{
		if (_maps.TryGetValue(mapName, out var existing))
		{
			return (IMap<TKey, TValue>)existing;
		}

		// Create new map if not exists
		RegisterMap<TKey, TValue>(mapName, itemTtl);

		if (_maps.TryGetValue(mapName, out var created))
		{
			return (IMap<TKey, TValue>)created;
		}

		throw new InvalidOperationException($"Failed to create map '{mapName}'");
	}

	public IMap<TKey, TValue> GetOrCreateMapProtoBuf<TKey, TValue>(string mapName, TimeSpan? itemTtl = null)
		where TKey : notnull
		where TValue : class, IMessage<TValue>, new()
	{
		if (_maps.TryGetValue(mapName, out var existing))
		{
			return (IMap<TKey, TValue>)existing;
		}

		RegisterMap<TKey, TValue>(mapName, itemTtl, new ProtobufRedisValueFormatter<TValue>());

		if (_maps.TryGetValue(mapName, out var created))
		{
			return (IMap<TKey, TValue>)created;
		}

		throw new InvalidOperationException($"Failed to create protobuf map '{mapName}'");
	}

	public IEnumerable<string> GetAllMapNames()
	{
		return _maps.Keys
			.Where(name => !name.Contains(":__meta:")) // ✅ Filter out internal metadata keys
			.ToList();
	}

	public IEnumerable<string> GetAllBucketNames() => _buckets.Keys.ToList();

	public object? GetMapInstance(string mapName)
	{
		_maps.TryGetValue(mapName, out var map);
		return map;
	}

	public void Return<TProtobuf>(TProtobuf protobuf)
		where TProtobuf : class, IMessage<TProtobuf>, new()
	{
		if (protobuf == null)
		{
			return;
		}

		ProtobufObjectPool.Return(protobuf);
	}

	internal void RegisterMap<TKey, TValue>(string mapName, TimeSpan? itemTtl = null, IRedisValueFormatter<TValue>? valueFormatter = null) where TKey : notnull
	{
		if (_maps.ContainsKey(mapName))
		{
			return;
		}

		var map = new RedisMap<TKey, TValue>(_redis, mapName, _database, _batchWaitTime, valueFormatter);

		// Set TTL if specified
		if (itemTtl.HasValue)
		{
			map.SetItemExpiration(itemTtl);
		}

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