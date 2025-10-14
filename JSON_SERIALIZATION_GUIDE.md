# JSON Serialization Guide

## 📋 Overview

CacheManager sử dụng **System.Text.Json** để serialize/deserialize dữ liệu khi lưu vào Redis. Tất cả Map và Bucket đều mặc định sử dụng JSON format.

## 🎯 JSON Configuration

### Default Settings

CacheManager sử dụng các settings sau cho JSON serialization:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = false,                    // Compact format (không xuống dòng)
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,  // camelCase cho properties
    PropertyNameCaseInsensitive = true,       // Case-insensitive khi deserialize
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull  // Bỏ qua null values
};
```

### Benefits

| Feature | Description | Example |
|---------|-------------|---------|
| **Compact Format** | Không indent, tiết kiệm bandwidth | `{"userId":"user1","name":"John"}` |
| **camelCase** | Property names theo convention JavaScript | `userId` thay vì `UserId` |
| **Case Insensitive** | Deserialize linh hoạt | `UserId`, `userid`, `USERID` đều được |
| **Ignore Null** | Không lưu trường null, giảm kích thước | `{"name":"John"}` thay vì `{"name":"John","age":null}` |

---

## 🔧 How It Works

### Map Serialization

**RedisMap.cs:**
```csharp
// Serialize Key (TKey -> JSON string)
private string SerializeKey(TKey key) => JsonSerializer.Serialize(key, JsonOptions);

// Serialize Value (TValue -> JSON string)
private string SerializeValue(TValue value) => JsonSerializer.Serialize(value, JsonOptions);

// Deserialize Value (JSON string -> TValue)
private TValue DeserializeValue(string json) => 
    JsonSerializer.Deserialize<TValue>(json, JsonOptions) 
    ?? throw new InvalidOperationException("Failed to deserialize value");
```

### Bucket Serialization

**RedisBucket.cs:**
```csharp
// Serialize khi Add
public async Task AddAsync(TValue value)
{
    var serializedValue = JsonSerializer.Serialize(value, JsonOptions);
    await db.ListRightPushAsync(listKey, serializedValue);
}

// Deserialize khi Pop/GetAll
public async Task<TValue?> PopAsync()
{
    var value = await db.ListLeftPopAsync(listKey);
    return JsonSerializer.Deserialize<TValue>(value!, JsonOptions);
}
```

---

## 📊 Examples

### 1. Simple Types

**String Map:**
```csharp
var map = storage.GetMap<string, string>("user-sessions");
await map.SetValueAsync("user1", "session-abc-123");
```

**Redis Storage:**
```
HSET map:user-sessions "\"user1\"" "\"session-abc-123\""
```

**Note:** String được quote kép vì là valid JSON.

---

### 2. Complex Objects

**UserInfo Example:**
```csharp
public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; } = 0;
}

var map = storage.GetMap<string, UserInfo>("user-info");
await map.SetValueAsync("user-001", new UserInfo
{
    UserId = "user-001",
    Name = "John Doe",
    Age = 30
});
```

**Redis Storage:**
```
HSET map:user-info "\"user-001\"" "{\"userId\":\"user-001\",\"name\":\"John Doe\",\"age\":30}"
```

**Key Features:**
- ✅ Properties serialize as **camelCase** (`userId`, `name`, `age`)
- ✅ Compact format (single line)
- ✅ UTF-8 encoding support

---

### 3. Nested Objects

**Nested Model:**
```csharp
public class UserProfile
{
    public string UserId { get; set; }
    public PersonalInfo Info { get; set; }
    public List<string> Tags { get; set; }
}

public class PersonalInfo
{
    public string Name { get; set; }
    public int Age { get; set; }
    public Address Address { get; set; }
}

public class Address
{
    public string City { get; set; }
    public string Country { get; set; }
}
```

**Usage:**
```csharp
var map = storage.GetMap<string, UserProfile>("user-profiles");
await map.SetValueAsync("user-001", new UserProfile
{
    UserId = "user-001",
    Info = new PersonalInfo
    {
        Name = "John Doe",
        Age = 30,
        Address = new Address
        {
            City = "Hanoi",
            Country = "Vietnam"
        }
    },
    Tags = new List<string> { "premium", "verified" }
});
```

**Redis Storage:**
```json
{
  "userId": "user-001",
  "info": {
    "name": "John Doe",
    "age": 30,
    "address": {
      "city": "Hanoi",
      "country": "Vietnam"
    }
  },
  "tags": ["premium", "verified"]
}
```

---

### 4. Collections

**List Serialization:**
```csharp
var map = storage.GetMap<string, List<string>>("user-tags");
await map.SetValueAsync("user-001", new List<string> { "admin", "premium", "verified" });
```

**Redis Storage:**
```
HSET map:user-tags "\"user-001\"" "[\"admin\",\"premium\",\"verified\"]"
```

**Dictionary Serialization:**
```csharp
var map = storage.GetMap<string, Dictionary<string, int>>("user-scores");
await map.SetValueAsync("user-001", new Dictionary<string, int>
{
    { "math", 95 },
    { "science", 88 },
    { "english", 92 }
});
```

**Redis Storage:**
```json
{
  "math": 95,
  "science": 88,
  "english": 92
}
```

---

## 🔍 Querying JSON in Redis

### Using redis-cli

```bash
# Connect to Redis
redis-cli

# Get map keys
HKEYS map:user-info

# Get specific user
HGET map:user-info "\"user-001\""

# Get all users
HGETALL map:user-info

# Count users
HLEN map:user-info

# Search by pattern
HSCAN map:user-info 0 MATCH "*001*"
```

### Using Redis Insight

1. Open Redis Insight
2. Connect to `localhost:6379`
3. Browse to `map:user-info`
4. View formatted JSON in GUI

---

## 💡 Best Practices

### 1. ✅ Use DTOs for Clean JSON

**Good:**
```csharp
public class UserDto
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
}
```

**Bad:**
```csharp
public class User
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public DbContext Context { get; set; }  // ❌ Circular reference!
    public byte[] PasswordHash { get; set; } // ❌ Sensitive data!
}
```

### 2. ✅ Use JsonIgnore for Sensitive Data

```csharp
public class UserInfo
{
    public string UserId { get; set; }
    public string Name { get; set; }
    
    [JsonIgnore]
    public string PasswordHash { get; set; }  // Not serialized
    
    [JsonIgnore]
    public string SecurityToken { get; set; } // Not serialized
}
```

### 3. ✅ Handle Null Values

```csharp
public class UserProfile
{
    public string UserId { get; set; } = string.Empty;  // Default value
    public string? NickName { get; set; }               // Nullable
    public int Age { get; set; } = 0;                   // Default value
}
```

**Serialized (age null will be omitted):**
```json
{
  "userId": "user-001",
  "age": 0
}
```

### 4. ✅ Use Proper Types

```csharp
// ✅ Good - Typed
public class Event
{
    public DateTime Timestamp { get; set; }
    public EventType Type { get; set; }
    public int Count { get; set; }
}

// ❌ Bad - Everything string
public class Event
{
    public string Timestamp { get; set; }
    public string Type { get; set; }
    public string Count { get; set; }
}
```

### 5. ✅ Version Your Models

```csharp
public class UserInfo
{
    public int Version { get; set; } = 2;  // Schema version
    public string UserId { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    
    // Added in v2
    public string? Email { get; set; }
}
```

---

## 🎨 Custom Serialization

### Using JsonConverter

```csharp
public class DateOnlyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateOnly.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
    }
}

// Usage
public class UserInfo
{
    public string UserId { get; set; }
    
    [JsonConverter(typeof(DateOnlyConverter))]
    public DateOnly BirthDate { get; set; }
}
```

### Using JsonPropertyName

```csharp
public class UserInfo
{
    [JsonPropertyName("id")]
    public string UserId { get; set; }
    
    [JsonPropertyName("full_name")]
    public string Name { get; set; }
    
    [JsonPropertyName("years_old")]
    public int Age { get; set; }
}
```

**Serialized:**
```json
{
  "id": "user-001",
  "full_name": "John Doe",
  "years_old": 30
}
```

---

## 📏 Size Optimization

### Comparison

| Approach | Size | Example |
|----------|------|---------|
| **Full Names** | 78 bytes | `{"UserId":"user-001","Name":"John Doe","Age":30}` |
| **camelCase** | 73 bytes | `{"userId":"user-001","name":"John Doe","age":30}` |
| **Short Names** | 52 bytes | `{"id":"user-001","n":"John Doe","a":30}` |
| **No Nulls** | 45 bytes | `{"id":"user-001","n":"John Doe"}` (omit age:null) |

**Recommendation:** Stick with camelCase (default) for readability. Only optimize if you have millions of records.

---

## 🔒 Security Considerations

### 1. ⚠️ Don't Store Sensitive Data

```csharp
// ❌ BAD
public class User
{
    public string Password { get; set; }
    public string CreditCard { get; set; }
    public string SSN { get; set; }
}

// ✅ GOOD - Use references
public class User
{
    public string UserId { get; set; }
    public string PasswordHashId { get; set; }  // Reference to secure store
}
```

### 2. ⚠️ Validate Input

```csharp
public class UserInfo
{
    [Required]
    [StringLength(50)]
    public string UserId { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; }
    
    [Range(0, 150)]
    public int Age { get; set; }
}
```

### 3. ⚠️ Handle Deserialization Errors

```csharp
try
{
    var user = await map.GetValueAsync("user-001");
}
catch (JsonException ex)
{
    _logger.LogError(ex, "Failed to deserialize user data");
    // Handle corrupted data
}
```

---

## 🧪 Testing

### Test Serialization Roundtrip

```csharp
[Fact]
public async Task UserInfo_ShouldSerializeAndDeserialize()
{
    // Arrange
    var original = new UserInfo
    {
        UserId = "user-001",
        Name = "John Doe",
        Age = 30
    };
    
    var map = _storage.GetMap<string, UserInfo>("test-map");
    
    // Act
    await map.SetValueAsync(original.UserId, original);
    var retrieved = await map.GetValueAsync(original.UserId);
    
    // Assert
    Assert.Equal(original.UserId, retrieved.UserId);
    Assert.Equal(original.Name, retrieved.Name);
    Assert.Equal(original.Age, retrieved.Age);
}
```

### Verify Redis Storage

```csharp
[Fact]
public async Task UserInfo_ShouldStoreAsJson()
{
    // Arrange
    var user = new UserInfo
    {
        UserId = "user-001",
        Name = "John Doe",
        Age = 30
    };
    
    var map = _storage.GetMap<string, UserInfo>("test-map");
    
    // Act
    await map.SetValueAsync(user.UserId, user);
    
    // Verify via raw Redis
    var db = _redis.GetDatabase();
    var json = await db.HashGetAsync("map:test-map", "\"user-001\"");
    
    // Assert
    Assert.Contains("\"userId\":\"user-001\"", json.ToString());
    Assert.Contains("\"name\":\"John Doe\"", json.ToString());
    Assert.Contains("\"age\":30", json.ToString());
}
```

---

## 📚 Related Documentation

- [README.md](README.md) - Project overview
- [USERINFO_API_GUIDE.md](USERINFO_API_GUIDE.md) - UserInfo CRUD API
- [CONFIGURATION_GUIDE.md](CONFIGURATION_GUIDE.md) - Configuration setup
- [OPTIMIZATION_GUIDE.md](OPTIMIZATION_GUIDE.md) - Performance optimization

---

## 🎯 Summary

✅ **CacheManager sử dụng JSON mặc định**
- System.Text.Json
- camelCase properties
- Compact format
- Case-insensitive deserialize
- Null values ignored

✅ **Best Practices:**
- Use DTOs for clean models
- Avoid circular references
- Don't store sensitive data
- Version your models
- Validate input/output

✅ **Performance:**
- Compact JSON saves bandwidth
- Fast serialization with System.Text.Json
- Optimized for Redis storage

---

Created: 2024
Author: CacheManager Team
