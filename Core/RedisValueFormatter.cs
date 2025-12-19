using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using StackExchange.Redis;

namespace CacheManager.Core;

internal interface IRedisValueFormatter<TValue>
{
    RedisValue Serialize(TValue value);
    TValue Deserialize(RedisValue value);
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

    public RedisValue Serialize(TValue value) => JsonSerializer.Serialize(value, _options);

    public TValue Deserialize(RedisValue value) =>
        JsonSerializer.Deserialize<TValue>(value.ToString(), _options) ??
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
    public RedisValue Serialize(TValue value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.ToByteArray();
    }

    public TValue Deserialize(RedisValue value)
    {
        if (!value.HasValue)
        {
            throw new InvalidOperationException("Cannot deserialize an empty Redis value");
        }

        var instance = ProtobufObjectPool.Rent<TValue>();
        var bytes = (byte[])value!;
        instance.MergeFrom(bytes);
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

internal static class ProtobufObjectPool
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
