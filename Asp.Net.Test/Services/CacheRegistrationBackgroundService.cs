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

        // Register buckets
        builder.CreateBucket<string>("logs");
    }
}