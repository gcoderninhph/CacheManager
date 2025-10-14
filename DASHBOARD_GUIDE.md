# CacheManager Dashboard - Hướng dẫn sử dụng

## 🎯 Tính năng đã triển khai

### 1. **Giao diện Dashboard**
- **Navigation bên trái (30% width)**:
  - Tabs: Map, Bucket, Pub/Sub, Settings
  - Danh sách các Map/Bucket đã đăng ký
  - Click vào map name để xem dữ liệu

- **Content bên phải (70% width)**:
  - **Search box**: Tìm kiếm theo key (debounce 300ms)
  - **Bảng dữ liệu**: Key, Value, Version (3 cột)
  - **Pagination**: Previous/Next buttons, Page info
  - **Nút Refresh**: Cập nhật dữ liệu mới nhất
  - **Giới hạn**: 20 records/page

### 2. **API Endpoints**
- `GET /cache-manager/api/registry` - Lấy danh sách tất cả maps và buckets
- `GET /cache-manager/api/map/{mapName}?page=1&pageSize=20&search=keyword` - Lấy dữ liệu map với pagination và search

### 3. **Cách sử dụng**

#### Đăng ký Maps và Buckets
```csharp
// Trong Program.cs
var registerService = app.Services.GetRequiredService<ICacheRegisterService>();
registerService.RegisterBuilder()
    .CreateMap<string, string>("user-sessions")
    .CreateMap<int, string>("user-data")
    .CreateBucket<string>("logs")
    .Build();
```

#### Thêm dữ liệu vào Map
```csharp
// Inject ICacheStorage
var storage = app.Services.GetRequiredService<ICacheStorage>();

// Lấy map và thêm dữ liệu
var map = storage.GetMap<string, string>("user-sessions");
await map.SetValueAsync("user123", "session-token-abc");
await map.SetValueAsync("user456", "session-token-xyz");
```

#### Xem Dashboard
1. Chạy ứng dụng: `dotnet run --project Asp.Net.Test\Asp.Net.Test.csproj`
2. Truy cập: `http://localhost:5011/cache-manager`
3. Chọn tab "Map"
4. Click vào tên map trong nav bên trái
5. Xem dữ liệu hiển thị bên phải

### 4. **Cấu trúc dữ liệu**

#### MapEntryData (dữ liệu hiển thị)
```csharp
{
    "key": "user123",           // Key của entry
    "value": "session-token-abc", // Value của entry  
    "version": "a1b2c3d4"        // Version GUID (8 ký tự đầu)
}
```

### 5. **Tính năng Version Tracking**
- Mỗi entry có một Version GUID duy nhất
- Version được tạo mới khi có update
- Dashboard chỉ hiển thị 8 ký tự đầu của GUID

### 6. **Batch Update Logic**
- Timer check mỗi 1 giây
- Gom tất cả entries có thay đổi sau khoảng thời gian chờ (mặc định 5s)
- Trigger event `OnBatchUpdate` với danh sách entries đã thay đổi

### 7. **Cấu hình**

#### appsettings.json
```json
{
  "CacheManager": {
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 5
  }
}
```

#### Program.cs
```csharp
builder.Services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.RedisDatabase = 0;
    options.BatchWaitTimeSeconds = 5;  // Thời gian chờ batch
});
```

## 🚀 Quick Start

### 1. Khởi động Redis
```bash
docker run -d -p 6379:6379 redis:latest
```

### 2. Chạy ứng dụng
```bash
cd e:\Ninh\CSharp\CacheManager
.\run_aspnet.cmd
```

### 3. Test với sample data
```csharp
// Thêm endpoint test trong Program.cs
app.MapGet("/test/add-data", async (ICacheStorage storage) =>
{
    var map = storage.GetMap<string, string>("user-sessions");
    await map.SetValueAsync("user1", "token-abc-123");
    await map.SetValueAsync("user2", "token-def-456");
    await map.SetValueAsync("user3", "token-ghi-789");
    return "Data added!";
});
```

Truy cập `http://localhost:5011/test/add-data` để thêm dữ liệu test.

### 4. Xem Dashboard
Truy cập `http://localhost:5011/cache-manager`

## 📝 Lưu ý

1. **Redis phải đang chạy** trước khi khởi động ứng dụng
2. **Hard refresh browser** (Ctrl+Shift+R) nếu CSS không cập nhật
3. **Maps phải được đăng ký** trước khi sử dụng
4. **Version tracking** chỉ hoạt động cho entries được set thông qua `SetValueAsync`

## 🎨 Theme

Dashboard sử dụng dark theme với:
- Background: Gradient từ #0a0e1a → #1a1f2e
- Primary color: #3b82f6 (blue)
- Accent color: #06b6d4 (cyan)
- Gradient effects cho logo, buttons, text
- Custom scrollbar
- Hover animations

Enjoy! 🎉
