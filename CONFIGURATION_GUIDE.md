# Configuration Guide

## 📋 Overview

CacheManager now supports **configuration-driven setup** using `appsettings.json` and **background service registration** for maps and buckets.

## 🔧 Configuration

### appsettings.json

Add CacheManager configuration to your `appsettings.json`:

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

### Configuration Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RedisConnectionString` | string | `"localhost:6379"` | Redis server connection string |
| `RedisDatabase` | int | `0` | Redis database number (0-15) |
| `BatchWaitTimeSeconds` | int | `5` | Wait time before flushing batch updates |
| `DashboardPath` | string | `"/cache-manager"` | URL path for dashboard |

---

## 🚀 Usage in ASP.NET Core

### 1. Add CacheManager Services

**Option A: From appsettings.json (Recommended)**

```csharp
using CacheManager;
using CacheManager.Core;

var builder = WebApplication.CreateBuilder(args);

// Read configuration from appsettings.json "CacheManager" section
builder.Services.AddCacheManager(builder.Configuration);
```

**Option B: Manual Configuration (Legacy)**

```csharp
builder.Services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.RedisDatabase = 0;
    options.BatchWaitTimeSeconds = 5;
    options.DashboardPath = "/cache-manager";
});
```

### 2. Register Maps and Buckets via Background Service

Create a background service to register your maps and buckets:

**File: `Services/CacheRegistrationBackgroundService.cs`**

```csharp
using CacheManager.Core;

namespace YourApp.Services;

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
        // Register maps with typed keys and values
        builder.CreateMap<string, string>("user-sessions");
        builder.CreateMap<int, string>("user-data");
        builder.CreateMap<Guid, UserProfile>("user-profiles");

        // Register buckets (list-like structures)
        builder.CreateBucket<string>("logs");
        builder.CreateBucket<AuditEvent>("audit-events");
    }
}
```

**Register in Program.cs:**

```csharp
// Register background service for cache registration
builder.Services.AddHostedService<CacheRegistrationBackgroundService>();
```

### 3. Enable Dashboard

**Option A: Use path from appsettings.json**

```csharp
var app = builder.Build();

// Automatically uses DashboardPath from configuration
app.UseCacheManagerDashboard();
```

**Option B: Override with custom path**

```csharp
// Use custom path, ignoring appsettings.json
app.CacheManagerView("/my-custom-path");
```

---

## 📝 Complete Example

### Program.cs

```csharp
using CacheManager;
using CacheManager.Core;
using YourApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CacheManager from appsettings.json
builder.Services.AddCacheManager(builder.Configuration);

// Register cache configuration via background service
builder.Services.AddHostedService<CacheRegistrationBackgroundService>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

// Enable dashboard (reads path from appsettings.json)
app.UseCacheManagerDashboard();

app.MapRazorPages();
app.Run();
```

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "CacheManager": {
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 5,
    "DashboardPath": "/cache-manager"
  }
}
```

### appsettings.Development.json (Override for Development)

```json
{
  "CacheManager": {
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 1,
    "BatchWaitTimeSeconds": 2
  }
}
```

### appsettings.Production.json (Production Settings)

```json
{
  "CacheManager": {
    "RedisConnectionString": "production-redis.example.com:6379,password=your-password,ssl=true",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 10
  }
}
```

---

## 🎯 Background Service Details

### CacheManagerRegistrationService

Base class for registering maps and buckets in a background service.

**Benefits:**
- ✅ Runs on application startup
- ✅ Separates registration logic from Program.cs
- ✅ Centralized cache configuration
- ✅ Logging support
- ✅ Async execution (non-blocking)

**Implementation:**

```csharp
public abstract class CacheManagerRegistrationService : BackgroundService
{
    protected abstract void ConfigureCache(IRegisterBuilder builder);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Logs: "Starting CacheManager registration..."
        var builder = _registerService.RegisterBuilder();
        ConfigureCache(builder);
        builder.Build();
        // Logs: "CacheManager registration completed successfully"
    }
}
```

### Custom Implementation Example

```csharp
public class CacheRegistrationBackgroundService : CacheManagerRegistrationService
{
    private readonly IConfiguration _configuration;

    public CacheRegistrationBackgroundService(
        ICacheRegisterService registerService,
        ILogger<CacheManagerRegistrationService> logger,
        IConfiguration configuration)
        : base(registerService, logger)
    {
        _configuration = configuration;
    }

    protected override void ConfigureCache(IRegisterBuilder builder)
    {
        // Dynamic registration based on configuration
        var enableUserCache = _configuration.GetValue<bool>("Features:UserCache");
        if (enableUserCache)
        {
            builder.CreateMap<string, string>("user-sessions");
        }

        var enableAudit = _configuration.GetValue<bool>("Features:AuditLog");
        if (enableAudit)
        {
            builder.CreateBucket<string>("audit-logs");
        }
    }
}
```

---

## 🔄 Migration from Old Approach

### Before (Old Way)

```csharp
// ❌ Manual configuration in code
builder.Services.AddCacheManager(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.RedisDatabase = 0;
});

var app = builder.Build();

// ❌ Manual registration after app build
var registerService = app.Services.GetRequiredService<ICacheRegisterService>();
registerService.RegisterBuilder()
    .CreateMap<string, string>("user-sessions")
    .Build();

// ❌ Manual dashboard path
app.CacheManagerView("/cache-manager");
```

### After (New Way)

```csharp
// ✅ Configuration from appsettings.json
builder.Services.AddCacheManager(builder.Configuration);

// ✅ Background service registration
builder.Services.AddHostedService<CacheRegistrationBackgroundService>();

var app = builder.Build();

// ✅ Auto dashboard path from config
app.UseCacheManagerDashboard();
```

---

## 📊 Environment-Specific Configuration

### Development Environment

```json
// appsettings.Development.json
{
  "CacheManager": {
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 1,
    "BatchWaitTimeSeconds": 2,
    "DashboardPath": "/cache-manager"
  }
}
```

### Staging Environment

```json
// appsettings.Staging.json
{
  "CacheManager": {
    "RedisConnectionString": "staging-redis.internal:6379,password=${REDIS_PASSWORD}",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 5,
    "DashboardPath": "/admin/cache"
  }
}
```

### Production Environment

```json
// appsettings.Production.json
{
  "CacheManager": {
    "RedisConnectionString": "redis.prod.example.com:6380,password=${REDIS_PASSWORD},ssl=true,abortConnect=false",
    "RedisDatabase": 0,
    "BatchWaitTimeSeconds": 10,
    "DashboardPath": "/internal/cache-dashboard"
  }
}
```

---

## 🔐 Using Azure Key Vault / Environment Variables

### With Environment Variables

```json
// appsettings.json
{
  "CacheManager": {
    "RedisConnectionString": "${REDIS_CONNECTION_STRING}",
    "RedisDatabase": 0
  }
}
```

**Set environment variable:**

```bash
# Linux/Mac
export REDIS_CONNECTION_STRING="production-redis:6379,password=secret"

# Windows
set REDIS_CONNECTION_STRING=production-redis:6379,password=secret
```

### With Azure Key Vault

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault
if (builder.Environment.IsProduction())
{
    var keyVaultEndpoint = builder.Configuration["KeyVault:Endpoint"];
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultEndpoint),
        new DefaultAzureCredential());
}

// CacheManager will read from Key Vault secrets
builder.Services.AddCacheManager(builder.Configuration);
```

**Key Vault Secret:**
- Name: `CacheManager--RedisConnectionString`
- Value: `production-redis:6379,password=...`

---

## 🧪 Testing

### Unit Test Example

```csharp
[Fact]
public void ConfigureCache_ShouldRegisterMaps()
{
    // Arrange
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.Test.json")
        .Build();
    
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddCacheManager(config);
    services.AddHostedService<CacheRegistrationBackgroundService>();
    
    var provider = services.BuildServiceProvider();
    
    // Act
    var storage = provider.GetRequiredService<ICacheStorage>();
    
    // Assert
    Assert.Contains("user-sessions", storage.GetAllMapNames());
}
```

---

## 📚 Related Documentation

- [README.md](README.md) - Project overview
- [API_GUIDE.md](API_GUIDE.md) - Swagger and CRUD APIs
- [DASHBOARD_GUIDE.md](DASHBOARD_GUIDE.md) - Dashboard usage
- [OPTIMIZATION_GUIDE.md](OPTIMIZATION_GUIDE.md) - Performance optimization

---

## 💡 Best Practices

1. ✅ **Use appsettings.json** for all environment-specific configuration
2. ✅ **Use Background Service** for cache registration (cleaner separation)
3. ✅ **Use Environment Variables** for sensitive data (connection strings, passwords)
4. ✅ **Use `UseCacheManagerDashboard()`** to auto-read dashboard path from config
5. ✅ **Use different databases** for different environments (dev: 1, prod: 0)
6. ✅ **Tune `BatchWaitTimeSeconds`** based on your write patterns
7. ✅ **Secure dashboard path** in production (e.g., `/internal/cache-admin`)

---

Created: 2024
Author: CacheManager Team
