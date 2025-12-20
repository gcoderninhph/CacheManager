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
        var instance = new TValue();
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