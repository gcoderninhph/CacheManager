# 🎯 CacheManager - Redis Cache Management System

[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Redis](https://img.shields.io/badge/Redis-7.0+-red.svg)](https://redis.io/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

Một hệ thống quản lý Redis cache mạnh mẽ với dashboard web, Swagger API, và batch update support cho ASP.NET Core.

## ✨ Tính năng chính

### 🎨 Dashboard Web
- **Dark Theme UI** với gradient effects hiện đại
- **Real-time monitoring** của Maps và Buckets
- **Pagination** - 20 records/page với Previous/Next
- **Search** - Tìm kiếm theo key với debounce
- **Responsive design** - Tương thích mobile

### 🚀 CRUD API
- **RESTful endpoints** đầy đủ cho Map operations
- **Swagger UI** - Giao diện test API tích hợp
- **OpenAPI specification** - Tài liệu API tự động
- **Pagination support** - Query parameters linh hoạt

### ⚡ Core Features
- **Batch Update** - Gom events trong khoảng thời gian configurable
- **Version Tracking** - Mỗi entry có GUID version unique
- **Event Handlers** - OnAdd, OnUpdate, OnRemove, OnClear, OnBatchUpdate
- **Redis Backend** - Hash cho Maps, List cho Buckets
- **DI Integration** - Hoàn toàn tích hợp ASP.NET Core DI

## 🏗️ Kiến trúc

```
CacheManager/
├── Core/                          # Class library chính
│   ├── Dashboard/                 # Embedded web UI
│   │   ├── index.html            # Dashboard layout
│   │   ├── styles.css            # Dark theme CSS
│   │   └── app.js                # Client-side logic
│   ├── Class1.cs                 # Service registration & routing
│   ├── Map.cs                    # IMap interface
│   ├── RedisMap.cs               # Redis Hash implementation
│   ├── RedisBucket.cs            # Redis List implementation
│   ├── CacheStorage.cs           # Registry cho maps/buckets
│   └── RegisterService.cs        # Builder pattern registration
│
├── Asp.Net.Test/                 # Test application
│   ├── Program.cs                # Startup & API endpoints
│   └── Pages/                    # Razor pages
│
├── API_GUIDE.md                  # Swagger & CRUD API docs
├── CONFIGURATION_GUIDE.md        # Configuration & Background Service
├── DASHBOARD_GUIDE.md            # Dashboard usage guide
├── PAGINATION_GUIDE.md           # Pagination & search details
├── OPTIMIZATION_GUIDE.md         # Performance optimization guide
└── README.md                     # This file
```

## 🚀 Quick Start

### Prerequisites
- .NET 9.0 SDK
- Redis Server (localhost:6379)
- Docker (optional, cho Redis)

### 1. Start Redis
```bash
# Using Docker
docker run -d -p 6379:6379 redis:latest

# Or install Redis locally
```

### 2. Clone & Build
```bash
cd e:\Ninh\CSharp\CacheManager
dotnet build CacheManager.sln
```

### 3. Run Application
```bash
# Windows
.\run_aspnet.cmd

# Or directly
dotnet run --project Asp.Net.Test\Asp.Net.Test.csproj
```

### 4. Access Endpoints

#### Dashboard
```
http://localhost:5011/cache-manager
```

#### Swagger UI
```
http://localhost:5011/swagger
```

#### Add Test Data
```
http://localhost:5011/test/add-data
```

## 📖 Hướng dẫn sử dụng

### Cấu hình từ appsettings.json (Recommended)

**appsettings.json:**
```json
{
  "CacheManager": {
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 5,
    "DashboardPath": "/cache-manager"
  }
}
```

**Program.cs:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Read configuration from appsettings.json
builder.Services.AddCacheManager(builder.Configuration);

// Register via Background Service
builder.Services.AddHostedService<CacheRegistrationBackgroundService>();

var app = builder.Build();

// Auto-enable dashboard (reads path from config)
app.UseCacheManagerDashboard();
```

**Services/CacheRegistrationBackgroundService.cs:**
```csharp
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
        builder.CreateMap<string, string>("user-sessions");
        builder.CreateMap<int, string>("user-data");
        builder.CreateBucket<string>("logs");
    }
}
```

> 📘 **Chi tiết**: Xem [CONFIGURATION_GUIDE.md](CONFIGURATION_GUIDE.md)

### Cấu hình thủ công (Legacy)

```csharp
// Program.cs
builder.Services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.RedisDatabase = 0;
    options.BatchWaitTimeSeconds = 5;
});

var app = builder.Build();

var registerService = app.Services.GetRequiredService<ICacheRegisterService>();
registerService.RegisterBuilder()
    .CreateMap<string, string>("user-sessions")
    .CreateBucket<string>("logs")
    .Build();

app.CacheManagerView("/cache-manager");
```

### Sử dụng Map

```csharp
// Inject ICacheStorage
var storage = app.Services.GetRequiredService<ICacheStorage>();

// Lấy map
var map = storage.GetMap<string, string>("user-sessions");

// Set value
await map.SetValueAsync("user1", "session-token-abc");

// Get value
var value = await map.GetValueAsync("user1");

// Event handlers
map.OnAdd((key, value) => 
{
    Console.WriteLine($"Added: {key} = {value}");
});

map.OnBatchUpdate(entries => 
{
    Console.WriteLine($"Batch update: {entries.Count()} entries");
});
```

### CRUD API Examples

```bash
# CREATE - Thêm mới
curl -X POST "http://localhost:5011/api/map/user-sessions/user1?value=token-123"

# READ - Lấy giá trị
curl -X GET "http://localhost:5011/api/map/user-sessions/user1"

# UPDATE - Cập nhật
curl -X PUT "http://localhost:5011/api/map/user-sessions/user1?value=token-456"

# DELETE - Xóa tất cả
curl -X DELETE "http://localhost:5011/api/map/user-sessions"

# LIST - Lấy tất cả (pagination)
curl -X GET "http://localhost:5011/api/map/user-sessions?page=1&pageSize=20"
```

## 🎨 Dashboard Features

### Navigation Panel (Left)
- **Tabs**: Map, Bucket, Pub/Sub, Settings
- **Map List**: Clickable list của registered maps
- **Active State**: Highlight map đang được chọn

### Content Panel (Right)
- **Search Box**: Tìm theo key, debounce 300ms
- **Data Table**: 3 columns (Key, Value, Version)
- **Pagination**: Previous/Next buttons, page info
- **Refresh Button**: Reload data from Redis

### UI Highlights
- 🌙 **Dark Theme**: Modern gradient background
- 🎨 **Glass Morphism**: Transparent panels với blur
- ⚡ **Smooth Animations**: Hover effects, transitions
- 📱 **Responsive**: Mobile-friendly breakpoints

## 🔧 Configuration

### appsettings.json
```json
{
  "CacheManager": {
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 5
  }
}
```

### Program.cs
```csharp
builder.Services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.RedisDatabase = 0;
    options.BatchWaitTimeSeconds = 5;
    options.DashboardPath = "/cache-manager";
});
```

## 📚 Documentation

- **[API_GUIDE.md](API_GUIDE.md)** - Swagger & CRUD API documentation
- **[DASHBOARD_GUIDE.md](DASHBOARD_GUIDE.md)** - Dashboard usage guide
- **[PAGINATION_GUIDE.md](PAGINATION_GUIDE.md)** - Pagination & search details

## 🏷️ API Endpoints

### Map CRUD
- `GET /api/map/{mapName}/{key}` - Get value by key
- `POST /api/map/{mapName}/{key}?value={value}` - Create/Update
- `PUT /api/map/{mapName}/{key}?value={value}` - Update
- `DELETE /api/map/{mapName}` - Clear all
- `GET /api/map/{mapName}?page=1&pageSize=20` - List all (paginated)

### Dashboard API
- `GET /cache-manager/api/registry` - Get all maps/buckets
- `GET /cache-manager/api/map/{mapName}?page=1&pageSize=20&search=keyword` - Get map data

### Testing
- `GET /test/add-data` - Add 50+ test records

## 🎯 Batch Update Logic

```csharp
// Timer check mỗi 1 giây
// Gom tất cả entries có thay đổi sau 5 giây (configurable)
map.OnBatchUpdate(entries => 
{
    Console.WriteLine($"Batch: {entries.Count()} entries changed");
    // Process batch...
});
```

**Flow:**
1. SetValueAsync → Update version cache với timestamp
2. Timer check mỗi 1s
3. Nếu (now - LastUpdated) >= BatchWaitTime (5s)
4. Gom vào batch và trigger OnBatchUpdate
5. Clear version cache cho các entries đã xử lý

## 🔄 Version Tracking

Mỗi entry có:
```csharp
{
    Key: "user1",
    Value: "session-token",
    Version: "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    LastUpdated: "2025-10-14T10:30:00Z"
}
```

Dashboard hiển thị 8 ký tự đầu của Version GUID.

## 🧪 Testing

### 1. Thêm dữ liệu test
```
http://localhost:5011/test/add-data
```

### 2. Xem Dashboard
```
http://localhost:5011/cache-manager
```

### 3. Test API với Swagger
```
http://localhost:5011/swagger
```

### 4. Test CRUD
- POST để create
- GET để verify
- PUT để update
- DELETE để clear
- GET list để xem all

## 📊 Performance

- **Optimized Pagination**: HSCAN cursor-based (1,600x faster for large datasets)
- **Memory Efficient**: 50,000x reduction (200MB → 4KB for 1M records)
- **Page Size**: 20 records (optimal UX)
- **Search Debounce**: 300ms (reduce API calls)
- **Redis Backend**: High-performance cache
- **Batch Processing**: 1s check interval, 5s configurable wait time

> 📘 **Chi tiết**: Xem [OPTIMIZATION_GUIDE.md](OPTIMIZATION_GUIDE.md)

## 📚 Documentation

| Guide | Description |
|-------|-------------|
| [CONFIGURATION_GUIDE.md](CONFIGURATION_GUIDE.md) | Configuration & Background Service setup |
| [API_GUIDE.md](API_GUIDE.md) | Swagger UI & CRUD API reference |
| [DASHBOARD_GUIDE.md](DASHBOARD_GUIDE.md) | Web dashboard usage guide |
| [PAGINATION_GUIDE.md](PAGINATION_GUIDE.md) | Pagination & search implementation |
| [OPTIMIZATION_GUIDE.md](OPTIMIZATION_GUIDE.md) | Performance optimization details |

## 🎁 Features Roadmap

- [ ] Support cho Map<int, string> API
- [ ] Bucket CRUD API
- [ ] Pub/Sub monitoring
- [ ] Export/Import functionality
- [ ] Real-time updates (SignalR)
- [ ] TTL support per entry
- [ ] Authentication & Authorization
- [ ] Metrics & Analytics

## 🤝 Contributing

Contributions welcome! Please read contributing guidelines first.

## 📄 License

MIT License - feel free to use in your projects.

## 👨‍💻 Author

Ninh - CacheManager Project

## 🙏 Acknowledgments

- ASP.NET Core Team
- StackExchange.Redis
- Swashbuckle (Swagger)
- Redis Community

---

**Built with ❤️ using .NET 9.0 & Redis**

🌟 Star this repo if you find it useful!
