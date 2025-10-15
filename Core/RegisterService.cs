using System;
using System.Collections.Generic;

namespace CacheManager.Core;


/// <summary>
///  Đăng ký các map, bucket với cache manager, class này sẽ đươc inject vào asp.net core
/// </summary>
public interface ICacheRegisterService
{
    IRegisterBuilder RegisterBuilder();
}

public interface IRegisterBuilder
{
    /// <summary>
    ///  Tạo map với tên mapName, nếu đã tồn tại thì trả về map đã tồn tại 
    ///  Sau khi đăng ký xong sẽ có 
    /// </summary>
    /// <typeparam name="Key"></typeparam>
    /// <typeparam name="Value"></typeparam>
    /// <returns></returns>
    IRegisterBuilder CreateMap<Key, Value>(string mapName) where Key : notnull;
    
    /// <summary>
    /// Tạo map với TTL cho từng phần tử
	/// Nếu một phần tử không có bất kỳ hoạt động nào (Get/Set) trong khoảng thời gian này, nó sẽ tự động bị xóa
	/// Logic: Dùng Redis Sorted Set phụ để tracking thời gian truy cập cuối cùng
	/// <paramref name="mapName"/>: Tên của map
	/// <paramref name="itemTtl"/>: Thời gian hết hạn. Nếu null thì tắt TTL
    /// </summary>
    IRegisterBuilder CreateMap<Key, Value>(string mapName, TimeSpan? itemTtl) where Key : notnull;
    
    IRegisterBuilder CreateBucket<Value>(string bucketName);
    void Build();
}

internal sealed class CacheRegisterService : ICacheRegisterService
{
	private readonly RedisCacheStorage _storage;

	public CacheRegisterService(RedisCacheStorage storage)
	{
		_storage = storage;
	}

	public IRegisterBuilder RegisterBuilder()
	{
		return new RegisterBuilder(_storage);
	}
}

internal sealed class RegisterBuilder : IRegisterBuilder
{
	private readonly RedisCacheStorage _storage;
	private readonly List<Action> _registrations;

	public RegisterBuilder(RedisCacheStorage storage)
	{
		_storage = storage;
		_registrations = new List<Action>();
	}

	public IRegisterBuilder CreateMap<Key, Value>(string mapName) where Key : notnull
	{
		_registrations.Add(() => _storage.RegisterMap<Key, Value>(mapName));
		return this;
	}

	public IRegisterBuilder CreateMap<Key, Value>(string mapName, TimeSpan? itemTtl) where Key : notnull
	{
		_registrations.Add(() =>
		{
			_storage.RegisterMap<Key, Value>(mapName);
			
			// Cấu hình TTL cho từng item nếu có
			if (itemTtl.HasValue)
			{
				var map = _storage.GetMap<Key, Value>(mapName);
				map.SetItemExpiration(itemTtl);
			}
		});
		return this;
	}

	public IRegisterBuilder CreateBucket<Value>(string bucketName)
	{
		_registrations.Add(() => _storage.RegisterBucket<Value>(bucketName));
		return this;
	}

	public void Build()
	{
		foreach (var registration in _registrations)
		{
			registration();
		}
	}
}