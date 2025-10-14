using CacheManager;
using CacheManager.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CacheManager API", Version = "v1" });
});

// Add CacheManager
builder.Services.AddCacheManager(options =>
{
	options.RedisConnectionString = "localhost:6379";
	options.RedisDatabase = 0;
	options.BatchWaitTimeSeconds = 5;
});

var app = builder.Build();

// Register maps and buckets
var registerService = app.Services.GetRequiredService<ICacheRegisterService>();
registerService.RegisterBuilder()
	.CreateMap<string, string>("user-sessions")
	.CreateMap<int, string>("user-data")
	.CreateBucket<string>("logs")
	.Build();

// Configure Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CacheManager API v1");
    });
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.CacheManagerView("/cache-manager");

// ==================== CRUD API for Map ====================

/// <summary>
/// Get a value from map by key
/// </summary>
app.MapGet("/api/map/{mapName}/{key}", async (string mapName, string key, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        var value = await map.GetValueAsync(key);
        return Results.Ok(new { key, value });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = $"Key '{key}' not found in map '{mapName}'" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("GetMapValue")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Set a value in map
/// </summary>
app.MapPost("/api/map/{mapName}/{key}", async (string mapName, string key, string value, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        await map.SetValueAsync(key, value);
        return Results.Ok(new { message = "Value set successfully", key, value });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("SetMapValue")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Update a value in map (same as Set)
/// </summary>
app.MapPut("/api/map/{mapName}/{key}", async (string mapName, string key, string value, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        await map.SetValueAsync(key, value);
        return Results.Ok(new { message = "Value updated successfully", key, value });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("UpdateMapValue")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Delete all data in map
/// </summary>
app.MapDelete("/api/map/{mapName}", async (string mapName, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        await map.ClearAsync();
        return Results.Ok(new { message = $"Map '{mapName}' cleared successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("ClearMap")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Get all values from map (with pagination)
/// </summary>
app.MapGet("/api/map/{mapName}", async (string mapName, ICacheStorage storage, int page = 1, int pageSize = 20) =>
{
    try
    {
        var mapInstance = storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return Results.NotFound(new { error = $"Map '{mapName}' not found" });
        }

        var method = mapInstance.GetType().GetMethod("GetAllEntriesAsync");
        if (method != null)
        {
            var task = method.Invoke(mapInstance, null) as Task;
            if (task != null)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                var allEntries = resultProperty?.GetValue(task) as IEnumerable<object>;
                
                if (allEntries != null)
                {
                    var entriesList = allEntries.ToList();
                    var totalCount = entriesList.Count;
                    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                    
                    var pagedEntries = entriesList
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                    
                    return Results.Ok(new 
                    { 
                        mapName,
                        entries = pagedEntries,
                        pagination = new
                        {
                            currentPage = page,
                            pageSize,
                            totalCount,
                            totalPages
                        }
                    });
                }
            }
        }
        
        return Results.Problem("Unable to retrieve map data");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("GetAllMapValues")
.WithTags("Map CRUD")
.WithOpenApi();

// ==================== Test Data Endpoint ====================

/// <summary>
/// Add sample test data to maps
/// </summary>
app.MapGet("/test/add-data", async (ICacheStorage storage) =>
{
    var userSessions = storage.GetMap<string, string>("user-sessions");
    
    // Add more than 20 records to test pagination
    for (int i = 1; i <= 50; i++)
    {
        await userSessions.SetValueAsync($"user{i}", $"session-token-{Guid.NewGuid().ToString().Substring(0, 8)}");
    }
    
    var userData = storage.GetMap<int, string>("user-data");
    for (int i = 1; i <= 30; i++)
    {
        await userData.SetValueAsync(i, $"{{\"name\":\"User{i}\",\"email\":\"user{i}@example.com\"}}");
    }
    
    return Results.Json(new { success = true, message = "50 user-sessions and 30 user-data records added!" });
})
.WithName("AddTestData")
.WithTags("Testing")
.WithOpenApi();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

