using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using StackExchange.Redis;

namespace CacheManager.Core;

internal interface IRedisValueFormatter<TValue>
{
    PooledRedisValue Serialize(TValue value);
    TValue Deserialize(ReadOnlySpan<byte> value);
    string ToDisplayString(TValue value);
    bool SupportsPooling { get; }
    void ReturnToPool(TValue value);
}

internal sealed class JsonRedisValueFormatter<TValue> : IRedisValueFormatter<TValue>
{
    private readonly JsonSerializerOptions _options;

    public JsonRedisValueFormatter(JsonSerializerOptions options)
    {
        _options = options;
    }

    public PooledRedisValue Serialize(TValue value) => PooledRedisValue.FromValue(JsonSerializer.Serialize(value, _options));

    public TValue Deserialize(ReadOnlySpan<byte> value) =>
        JsonSerializer.Deserialize<TValue>(value, _options) ??
        throw new InvalidOperationException("Failed to deserialize value from Redis");

    public string ToDisplayString(TValue value) => JsonSerializer.Serialize(value, _options);

    public bool SupportsPooling => false;

    public void ReturnToPool(TValue value)
    {
        // JSON formatter does not need pooling.
    }
}

internal sealed class ProtobufRedisValueFormatter<TValue> : IRedisValueFormatter<TValue>
    where TValue : class, IMessage<TValue>, new()
{
    public PooledRedisValue Serialize(TValue value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var size = value.CalculateSize();
        if (size == 0)
        {
            return PooledRedisValue.FromValue(RedisValue.EmptyString);
        }

        var buffer = ExactByteArrayPool.Rent(size);
        value.WriteTo(buffer.AsSpan(0, size));

        return PooledRedisValue.FromOwnedBuffer(buffer, size);
    }

    public TValue Deserialize(ReadOnlySpan<byte> value)
    {
        var instance = ProtobufObjectPool.Rent<TValue>();
        if (!value.IsEmpty)
        {
            instance.MergeFrom(value);
        }

        return instance;
    }

    public string ToDisplayString(TValue value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return Convert.ToBase64String(value.ToByteArray());
    }

    public bool SupportsPooling => true;

    public void ReturnToPool(TValue value)
    {
        if (value != null)
        {
            ProtobufObjectPool.Return(value);
        }
    }
}

internal readonly struct PooledRedisValue : IDisposable
{
    private readonly byte[]? _buffer;

    private PooledRedisValue(RedisValue value, byte[]? buffer)
    {
        Value = value;
        _buffer = buffer;
    }

    public RedisValue Value { get; }

    public static PooledRedisValue FromOwnedBuffer(byte[] buffer, int length)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if ((uint)length > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var memory = new ReadOnlyMemory<byte>(buffer, 0, length);
        return new PooledRedisValue(memory, buffer);
    }

    public static PooledRedisValue FromValue(RedisValue value) => new(value, null);

    public static implicit operator RedisValue(PooledRedisValue value) => value.Value;

    public void Dispose()
    {
        if (_buffer != null)
        {
            ExactByteArrayPool.Return(_buffer);
        }
    }
}

internal static class ExactByteArrayPool
{
    public static byte[] Rent(int size)
    {
        if (size <= 0)
        {
            return Array.Empty<byte>();
        }

        return ArrayPool<byte>.Shared.Rent(size);
    }

    public static void Return(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }
}

public static class ProtobufObjectPool
{
    private static readonly ConcurrentDictionary<Type, ConcurrentBag<IMessage>> Pools = new();

    public static TProtobuf Rent<TProtobuf>() where TProtobuf : class, IMessage<TProtobuf>, new()
    {
        var bag = Pools.GetOrAdd(typeof(TProtobuf), _ => new ConcurrentBag<IMessage>());
        if (bag.TryTake(out var instance) && instance is TProtobuf typed)
        {
            return typed;
        }

        return new TProtobuf();
    }

    public static void Return<TProtobuf>(TProtobuf instance) where TProtobuf : class, IMessage<TProtobuf>, new()
    {
        if (instance == null)
        {
            return;
        }

        Reset(instance);

        var bag = Pools.GetOrAdd(typeof(TProtobuf), _ => new ConcurrentBag<IMessage>());
        if (bag.Count >= 100) return;
        bag.Add(instance);
    }

    private static void Reset(IMessage message)
    {
        var descriptor = message.Descriptor;
        foreach (var field in descriptor.Fields.InDeclarationOrder())
        {
            field.Accessor.Clear(message);
        }

        ClearUnknownFields(message);
    }

    private static void ClearUnknownFields(IMessage message)
    {
        var type = message.GetType();

        var clearUnknownFields = type.GetMethod(
            "ClearUnknownFields",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            Type.DefaultBinder,
            Type.EmptyTypes,
            null);

        if (clearUnknownFields != null)
        {
            clearUnknownFields.Invoke(message, null);
            return;
        }

        var mergeUnknownFields = type.GetMethod(
            "MergeUnknownFields",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            Type.DefaultBinder,
            new[] { typeof(UnknownFieldSet) },
            null);

        if (mergeUnknownFields != null)
        {
            mergeUnknownFields.Invoke(message, new object?[] { null });
            return;
        }

        var unknownFieldsField = type.GetField("_unknownFields", BindingFlags.Instance | BindingFlags.NonPublic);
        if (unknownFieldsField != null)
        {
            unknownFieldsField.SetValue(message, null);
        }
    }
}
