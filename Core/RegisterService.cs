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