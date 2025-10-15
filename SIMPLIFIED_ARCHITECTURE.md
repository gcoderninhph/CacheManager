# Simplified Architecture - Loại bỏ Registration Pattern

## 📋 Tổng quan

Đã đơn giản hóa architecture bằng cách loại bỏ **Builder/Registration Pattern** và chỉ sử dụng **`GetOrCreateMapAsync`** duy nhất để tạo và truy cập maps.

---

## 🔄 Thay đổi chính

### ❌ Đã xóa:

1. **`IRegisterBuilder` interface** - Builder pattern không cần thiết
2. **`ICacheRegisterService` interface** - Service đăng ký map
3. **`RegisterService.cs`** - Implementation của registration pattern
4. **`CacheManagerRegistrationService.cs`** - Abstract base class cho registration
5. **`ICacheStorage.GetMap<TKey, TValue>`** - Synchronous getter

### ✅ Đã thêm/sửa:

1. **`ICacheStorage.GetOrCreateMapAsync<TKey, TValue>(string mapName, TimeSpan? itemTtl = null)`**
   - Tạo map nếu chưa tồn tại
   - Cấu hình TTL ngay khi tạo
   - Async-first design

2. **`RedisCacheStorage.RegisterMap<TKey, TValue>(string mapName, TimeSpan? itemTtl = null)`**
   - Internal method để tạo map
   - Tự động set TTL nếu có

---

## 📝 Cách sử dụng mới

### ❌ TRƯỚC (Builder Pattern):

```csharp
// 1. Inject ICacheRegisterService
public class CacheRegistrationBackgroundService : CacheManagerRegistrationService
{
    public CacheRegistrationBackgroundService(
        ICacheRegisterService registerService,
        ILogger<CacheManagerRegistrationService> logger)
        : base(registerService, logger)
    {
    }

    protected override void ConfigureCache(IRegisterBuilder builder)
    {
        // Đăng ký maps
        builder.CreateMap<string, string>("user-sessions");
        builder.CreateMap<string, TempSession>("temp-sessions", TimeSpan.FromMinutes(2));
        builder.Build();
    }
}

// 2. Sử dụng GetMap (synchronous)
public class MyController : ControllerBase
{
    private readonly ICacheStorage _storage;
    
    public IActionResult GetUser(string id)
    {
        var map = _storage.GetMap<string, UserInfo>("user-info");
        var user = await map.GetValueAsync(id);
        return Ok(user);
    }
}
```

### ✅ SAU (GetOrCreateMapAsync):

```csharp
// 1. Khởi tạo maps trong Background Service
public class CacheRegistrationBackgroundService : BackgroundService
{
    private readonly ICacheStorage _storage;
    private readonly ILogger<CacheRegistrationBackgroundService> _logger;

    public CacheRegistrationBackgroundService(
        ICacheStorage storage,
        ILogger<CacheRegistrationBackgroundService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting CacheManager initialization...");

        // Tạo maps với GetOrCreateMapAsync
        await _storage.GetOrCreateMapAsync<string, string>("user-sessions");
        await _storage.GetOrCreateMapAsync<string, TempSession>("temp-sessions", TimeSpan.FromMinutes(2));
        await _storage.GetOrCreateMapAsync<int, Product>("products");

        _logger.LogInformation("CacheManager initialization completed");
    }
}

// 2. Sử dụng GetOrCreateMapAsync (async)
public class MyController : ControllerBase
{
    private readonly ICacheStorage _storage;
    
    public async Task<IActionResult> GetUser(string id)
    {
        var map = await _storage.GetOrCreateMapAsync<string, UserInfo>("user-info");
        var user = await map.GetValueAsync(id);
        return Ok(user);
    }
}
```

---

## 🎯 Ưu điểm

### 1. **Đơn giản hơn**
- ❌ Không cần hiểu Builder Pattern
- ❌ Không cần abstract base class
- ✅ Chỉ cần 1 method: `GetOrCreateMapAsync`

### 2. **Async-first**
- ✅ Tất cả operations đều async
- ✅ Phù hợp với modern ASP.NET Core

### 3. **Lazy Loading**
- ✅ Map chỉ được tạo khi cần thiết
- ✅ TTL được cấu hình ngay lúc tạo

### 4. **Ít code hơn**
```
Trước: 4 files (RegisterService.cs, CacheManagerRegistrationService.cs, etc.)
Sau:  1 method (GetOrCreateMapAsync)
```

---

## 📦 Dependency Injection

### ❌ TRƯỚC:

```csharp
services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
});

// 3 services được đăng ký:
// - ICacheStorage
// - ICacheRegisterService
// - RedisCacheStorage
```

### ✅ SAU:

```csharp
services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
});

// 2 services được đăng ký:
// - ICacheStorage
// - RedisCacheStorage
```

---

## 🔧 Migration từ code cũ

### Step 1: Xóa CacheRegistrationBackgroundService kế thừa

```diff
- public class CacheRegistrationBackgroundService : CacheManagerRegistrationService
+ public class CacheRegistrationBackgroundService : BackgroundService
{
-   public CacheRegistrationBackgroundService(
-       ICacheRegisterService registerService,
-       ILogger<CacheManagerRegistrationService> logger)
-       : base(registerService, logger)
+   public CacheRegistrationBackgroundService(
+       ICacheStorage storage,
+       ILogger<CacheRegistrationBackgroundService> logger)
    {
+       _storage = storage;
+       _logger = logger;
    }
```

### Step 2: Thay ConfigureCache bằng ExecuteAsync

```diff
- protected override void ConfigureCache(IRegisterBuilder builder)
+ protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
-   builder.CreateMap<string, string>("user-sessions");
-   builder.CreateMap<string, TempSession>("temp-sessions", TimeSpan.FromMinutes(2));
-   builder.Build();
+   await _storage.GetOrCreateMapAsync<string, string>("user-sessions");
+   await _storage.GetOrCreateMapAsync<string, TempSession>("temp-sessions", TimeSpan.FromMinutes(2));
}
```

### Step 3: Thay GetMap bằng GetOrCreateMapAsync

```diff
public async Task<IActionResult> GetUser(string id)
{
-   var map = _storage.GetMap<string, UserInfo>("user-info");
+   var map = await _storage.GetOrCreateMapAsync<string, UserInfo>("user-info");
    var user = await map.GetValueAsync(id);
    return Ok(user);
}
```

---

## ✅ Files đã được cập nhật

### Core:
- ✅ `Core/CacheStorage.cs` - Added `itemTtl` parameter to `GetOrCreateMapAsync`
- ✅ `Core/Class1.cs` - Removed `ICacheRegisterService` registration
- ❌ `Core/RegisterService.cs` - **DELETED**
- ❌ `Core/CacheManagerRegistrationService.cs` - **DELETED**

### Asp.Net.Test:
- ✅ `Services/CacheRegistrationBackgroundService.cs` - Chuyển sang `BackgroundService`
- ✅ `Services/ProductUpdateBackgroundService.cs` - `GetMap` → `GetOrCreateMapAsync`
- ✅ `Controllers/TestController.cs` - `GetMap` → `GetOrCreateMapAsync`
- ✅ `Controllers/TtlTestController.cs` - `GetMap` → `GetOrCreateMapAsync`
- ✅ `Controllers/UserInfoController.cs` - `GetMap` → `GetOrCreateMapAsync`
- ✅ `Controllers/MapController.cs` - `GetMap` → `GetOrCreateMapAsync`
- ✅ `Controllers/BatchTestController.cs` - `GetMap` → `GetOrCreateMapAsync`

---

## 🚀 Kết luận

Architecture mới **đơn giản hơn**, **dễ hiểu hơn**, và **async-first**. 

**Quy tắc vàng:**
> Chỉ dùng `GetOrCreateMapAsync` để tạo và truy cập maps. Không có pattern nào khác!

```csharp
// ✅ ĐÚNG
var map = await _storage.GetOrCreateMapAsync<string, UserInfo>("user-info");

// ✅ ĐÚNG (với TTL)
var map = await _storage.GetOrCreateMapAsync<string, UserInfo>("user-info", TimeSpan.FromMinutes(30));

// ❌ SAI (method này không còn tồn tại)
var map = _storage.GetMap<string, UserInfo>("user-info");

// ❌ SAI (pattern này đã bị xóa)
builder.CreateMap<string, UserInfo>("user-info");
```
