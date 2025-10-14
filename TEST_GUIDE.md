# Test Guide - TTL & Batch Update

## 📋 Mục lục
- [1. TTL (Time To Live) Test](#1-ttl-time-to-live-test)
- [2. Batch Update Test](#2-batch-update-test)

---

## 1. TTL (Time To Live) Test

### 🎯 Mục đích
Test tính năng tự động xóa các items không hoạt động sau 2 phút.

### 📦 Model: TempSession
```csharp
public class TempSession
{
    public string SessionId { get; set; }
    public string UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessAt { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
}
```

### 🔧 Cấu hình
```csharp
// Registered in CacheRegistrationBackgroundService
builder.CreateMap<string, TempSession>(
    mapName: "temp-sessions",
    expiration: null,              // Map không hết hạn
    itemTtl: TimeSpan.FromMinutes(2) // Items hết hạn sau 2 phút
);
```

### 📡 API Endpoints

#### 1. Tạo test sessions
```bash
POST /api/ttltest/create-sessions?count=10
```

**Response:**
```json
{
  "message": "Created 10 test sessions. They will expire after 2 minutes of inactivity.",
  "sessions": [
    {
      "sessionId": "sess-abc123...",
      "userId": "user-001",
      "createdAt": "2025-10-14T10:00:00Z",
      "lastAccessAt": "2025-10-14T10:00:00Z",
      "ipAddress": "192.168.1.100",
      "userAgent": "Test Browser"
    }
  ],
  "instructions": {
    "access": "Use GET /api/ttltest/access-session/{sessionId} to reset TTL",
    "check": "Use GET /api/ttltest/sessions to see active sessions",
    "note": "Sessions will auto-delete after 2 minutes without access"
  }
}
```

#### 2. Access session (reset TTL)
```bash
GET /api/ttltest/access-session/{sessionId}
```

**Response:**
```json
{
  "message": "Session accessed successfully. TTL reset to 2 minutes.",
  "session": {
    "sessionId": "sess-abc123...",
    "userId": "user-001",
    "lastAccessAt": "2025-10-14T10:05:00Z"
  },
  "ttlInfo": {
    "resetAt": "2025-10-14T10:05:00Z",
    "willExpireAt": "2025-10-14T10:07:00Z",
    "note": "If no access for 2 minutes, session will be deleted"
  }
}
```

#### 3. Xem active sessions
```bash
GET /api/ttltest/sessions?page=1&pageSize=20
```

#### 4. Clear tất cả sessions
```bash
DELETE /api/ttltest/sessions
```

### 🧪 Test Scenarios

#### Scenario 1: Normal Expiration
```bash
# 1. Tạo 5 sessions
curl -X POST "http://localhost:5011/api/ttltest/create-sessions?count=5"

# 2. Đợi 2 phút (không access)

# 3. Check logs - Sẽ thấy:
# ⏰ SESSION EXPIRED: sess-xxx | User: user-001 | Created: 10:00:00 | Last Access: 10:00:00
# ⏰ SESSION EXPIRED: sess-yyy | User: user-002 | Created: 10:00:00 | Last Access: 10:00:00

# 4. List sessions - Sẽ rỗng
curl "http://localhost:5011/api/ttltest/sessions"
```

#### Scenario 2: TTL Reset by Access
```bash
# 1. Tạo sessions
curl -X POST "http://localhost:5011/api/ttltest/create-sessions?count=3"

# 2. Sau 1 phút, access session đầu tiên
curl "http://localhost:5011/api/ttltest/access-session/sess-abc123"

# 3. Đợi thêm 1 phút (tổng 2 phút)
# → 2 sessions không access sẽ expire
# → Session được access vẫn còn (vì đã reset TTL)

# 4. Check logs:
# ⏰ SESSION EXPIRED: sess-yyy (2 phút không access)
# ⏰ SESSION EXPIRED: sess-zzz (2 phút không access)
# (sess-abc123 vẫn còn vì đã reset)
```

#### Scenario 3: Continuous Access
```bash
# 1. Tạo 1 session
curl -X POST "http://localhost:5011/api/ttltest/create-sessions?count=1"

# 2. Access mỗi 1 phút (trước khi expire)
curl "http://localhost:5011/api/ttltest/access-session/sess-abc123"
# Wait 1 minute
curl "http://localhost:5011/api/ttltest/access-session/sess-abc123"
# Wait 1 minute
curl "http://localhost:5011/api/ttltest/access-session/sess-abc123"

# → Session không bao giờ expire vì được access liên tục
```

### 📊 Expected Logs
```
info: Asp.Net.Test.Controllers.TtlTestController[0]
      Created 10 sessions with 2-minute TTL

# ... After 2 minutes of inactivity ...

warn: CacheManager.Core.RedisMap`2[System.String,Asp.Net.Test.Models.TempSession][0]
      ⏰ SESSION EXPIRED: sess-abc123... | User: user-001 | Created: 10:00:00 | Last Access: 10:00:00

warn: CacheManager.Core.RedisMap`2[System.String,Asp.Net.Test.Models.TempSession][0]
      ⏰ SESSION EXPIRED: sess-def456... | User: user-002 | Created: 10:00:05 | Last Access: 10:00:05
```

---

## 2. Batch Update Test

### 🎯 Mục đích
Test tính năng gom nhóm các updates trong khoảng thời gian 5 giây (default batch wait time).

### 📦 Model: Product
```csharp
public class Product
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime LastUpdated { get; set; }
    public int UpdateCount { get; set; }
}
```

### 🔧 Cấu hình
```csharp
// Registered in CacheRegistrationBackgroundService
builder.CreateMap<int, Product>("products");

// Batch wait time: 5 seconds (default)
// Auto-update: 5 random products every minute
```

### 🤖 Background Service: ProductUpdateBackgroundService

**Chức năng:**
1. Tạo 100 products ban đầu
2. Mỗi phút update ngẫu nhiên 5 products
3. Log batch updates khi triggered

**Code:**
```csharp
// Setup batch listener
productsMap.OnBatchUpdate(entries =>
{
    _logger.LogInformation("=== BATCH UPDATE TRIGGERED ===");
    _logger.LogInformation($"Total items in batch: {entries.Count()}");
    
    foreach (var entry in entries)
    {
        var product = entry.GetValue();
        _logger.LogInformation(
            $"  → Product #{product.ProductId}: {product.Name} | " +
            $"Price: ${product.Price:F2} | Stock: {product.Stock}"
        );
    }
});
```

### 📡 API Endpoints

#### 1. Xem tất cả products
```bash
GET /api/batchtest/products?page=1&pageSize=20
```

**Response:**
```json
{
  "mapName": "products",
  "data": {
    "entries": [...],
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 100,
    "totalPages": 5
  },
  "info": {
    "totalProducts": 100,
    "autoUpdate": "5 random products every minute",
    "batchWaitTime": "5 seconds",
    "note": "Check server logs to see batch update events"
  }
}
```

#### 2. Xem product by ID
```bash
GET /api/batchtest/products/{productId}
```

#### 3. Manual update (trigger batch)
```bash
POST /api/batchtest/update-products?count=5
```

**Response:**
```json
{
  "message": "Updated 5 products. Check server logs for batch update event.",
  "updatedProducts": [
    {
      "productId": 23,
      "name": "Product 23",
      "price": 45.67,
      "stock": 234,
      "lastUpdated": "2025-10-14T10:10:00Z",
      "updateCount": 5
    }
  ],
  "info": {
    "batchWaitTime": "5 seconds (default)",
    "note": "Batch update will be triggered after 5 seconds of inactivity"
  }
}
```

#### 4. Top updated products
```bash
GET /api/batchtest/products/top-updated?top=10
```

### 🧪 Test Scenarios

#### Scenario 1: Auto Batch Update (every minute)
```bash
# 1. Khởi động app - 100 products được tạo
# Log:
# info: Initializing 100 products...
# info: ✅ 100 products initialized successfully

# 2. Đợi 1 phút - 5 products được update tự động
# Log:
# info: 🔄 Starting random product updates...
# info:   Updated Product #23: Product 23 | New Price: $45.67 | New Stock: 234
# info:   Updated Product #67: Product 67 | New Price: $78.90 | New Stock: 456
# ... (3 more)
# info: ✅ Updated 5 products. Waiting 5 seconds for batch...

# 3. Sau 5 giây (batch wait time), batch update triggered
# Log:
# info: === BATCH UPDATE TRIGGERED ===
# info: Total items in batch: 5
# info:   → Product #23: Product 23 | Price: $45.67 | Stock: 234 | Updates: 1
# info:   → Product #67: Product 67 | Price: $78.90 | Stock: 456 | Updates: 1
# ... (3 more)
# info: ==============================
```

#### Scenario 2: Manual Batch Test
```bash
# 1. Update 3 products manually
curl -X POST "http://localhost:5011/api/batchtest/update-products?count=3"

# 2. Đợi 5 giây (batch wait time)

# 3. Check logs - Sẽ thấy batch triggered với 3 items
# Log:
# info: === BATCH UPDATE TRIGGERED ===
# info: Total items in batch: 3
# info:   → Product #12: Product 12 | Price: $23.45 | Stock: 678 | Updates: 2
# info:   → Product #34: Product 34 | Price: $56.78 | Stock: 890 | Updates: 1
# info:   → Product #56: Product 56 | Price: $89.01 | Stock: 123 | Updates: 3
# info: ==============================
```

#### Scenario 3: Multiple Updates in Window
```bash
# Test: Update nhiều products trong cửa sổ 5 giây

# 1. Update 2 products
curl -X POST "http://localhost:5011/api/batchtest/update-products?count=2"

# 2. Ngay lập tức (trong 3 giây), update thêm 3 products
curl -X POST "http://localhost:5011/api/batchtest/update-products?count=3"

# 3. Đợi 5 giây kể từ lần update cuối

# 4. Check logs - Sẽ thấy 1 batch với 5 items
# (Tất cả updates trong cửa sổ 5 giây được gom lại)
# Log:
# info: === BATCH UPDATE TRIGGERED ===
# info: Total items in batch: 5
# ... (all 5 products)
```

#### Scenario 4: Check Update Statistics
```bash
# Xem top 10 products được update nhiều nhất
curl "http://localhost:5011/api/batchtest/products/top-updated?top=10"

# Response:
{
  "topUpdatedProducts": [
    {
      "productId": 23,
      "name": "Product 23",
      "updateCount": 15  // Đã update 15 lần
    },
    {
      "productId": 67,
      "updateCount": 12
    }
    // ...
  ]
}
```

### 📊 Expected Logs

**Application Start:**
```
info: CacheManager.Core.CacheManagerRegistrationService[0]
      Starting CacheManager registration...
info: CacheManager.Core.CacheManagerRegistrationService[0]
      CacheManager registration completed successfully
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      Initializing 100 products...
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      ✅ 100 products initialized successfully
```

**Every Minute (Auto Update):**
```
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      🔄 Starting random product updates...
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        Updated Product #12: Product 12 | New Price: $45.67 | New Stock: 234
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        Updated Product #34: Product 34 | New Price: $78.90 | New Stock: 567
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        Updated Product #56: Product 56 | New Price: $23.45 | New Stock: 890
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        Updated Product #78: Product 78 | New Price: $67.89 | New Stock: 123
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        Updated Product #90: Product 90 | New Price: $34.56 | New Stock: 456
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      ✅ Updated 5 products. Waiting 5 seconds for batch...
```

**After 5 Seconds (Batch Triggered):**
```
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      === BATCH UPDATE TRIGGERED ===
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      Total items in batch: 5
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        → Product #12: Product 12 | Price: $45.67 | Stock: 234 | Updates: 3
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        → Product #34: Product 34 | Price: $78.90 | Stock: 567 | Updates: 2
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        → Product #56: Product 56 | Price: $23.45 | Stock: 890 | Updates: 5
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        → Product #78: Product 78 | Price: $67.89 | Stock: 123 | Updates: 1
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
        → Product #90: Product 90 | Price: $34.56 | Stock: 456 | Updates: 4
info: Asp.Net.Test.Services.ProductUpdateBackgroundService[0]
      ==============================
```

---

## 🎯 Testing Checklist

### TTL Test
- [ ] Tạo sessions và verify chúng tồn tại
- [ ] Đợi 2 phút, verify sessions tự động expire
- [ ] Access session, verify TTL reset
- [ ] Check logs xem expiration callbacks
- [ ] Clear sessions manually

### Batch Update Test
- [ ] Verify 100 products được tạo ban đầu
- [ ] Đợi 1 phút, verify auto-update 5 products
- [ ] Check logs xem batch update events
- [ ] Manual update products, verify batch triggered
- [ ] Test multiple updates trong batch window
- [ ] Check top updated products statistics

---

## 🚀 Quick Start

```bash
# 1. Start application
dotnet run --project Asp.Net.Test

# 2. Open Swagger UI
http://localhost:5011/swagger

# 3. Test TTL
curl -X POST "http://localhost:5011/api/ttltest/create-sessions?count=5"

# 4. Test Batch Update
curl -X POST "http://localhost:5011/api/batchtest/update-products?count=3"

# 5. Watch logs in terminal
# - TTL expirations after 2 minutes
# - Batch updates after 5 seconds
# - Auto product updates every minute
```

**Happy Testing! 🎉**
