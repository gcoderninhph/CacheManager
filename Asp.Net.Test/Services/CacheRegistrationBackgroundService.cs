using CacheManager.Core;
using Asp.Net.Test.Models;

namespace Asp.Net.Test.Services;

/// <summary>
/// Background service to register maps and buckets on application startup
/// </summary>
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
        // Register maps
        builder.CreateMap<string, string>("user-sessions");
        builder.CreateMap<int, string>("user-data");
        builder.CreateMap<string, UserInfo>("user-info");
        
        // Example: Map with TTL - Items expire after 5 minutes of inactivity
        // Uncommment to enable:
        // builder.CreateMap<string, string>(
        //     mapName: "temp-sessions",
        //     expiration: null,              // Map itself doesn't expire
        //     itemTtl: TimeSpan.FromMinutes(5) // Items auto-delete after 5 min idle
        // );

        // Register buckets
        builder.CreateBucket<string>("logs");
    }
}