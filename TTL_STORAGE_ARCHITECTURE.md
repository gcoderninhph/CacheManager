# TTL Storage Architecture - Kiến trúc lưu trữ TTL/Expiration

## Câu hỏi: Version/Time Expiration trong logic xử lý cache được lưu ở đâu?

### TL;DR - Câu trả lời ngắn gọn

**TTL/Expiration được lưu TẠI REDIS**, không phải trong bộ nhớ C#.

- **Access Time**: Lưu trong Redis Sorted Set với key `map:{mapName}:access-time`
- **Data**: Lưu trong Redis Hash với key `map:{mapName}`
- **Version Cache**: Chỉ có version được cache trong C# memory (`ConcurrentDictionary`) để tối ưu performance

---

## Kiến trúc chi tiết

### 1. Dữ liệu trong Redis

#### A. Hash - Lưu trữ Key-Value chính
```
Key: "map:products"
Type: Redis Hash

Field           | Value (JSON)
----------------|------------------
"1"             | {"id":1,"name":"Product 1","price":100}
"2"             | {"id":2,"name":"Product 2","price":200}
"3"             | {"id":3,"name":"Product 3","price":300}
```

#### B. Sorted Set - Lưu trữ Access Time
```
Key: "map:products:access-time"
Type: Redis Sorted Set (ZSET)

Member (field)  | Score (Unix Timestamp)
----------------|------------------------
"1"             | 1729033200  (Last access: 2024-10-15 10:00:00)
"2"             | 1729033260  (Last access: 2024-10-15 10:01:00)
"3"             | 1729033320  (Last access: 2024-10-15 10:02:00)
```

**Score** = Unix timestamp (seconds) của lần truy cập cuối cùng

---

### 2. Dữ liệu trong C# Memory

#### Version Cache (ConcurrentDictionary)
```csharp
private readonly ConcurrentDictionary<TKey, MapEntry> _versionCache;

// Chỉ lưu metadata, KHÔNG lưu access time
class MapEntry {
    Guid Version;
    DateTime LastModified;
}
```

**Mục đích**: 
- Tối ưu performance cho version tracking
- Detect concurrent updates
- KHÔNG dùng để lưu TTL/expiration

---

## Luồng xử lý TTL/Expiration

### 1. Khi Set TTL cho Map

```csharp
// Asp.Net.Test/Controllers/TtlTestController.cs
map.SetItemExpiration(TimeSpan.FromMinutes(5)); // TTL = 5 phút
```

**Điều gì xảy ra?**
```csharp
// Core/RedisMap.cs - Line 159
public void SetItemExpiration(TimeSpan? ttl)
{
    _itemTtl = ttl; // Lưu vào biến instance C#
    
    if (ttl.HasValue && _expirationTimer == null)
    {
        // Khởi tạo timer check expiration mỗi 1 giây
        _expirationTimer = new Timer(ProcessExpiration, null, 
            TimeSpan.FromSeconds(1), 
            TimeSpan.FromSeconds(1));
    }
}
```

**TTL được lưu ở đâu?**
- ❌ **KHÔNG** lưu vào Redis
- ✅ Chỉ lưu trong biến instance `_itemTtl` của C#
- ✅ Access time mới được lưu vào Redis Sorted Set

---

### 2. Khi GetValue hoặc SetValue

```csharp
// Core/RedisMap.cs - Line 73-76
public async Task<TValue> GetValueAsync(TKey key)
{
    // ... lấy value từ Redis Hash ...
    
    // Update access time nếu có TTL
    if (_itemTtl.HasValue)
    {
        await UpdateAccessTimeAsync(key); // ← Lưu vào Redis
    }
    
    return value;
}
```

**UpdateAccessTimeAsync** lưu access time vào Redis:
```csharp
// Core/RedisMap.cs - Line 757
private async Task UpdateAccessTimeAsync(TKey key)
{
    var db = _redis.GetDatabase(_database);
    var accessTimeKey = GetAccessTimeKey(); // "map:{mapName}:access-time"
    var fieldName = SerializeKey(key);
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Unix timestamp
    
    // Lưu vào Redis Sorted Set
    await db.SortedSetAddAsync(accessTimeKey, fieldName, now);
}
```

**Redis command tương đương:**
```redis
ZADD map:products:access-time 1729033200 "1"
```

---

### 3. Background Timer - ProcessExpiration

Timer chạy mỗi 1 giây để kiểm tra và xóa keys đã hết hạn:

```csharp
// Core/RedisMap.cs - Line 771
private void ProcessExpiration(object? state)
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var expirationThreshold = now - (long)_itemTtl.Value.TotalSeconds;
    
    // Lấy tất cả keys có access time < threshold (đã hết hạn)
    // Redis command: ZRANGEBYSCORE map:products:access-time -inf {threshold}
    var expiredKeys = db.SortedSetRangeByScore(
        accessTimeKey,
        double.NegativeInfinity,
        expirationThreshold
    );
    
    foreach (var expiredKey in expiredKeys)
    {
        // 1. Xóa khỏi Hash (data chính)
        db.HashDelete(hashKey, expiredKey);
        
        // 2. Xóa khỏi Sorted Set (access time)
        db.SortedSetRemove(accessTimeKey, expiredKey);
        
        // 3. Xóa khỏi version cache (C# memory)
        _versionCache.TryRemove(key, out _);
        
        // 4. Trigger callbacks
        TriggerExpiredHandlers(key, value);
    }
}
```

**Redis commands tương đương:**
```redis
# 1. Tìm keys đã hết hạn (access time < threshold)
ZRANGEBYSCORE map:products:access-time -inf 1729029600

# 2. Xóa data
HDEL map:products "1" "2" "3"

# 3. Xóa access time
ZREM map:products:access-time "1" "2" "3"
```

---

## So sánh 3 loại storage

| Loại dữ liệu | Lưu ở Redis? | Lưu trong C#? | Mục đích | Persistence |
|--------------|-------------|---------------|----------|-------------|
| **Key-Value Data** | ✅ Redis Hash | ❌ | Lưu trữ chính | Persistent |
| **Access Time** | ✅ Redis Sorted Set | ❌ | Track TTL | Persistent |
| **TTL Duration** | ❌ | ✅ `_itemTtl` | Config | In-memory |
| **Version** | ❌ | ✅ `_versionCache` | Optimistic locking | In-memory |

---

## Ví dụ thực tế

### Scenario: Set TTL = 5 phút, sau đó access key

```csharp
// 1. Set TTL (10:00:00)
map.SetItemExpiration(TimeSpan.FromMinutes(5));
// → _itemTtl = 5 minutes (lưu trong C#)
// → Timer bắt đầu chạy mỗi 1 giây

// 2. Get key = 1 (10:00:00)
var product = await map.GetValueAsync(1);
// → Redis: ZADD map:products:access-time 1729033200 "1"
// → Last access = 10:00:00

// 3. Get key = 1 again (10:02:00)
var product = await map.GetValueAsync(1);
// → Redis: ZADD map:products:access-time 1729033320 "1"
// → Last access = 10:02:00 (updated)

// 4. Wait 5 minutes... (10:07:00)
// → Timer ProcessExpiration chạy:
//   - Now = 10:07:00 = 1729033620
//   - Threshold = 10:07:00 - 5 min = 10:02:00 = 1729033320
//   - Check: ZRANGEBYSCORE map:products:access-time -inf 1729033320
//   - Kết quả: Key "1" có score = 1729033320 (10:02:00) ≤ threshold
//   - Action: Key "1" CHƯA expired (vừa đúng 5 phút)

// 5. Wait thêm 1 second... (10:07:01)
// → Timer chạy lại:
//   - Threshold = 10:07:01 - 5 min = 10:02:01
//   - Check: Key "1" có score = 1729033320 (10:02:00) < 10:02:01
//   - Action: Key "1" EXPIRED! → Xóa khỏi Redis
```

---

## Ưu điểm của kiến trúc này

### 1. **Persistence** ✅
- Access time được lưu trong Redis → Không mất khi restart C# app
- Data an toàn kể cả khi C# app crash

### 2. **Scalability** ✅
- Multiple instances của C# app có thể share cùng Redis
- Mỗi instance chạy timer riêng → Load balanced
- Redis Sorted Set cực kỳ hiệu quả cho range queries

### 3. **Performance** ✅
- `ZRANGEBYSCORE` rất nhanh (O(log(N) + M))
- Version cache trong C# giảm Redis round-trips
- Timer chạy async không block main thread

### 4. **Flexibility** ✅
- Có thể thay đổi TTL runtime
- Có thể query access time từ Redis
- Có thể manual cleanup nếu cần

---

## Redis commands để query

### 1. Xem tất cả keys và access time
```redis
ZRANGE map:products:access-time 0 -1 WITHSCORES

# Output:
# 1) "1"
# 2) "1729033200"
# 3) "2"
# 4) "1729033260"
```

### 2. Xem keys sắp hết hạn (TTL = 5 phút)
```redis
ZRANGEBYSCORE map:products:access-time -inf (now - 300) WITHSCORES
```

### 3. Xem access time của một key cụ thể
```redis
ZSCORE map:products:access-time "1"

# Output: "1729033200"
```

### 4. Count số keys đã hết hạn
```redis
ZCOUNT map:products:access-time -inf (now - 300)
```

---

## Lưu ý quan trọng

### 1. TTL Duration không lưu trong Redis
```csharp
_itemTtl = TimeSpan.FromMinutes(5); // Chỉ trong C# memory
```

**Hậu quả:**
- ❌ Nếu restart C# app → Mất TTL config → Cần set lại
- ✅ Access time vẫn còn trong Redis → Có thể recover

**Giải pháp:**
```csharp
// TODO: Lưu TTL config vào Redis
await db.StringSetAsync($"map:{mapName}:ttl-config", ttl.TotalSeconds);
```

### 2. Timer chạy local trên mỗi instance
- Nếu có 3 instances C# → 3 timers chạy song song
- Không có vấn đề vì Redis atomic operations
- Performance tốt hơn so với centralized timer

### 3. Access Time chỉ update khi Get/Set
- Không tự động update nếu không access
- Background update không được thực hiện

---

## Tóm tắt

| **Câu hỏi** | **Câu trả lời** |
|-------------|-----------------|
| TTL duration được lưu ở đâu? | C# memory (`_itemTtl`) - KHÔNG persistent |
| Access time được lưu ở đâu? | Redis Sorted Set - PERSISTENT |
| Data được lưu ở đâu? | Redis Hash - PERSISTENT |
| Version được lưu ở đâu? | C# memory (`_versionCache`) + Redis Hash |
| Ai chịu trách nhiệm xóa keys hết hạn? | C# Timer (`ProcessExpiration`) - Mỗi 1 giây |

**Kết luận:**
- ✅ **Access time LƯU TRONG REDIS** → Persistent, shareable
- ❌ **TTL duration LƯU TRONG C#** → Non-persistent, cần set lại khi restart
- ✅ **Data LƯU TRONG REDIS** → Persistent
- ⚠️ **Version cache trong C#** → Optimization only

---

## Cải tiến đề xuất

### 1. Lưu TTL config vào Redis
```csharp
public async Task SetItemExpiration(TimeSpan? ttl)
{
    _itemTtl = ttl;
    
    // Lưu config vào Redis để recover sau restart
    var db = _redis.GetDatabase(_database);
    if (ttl.HasValue)
    {
        await db.StringSetAsync(
            $"map:{_mapName}:ttl-config", 
            ttl.Value.TotalSeconds
        );
    }
    else
    {
        await db.KeyDeleteAsync($"map:{_mapName}:ttl-config");
    }
    
    // ... rest of code ...
}
```

### 2. Load TTL config từ Redis khi khởi tạo
```csharp
public RedisMap(...)
{
    // ... existing code ...
    
    // Load TTL config from Redis if exists
    var ttlConfig = db.StringGet($"map:{mapName}:ttl-config");
    if (ttlConfig.HasValue)
    {
        _itemTtl = TimeSpan.FromSeconds((double)ttlConfig);
        _expirationTimer = new Timer(...);
    }
}
```

### 3. Sử dụng Redis Keyspace Notifications (alternative)
```redis
CONFIG SET notify-keyspace-events Ex
```

Nhưng approach hiện tại (Sorted Set) tốt hơn vì:
- Control được chính xác timing
- Batch delete hiệu quả hơn
- Không phụ thuộc Redis notifications

---

## Kết luận cuối cùng

**TTL/Expiration logic trong CacheManager:**
- 🔴 **TTL Duration**: C# memory (non-persistent)
- 🟢 **Access Time**: Redis Sorted Set (persistent)
- 🟢 **Data**: Redis Hash (persistent)
- 🟡 **Version**: C# cache (optimization)

**Redis là nguồn chân lý (source of truth) cho access time và data, nhưng TTL duration cần được cấu hình lại sau khi restart C# application.**
