using System;
using System.Collections.Generic;

namespace CacheManager.Core;

internal sealed class Entry<TKey, TValue> : IEntry<TKey, TValue>
{
    private readonly TKey _key;
    private readonly TValue _value;

    public Entry(TKey key, TValue value)
    {
        _key = key;
        _value = value;
    }

    public TKey GetKey() => _key;
    public TValue GetValue() => _value;
}

public class MapEntryData
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string LastModified { get; set; } = string.Empty;
    public long LastModifiedTicks { get; set; }
}

public class PagedMapEntries
{
    public List<MapEntryData> Entries { get; set; } = new();
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNext { get; set; }
    public bool HasPrev { get; set; }
}

public class MigrationStatus
{
    public string MapName { get; set; } = string.Empty;
    public long HashCount { get; set; }
    public long SortedSetCount { get; set; }
    public bool IsMigrated { get; set; }
    public bool IsComplete { get; set; }
}
